using System.Collections;
using Unity.Netcode;
using UnityEngine;
using Game.Character;

namespace Game.Net
{
    /// <summary>
    /// 对战模式管理(阶段三 3.1/3.2 + 阵营版,2026-07-31):Host 权威。
    ///
    /// 模式(_mode):0=合作 PvE(全员同队,禁友伤) 1=FFA 大乱斗(各自为战) 2=团队 PvP(A/B 两队)。
    ///
    /// 职责:
    ///   模式切换(NetworkVariable 同步,GM 面板 Host 切换);
    ///   友伤门禁——同队禁伤(NetworkCombatRelay 复核点查 IsAlly);
    ///   阵营分配——团队模式按连接顺序奇偶分 A/B(与 NetworkSpawnPlacer 的
    ///     PlayerSpawn_A/B 奇偶分边天然对齐);回合中途加入补记分行(人少一队);
    ///   回合计时(默认 300s,到点按击杀判定:FFA 比个人,团队比队合计,平分=平局);
    ///   胜利判定(killsToWin 达数提前结束:FFA=个人,团队=队合计);
    ///   重生(3s 延迟,Owner 端自传送+满血复活,符合客户端权威移动);
    ///   记分板+回合结算+GM 调试面板(OnGUI)。
    ///
    /// 生命周期:Host 建房后由 NetworkGameManager 生成(注册 prefab,同 relay)。
    /// 血条着色:OnTeamsChanged 静态事件(模式/分边/管理器 spawn 时派发),
    ///   NetworkCharacterSetup 远端副本订阅刷新 CharacterHUD 颜色(同队绿/异队红)。
    /// </summary>
    public class NetworkMatchManager : NetworkBehaviour
    {
        public struct PvpScore : INetworkSerializable, System.IEquatable<PvpScore>
        {
            public ulong ClientId;
            public int Team;   // 团队模式:0=A 1=B;合作/FFA:-1
            public int Kills;
            public int Deaths;
            public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
            {
                s.SerializeValue(ref ClientId);
                s.SerializeValue(ref Team);
                s.SerializeValue(ref Kills);
                s.SerializeValue(ref Deaths);
            }
            public bool Equals(PvpScore other)
                => ClientId == other.ClientId && Team == other.Team
                   && Kills == other.Kills && Deaths == other.Deaths;
            public override int GetHashCode() => ClientId.GetHashCode();
        }

        public static NetworkMatchManager Instance { get; private set; }

        /// <summary>阵营/分边变化事件(血条着色等表现层订阅,各端本地派发)。</summary>
        public static event System.Action OnTeamsChanged;

        private readonly NetworkVariable<int> _mode  = new(0);   // 0=合作 1=FFA 2=团队
        private readonly NetworkVariable<int> _state = new(0);   // 0=空闲 1=回合中 2=回合结束
        private readonly NetworkVariable<float> _timeLeft = new(0f);
        // -2=回合未结束 -1=平局;FFA=胜者 ClientId;团队=胜队(0=A 1=B)
        private readonly NetworkVariable<long> _winner = new(-2L);
        private readonly NetworkList<PvpScore> _scores = new();

        private const float RoundSeconds = 300f;
        private const float RespawnDelay = 3f;
        [Tooltip("胜利所需击杀数:FFA=个人,团队=队合计(达到即提前结束;时间到按比分判定,平分=平局)")]
        public int killsToWin = 5;

        public override void OnNetworkSpawn()
        {
            Instance = this;
            _mode.OnValueChanged += OnModeChangedNet;
            _scores.OnListChanged += OnScoresChangedNet;
            if (IsServer && NetworkManager != null)
                NetworkManager.OnClientConnectedCallback += OnClientJoinedLate;
            // 初始同步不触发 OnValueChanged/OnListChanged,spawn 完成主动发一次,
            // 覆盖"玩家先于管理器 spawn"的着色时序
            OnTeamsChanged?.Invoke();
        }

        public override void OnNetworkDespawn()
        {
            if (Instance == this) Instance = null;
            _mode.OnValueChanged -= OnModeChangedNet;
            _scores.OnListChanged -= OnScoresChangedNet;
            if (IsServer && NetworkManager != null)
                NetworkManager.OnClientConnectedCallback -= OnClientJoinedLate;
        }

        private void OnModeChangedNet(int prev, int next) => OnTeamsChanged?.Invoke();
        private void OnScoresChangedNet(NetworkListEvent<PvpScore> e) => OnTeamsChanged?.Invoke();

        // ═══════════════════════════════════════════════════════════════
        // 阵营判定(友伤门禁/血条着色共用)
        // ═══════════════════════════════════════════════════════════════

        /// <summary>阵营:0=合作全员同队;1=FFA 各自为战;2=团队按记分表 Team(未入表=-1)。</summary>
        public int GetTeam(ulong clientId)
        {
            if (_mode.Value == 2)
            {
                for (int i = 0; i < _scores.Count; i++)
                    if (_scores[i].ClientId == clientId) return _scores[i].Team;
                return -1;
            }
            if (_mode.Value == 1) return (int)clientId + 1;   // 各自一队(+1 避免与合作的 0 混淆)
            return 0;
        }

        /// <summary>同盟判定:无管理器=合作=全同盟;自己=同盟(溅射不伤己)。</summary>
        public static bool IsAlly(ulong a, ulong b)
        {
            if (a == b) return true;
            if (Instance == null) return true;
            return Instance.GetTeam(a) == Instance.GetTeam(b);
        }

        /// <summary>当前模式(面板/冒烟菜单用):0=合作 1=FFA 2=团队;无管理器=0。</summary>
        public static int CurrentMode => Instance != null ? Instance._mode.Value : 0;

        private static string ModeName(int m) => m == 1 ? "FFA 大乱斗" : m == 2 ? "团队对战" : "合作 PvE";

        // ═══════════════════════════════════════════════════════════════
        // 回合计时(Host)
        // ═══════════════════════════════════════════════════════════════

        void Update()
        {
            if (!IsServer) return;
            if (_state.Value == 1)
            {
                _timeLeft.Value -= Time.deltaTime;
                if (_timeLeft.Value <= 0f) { _timeLeft.Value = 0f; EndRoundByScore(); }
            }
        }

        /// <summary>Host 切换模式(GM 面板):0=合作PvE 1=FFA 2=团队PvP。</summary>
        public void SetMode(int mode)
        {
            if (!IsServer) return;
            _mode.Value = Mathf.Clamp(mode, 0, 2);
            if (_mode.Value == 0) { _state.Value = 0; _winner.Value = -2L; _scores.Clear(); }
            else StartRound();
        }

        private void StartRound()
        {
            _scores.Clear();
            var ids = NetworkManager.Singleton.ConnectedClientsIds;
            for (int i = 0; i < ids.Count; i++)
                _scores.Add(new PvpScore { ClientId = ids[i], Team = _mode.Value == 2 ? i % 2 : -1 });
            _timeLeft.Value = RoundSeconds;
            _winner.Value = -2L;
            _state.Value = 1;
            ReviveAllInternal();      // Host 权威:满血+解除死亡(经 NetworkVariable 同步各端)
            RespawnAllClientRpc();    // 各端本地传送回出生位
        }

        /// <summary>Host:回合中途加入补记分行(团队分人少一侧);已有行跳过。</summary>
        private void OnClientJoinedLate(ulong clientId)
        {
            if (_state.Value != 1) return;
            for (int i = 0; i < _scores.Count; i++)
                if (_scores[i].ClientId == clientId) return;
            int team = -1;
            if (_mode.Value == 2)
            {
                int a = 0, b = 0;
                for (int i = 0; i < _scores.Count; i++)
                {
                    if (_scores[i].Team == 0) a++;
                    else if (_scores[i].Team == 1) b++;
                }
                team = a <= b ? 0 : 1;
            }
            _scores.Add(new PvpScore { ClientId = clientId, Team = team });
        }

        // ═══════════════════════════════════════════════════════════════
        // 击杀记分与胜利判定(Host)
        // ═══════════════════════════════════════════════════════════════

        /// <summary>Host:命中落地后由 NetworkCombatRelay 上报;玩家击杀玩家则记分+安排重生。</summary>
        public void ReportKill(GameObject killer, GameObject victim)
        {
            if (!IsServer || _state.Value != 1 || victim == null) return;
            var victimNo = victim.GetComponent<NetworkObject>();
            var killerNo = killer != null ? killer.GetComponent<NetworkObject>() : null;
            if (victimNo == null || !victimNo.IsPlayerObject) return;
            if (killerNo == null || !killerNo.IsPlayerObject) return;

            for (int i = 0; i < _scores.Count; i++)
            {
                var s = _scores[i];
                if (s.ClientId == killerNo.OwnerClientId) { s.Kills++;  _scores[i] = s; }
                if (s.ClientId == victimNo.OwnerClientId) { s.Deaths++; _scores[i] = s; }
            }
            // 胜利判定:FFA 个人达数 / 团队队合计达数 → 提前结束(回合结束不再安排重生)
            if (_mode.Value == 2)
            {
                int t = GetTeam(killerNo.OwnerClientId);
                if (t >= 0 && TeamKills(t) >= killsToWin) { EndRound(t); return; }
            }
            else
            {
                for (int i = 0; i < _scores.Count; i++)
                    if (_scores[i].ClientId == killerNo.OwnerClientId && _scores[i].Kills >= killsToWin)
                    { EndRound((long)killerNo.OwnerClientId); return; }
            }
            StartCoroutine(RespawnAfter(victimNo.OwnerClientId));
        }

        private int TeamKills(int team)
        {
            int sum = 0;
            for (int i = 0; i < _scores.Count; i++)
                if (_scores[i].Team == team) sum += _scores[i].Kills;
            return sum;
        }

        /// <summary>Host:时间到判定——团队比队合计,FFA 比个人最高(平分=平局)。</summary>
        private void EndRoundByScore()
        {
            if (_mode.Value == 2)
            {
                int a = TeamKills(0), b = TeamKills(1);
                EndRound(a == b ? -1L : (a > b ? 0L : 1L));
                return;
            }
            long winner = -1L; int best = -1; bool tie = false;
            for (int i = 0; i < _scores.Count; i++)
            {
                var s = _scores[i];
                if (s.Kills > best) { best = s.Kills; winner = (long)s.ClientId; tie = false; }
                else if (s.Kills == best) tie = true;
            }
            EndRound(tie || _scores.Count == 0 ? -1L : winner);
        }

        /// <summary>Host:回合结束收口——写胜者/切状态/全员起身看结算。</summary>
        private void EndRound(long winnerClientId)
        {
            if (_state.Value != 1) return;
            _state.Value = 2;
            _winner.Value = winnerClientId;
            ReviveAllInternal();      // 死者起身(满血+解除死亡,同步各端)
            RespawnAllClientRpc();    // 下局由"再来一局"重新 RespawnAll
        }

        // ═══════════════════════════════════════════════════════════════
        // 重生
        // ═══════════════════════════════════════════════════════════════

        private IEnumerator RespawnAfter(ulong clientId)
        {
            yield return new WaitForSeconds(RespawnDelay);
            if (_state.Value != 1) yield break;
            // Host 权威:满血 + 解除死亡(经 NetworkVariable 同步各端),再让该端本地传送
            if (NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out var client))
            {
                var stats = client.PlayerObject != null ? client.PlayerObject.GetComponent<CharacterStats>() : null;
                if (stats != null)
                {
                    stats.ResetToMax();
                    stats.SetDead(false);
                }
            }
            var pos = NetworkSpawnPlacer.GetSpawnPos(clientId, out _);
            RespawnClientRpc(pos, new ClientRpcParams
            {
                Send = new ClientRpcSendParams { TargetClientIds = new[] { clientId } }
            });
        }

        [ClientRpc]
        private void RespawnAllClientRpc()
        {
            var local = NetworkManager.Singleton.LocalClient?.PlayerObject;
            if (local == null) return;
            var setup = local.GetComponent<NetworkCharacterSetup>();
            if (setup != null)
                setup.RespawnLocal(NetworkSpawnPlacer.GetSpawnPos(NetworkManager.Singleton.LocalClientId, out _));
        }

        [ClientRpc]
        private void RespawnClientRpc(Vector3 pos, ClientRpcParams p = default)
        {
            var local = NetworkManager.Singleton.LocalClient?.PlayerObject;
            if (local == null) return;
            var setup = local.GetComponent<NetworkCharacterSetup>();
            if (setup != null) setup.RespawnLocal(pos);
        }

        // ═══════════════════════════════════════════════════════════════
        // GM 调试命令:联机各端可调——Host 直调;Client 经 ServerRpc 由 Host 权威执行
        // (开发期全员可用,正式版需加权限门禁)
        // ═══════════════════════════════════════════════════════════════

        /// <summary>GM:切换模式 0=合作 1=FFA 2=团队。</summary>
        public void GmSetMode(int mode) { if (IsServer) SetMode(mode); else GmSetModeServerRpc(mode); }
        /// <summary>GM:立即按当前比分结束回合。</summary>
        public void GmEndRound() { if (IsServer) { if (_state.Value == 1) EndRoundByScore(); } else GmEndRoundServerRpc(); }
        /// <summary>GM:回合时间加减(秒)。</summary>
        public void GmAddTime(float sec) { if (IsServer) { if (_state.Value == 1) _timeLeft.Value += sec; } else GmAddTimeServerRpc(sec); }
        /// <summary>GM:全员满血(Host 写 NetworkVariable,各端同步)。</summary>
        public void GmHealAll() { if (IsServer) HealAllInternal(); else GmHealAllServerRpc(); }
        /// <summary>GM:按场景出生标记再刷一批敌人(无标记场景=不刷)。</summary>
        public void GmSpawnEnemies() { if (IsServer) NetworkGameManager.SpawnSceneEnemies(); else GmSpawnEnemiesServerRpc(); }

        [ServerRpc(RequireOwnership = false)] private void GmSetModeServerRpc(int mode) => SetMode(mode);
        [ServerRpc(RequireOwnership = false)] private void GmEndRoundServerRpc() { if (_state.Value == 1) EndRoundByScore(); }
        [ServerRpc(RequireOwnership = false)] private void GmAddTimeServerRpc(float sec) { if (_state.Value == 1) _timeLeft.Value += sec; }
        [ServerRpc(RequireOwnership = false)] private void GmHealAllServerRpc() => HealAllInternal();
        [ServerRpc(RequireOwnership = false)] private void GmSpawnEnemiesServerRpc() => NetworkGameManager.SpawnSceneEnemies();

        private void HealAllInternal()
        {
            foreach (var id in NetworkManager.Singleton.ConnectedClientsIds)
            {
                var po = NetworkManager.Singleton.ConnectedClients[id].PlayerObject;
                var stats = po != null ? po.GetComponent<CharacterStats>() : null;
                if (stats != null) stats.Heal(stats.maxHealth);
            }
        }

        /// <summary>Host 权威:全员满血 + 解除死亡(经 NetworkVariable 同步各端死亡/复活动画)。</summary>
        private void ReviveAllInternal()
        {
            foreach (var id in NetworkManager.Singleton.ConnectedClientsIds)
            {
                var po = NetworkManager.Singleton.ConnectedClients[id].PlayerObject;
                var stats = po != null ? po.GetComponent<CharacterStats>() : null;
                if (stats != null)
                {
                    stats.ResetToMax();
                    stats.SetDead(false);
                }
            }
        }

        /// <summary>GM 面板内容(分页签调试窗调用;联机各端可见,Client 按钮经 ServerRpc 执行)。</summary>
        public void DrawGmPanel()
        {
            GUILayout.Label($"<b>GM 调试</b> 模式:{ModeName(_mode.Value)}{(IsServer ? "" : " (请求 Host 执行)")}");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("合作")) GmSetMode(0);
            if (GUILayout.Button("FFA")) GmSetMode(1);
            if (GUILayout.Button("团队")) GmSetMode(2);
            GUILayout.EndHorizontal();
            if (_state.Value == 1)
            {
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("结束回合")) GmEndRound();
                if (GUILayout.Button("+60s")) GmAddTime(60f);
                GUILayout.EndHorizontal();
            }
            if (GUILayout.Button("全员满血")) GmHealAll();
            if (GUILayout.Button("刷一批敌人")) GmSpawnEnemies();
        }

        // ═══════════════════════════════════════════════════════════════
        // 开发 GUI:GM 面板(Host) + 记分板 + 回合结算
        // ═══════════════════════════════════════════════════════════════

        void OnGUI()
        {
            // GM 面板已并入 NetworkGameManager 分页签调试窗(DrawGmPanel),此处不再绘制
            if (_mode.Value == 0) return;   // 合作模式无记分板/结算

            // ── 右上记分板 ──
            GUILayout.BeginArea(new Rect(Screen.width - 258, 8, 250, 260), GUI.skin.box);
            GUILayout.Label($"<b>{ModeName(_mode.Value)}</b> {(_state.Value == 1 ? "回合中" : _state.Value == 2 ? "回合结束" : "准备")} " +
                            $"{Mathf.CeilToInt(_timeLeft.Value)}s  目标{killsToWin}杀");
            if (_mode.Value == 2)
            {
                GUILayout.Label($"<b>A 队</b> 合计 {TeamKills(0)}");
                for (int i = 0; i < _scores.Count; i++)
                    if (_scores[i].Team == 0)
                        GUILayout.Label($"  玩家{_scores[i].ClientId}: 杀 {_scores[i].Kills} / 死 {_scores[i].Deaths}");
                GUILayout.Label($"<b>B 队</b> 合计 {TeamKills(1)}");
                for (int i = 0; i < _scores.Count; i++)
                    if (_scores[i].Team == 1)
                        GUILayout.Label($"  玩家{_scores[i].ClientId}: 杀 {_scores[i].Kills} / 死 {_scores[i].Deaths}");
            }
            else
            {
                for (int i = 0; i < _scores.Count; i++)
                    GUILayout.Label($"  玩家{_scores[i].ClientId}: 杀 {_scores[i].Kills} / 死 {_scores[i].Deaths}");
            }
            GUILayout.EndArea();

            // ── 回合结算面板(居中) ──
            if (_state.Value != 2) return;
            GUILayout.BeginArea(new Rect(Screen.width / 2f - 170, Screen.height / 2f - 120, 340, 240), GUI.skin.box);
            GUILayout.Label("<b>══ 回合结束 ══</b>");
            GUILayout.Space(6);
            if (_winner.Value == -1) GUILayout.Label("<b>平局</b>");
            else if (_mode.Value == 2) GUILayout.Label($"<b>{(_winner.Value == 0 ? "A" : "B")} 队获胜</b>");
            else GUILayout.Label($"<b>胜者:玩家 {_winner.Value}</b>");
            GUILayout.Space(6);
            for (int i = 0; i < _scores.Count; i++)
                GUILayout.Label($"  玩家{_scores[i].ClientId}: 杀 {_scores[i].Kills} / 死 {_scores[i].Deaths}");
            GUILayout.FlexibleSpace();
            if (IsServer)
            {
                if (GUILayout.Button("再来一局")) StartRound();
            }
            else GUILayout.Label("等待房主开始新回合…");
            GUILayout.EndArea();
        }
    }
}
