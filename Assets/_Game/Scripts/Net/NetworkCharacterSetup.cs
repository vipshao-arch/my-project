using Unity.Netcode;
using UnityEngine;
using Game.Character;
using Game.SkillSystem;

namespace Game.Net
{
    /// <summary>
    /// 玩家联机装配(S2-2,2026-07-30):挂 graves_Character prefab 根节点。
    ///
    /// OnNetworkSpawn 按 IsOwner 分流:
    ///   本地玩家(IsOwner):一切照旧——输入源/技能/移动全本地驱动(移动客户端权威),
    ///     NetworkAnimator 负责把动画参数广播给其他端;
    ///   远端副本:禁用全部逻辑组件(InputHandler/SkillController/SkillAnimPlayer/Motor/
    ///     LadderAction/ActionHandler/WeaponSwitcher),Rigidbody 转 kinematic,
    ///     位置由 ClientNetworkTransform 驱动、动画参数由 NetworkAnimator 驱动。
    ///     攻击/技能表现(S2-5 事件广播)与命中权威(S2-4)在后续任务接入。
    ///
    /// 说明:禁用组件后其 Update/输入读取整体停摆,Awake 中缓存的输入源不再起作用,
    /// 无需额外的输入源热替换。
    /// </summary>
    public class NetworkCharacterSetup : NetworkBehaviour
    {
        /// <summary>远端副本标记(非 Owner)。</summary>
        public bool IsRemoteProxy { get; private set; }

        // ── 阵营血条着色(远端副本):同队绿/异队红 ──
        private CharacterHUD _hud;
        private static readonly Color AllyGreen  = new Color(0.25f, 0.8f,  0.3f,  1f);
        private static readonly Color EnemyRed   = new Color(0.9f,  0.15f, 0.15f, 1f); // = CharacterHUD 默认

        // ── 远端副本死亡/复活表现(2026-09-08):死亡状态由 CharacterStats._netDead Host 权威同步 ──
        private CharacterStats _stats;
        private Animator _anim;

        public override void OnNetworkDespawn()
        {
            NetworkMatchManager.OnTeamsChanged -= RefreshHudTeamColor;
            if (_stats != null) _stats.onDeadChanged -= OnRemoteDeadChanged;
        }

        /// <summary>阵营着色刷新:OnNetworkSpawn 一次 + OnTeamsChanged 订阅(模式/分边/管理器 spawn)。</summary>
        private void RefreshHudTeamColor()
        {
            if (_hud == null || IsOwner) return;
            var nm = NetworkManager.Singleton;
            if (nm == null) return;
            _hud.SetHealthFillColor(NetworkMatchManager.IsAlly(nm.LocalClientId, OwnerClientId)
                ? AllyGreen : EnemyRed);
        }

        public override void OnNetworkSpawn()
        {
            // 防御(2026-09-08):场景里预置的玩家角色(单机遗留)自带 NetworkObject,
            // 若未被 CleanupScenePlayer 及时清除,会被 ServerSpawnSceneObjectsOnStartSweep
            // 当作场景物体重复 spawn。玩家应由玩家 prefab(NetworkPlayer_<角色>)生成,
            // 场景物体不是玩家 → 直接停用,避免与玩家 prefab 重复/重叠卡阻挡。
            if (NetworkObject.IsSceneObject == true)
            {
                gameObject.SetActive(false);
                return;
            }

            if (IsOwner)
            {
                // 本地玩家:出生点由 Host 在 ConnectionApproval 里随机洗牌分配,
                // 经 response.Position 已把角色放置到位(transform.position 即出生点)。
                // 此处只做安全校正(贴地/防卡),不再重新选点——选点统一到 Host 权威。
                bool trust = HasExplicitSpawnPoints();
                var final = ResolveSafeSpawnPos(transform.position, trust);
                Debug.Log($"[Spawn] 本地玩家(Owner={OwnerClientId}) 出生:host放置={transform.position} → final={final}" +
                          (trust ? " [显式出生点·只贴地]" : " [回退标记·含防卡散开]"));
                TeleportRigidbodySafe(final);
                StartCoroutine(LogPostSpawnPosition(final));
                return;
            }
            IsRemoteProxy = true;

            // ── 远端副本:表现专用 ──
            // 注意:SkillAnimPlayer 保持启用——S2-5b 远端表现施法需要它的 Update 驱动
            // 技能图(visualOnly:VFX/弹道真实生成,伤害全抑制);它自身不读输入、无本地施法入口。
            Disable<CharacterInputHandler>();
            Disable<SkillController>();
            Disable<CharacterMotor>();
            Disable<CharacterLadderAction>();
            Disable<CharacterActionHandler>();
            Disable<WeaponSwitcher>();
            Disable<HitDetector>();   // 双保险:远端不做任何命中发起

            var rb = GetComponent<Rigidbody>();
            if (rb != null) rb.isKinematic = true;

            // ── 远端头顶血条:用 CharacterMotor.Awake→EnsureHUD 挂的原生 CharacterHUD 三条
            // (读 CharacterStats 联机值,Start 晚于 OnNetworkSpawn,初始值已是 spawn 包同步值)。
            // 按阵营着色:同队绿/异队红;模式/分边变化经 OnTeamsChanged 刷新。
            _hud = GetComponent<CharacterHUD>();
            RefreshHudTeamColor();
            NetworkMatchManager.OnTeamsChanged += RefreshHudTeamColor;

            // 远端副本:死亡/复活动画表现(死亡状态由 CharacterStats._netDead Host 权威同步,
            // 与 CharacterMotor 本地 Die/Revive 解耦——远端 CharacterMotor 已 disable,动画走这里驱动)
            _stats = GetComponent<CharacterStats>();
            _anim = GetComponent<Animator>();
            if (_stats != null) _stats.onDeadChanged += OnRemoteDeadChanged;
        }

        /// <summary>远端副本死亡/复活动画(由 Host 权威 _netDead 驱动)。</summary>
        private void OnRemoteDeadChanged(bool dead)
        {
            if (_anim != null) _anim.SetBool(AnimatorParams.IsDead, dead);
        }

        /// <summary>联机对战(3.2):本地玩家重生(Owner 权威:自己传送自己+满血复活)。</summary>
        public void RespawnLocal(Vector3 pos)
        {
            if (!IsOwner) return;
            bool trust = HasExplicitSpawnPoints();
            TeleportRigidbodySafe(ResolveSafeSpawnPos(pos, trust));
            var motor = GetComponent<CharacterMotor>();
            if (motor != null) motor.Revive();
        }

        /// <summary>
        /// 刚体安全传送(2026-09-08):角色带非 kinematic 刚体(重力+插值+连续碰撞),
        /// 直接用 transform 设位,物理引擎下一帧可能用「spawn 瞬间残留的速度/内部位置」
        /// 把角色推回出生点之外。改为走 rb.position 传送 + 清空速度,杜绝二次位移。
        /// </summary>
        private void TeleportRigidbodySafe(Vector3 pos)
        {
            var rb = GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.position = pos;
                rb.rotation = Quaternion.identity;
                rb.velocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                return;
            }
            transform.SetPositionAndRotation(pos, Quaternion.identity);
        }

        /// <summary>出生后位置追踪:确认角色有没有被物理/其他逻辑从出生点挪走。</summary>
        private System.Collections.IEnumerator LogPostSpawnPosition(Vector3 expected)
        {
            for (int i = 0; i < 4; i++)
            {
                yield return new WaitForSeconds(0.5f);
                var rb = GetComponent<Rigidbody>();
                var drift = Vector3.Distance(transform.position, expected);
                Debug.Log($"[Spawn] +{(i + 1) * 0.5f:F1}s 位置={transform.position} " +
                          $"rb={(rb != null ? rb.position : transform.position)} " +
                          $"离出生点漂移={drift:F3}m");
            }
        }

        /// <summary>场景是否放有显式出生点(PlayerSpawnPoint)。</summary>
        private bool HasExplicitSpawnPoints()
        {
            var pts = Object.FindObjectsByType<PlayerSpawnPoint>(FindObjectsSortMode.None);
            return pts != null && pts.Length > 0;
        }

        /// <summary>
        /// 出生点安全校正(2026-09-08):
        ///   trustPoint=true  显式出生点(用户摆好的干净位置)——只向下贴地,不做水平散开,
        ///                     避免把角色从指定出生点挪走;
        ///   trustPoint=false 回退标记——贴地 + 防卡散开(避免标记埋地面/阻挡内部)。
        /// 跳过角色自身(含子 collider)与触发体。
        /// </summary>
        private Vector3 ResolveSafeSpawnPos(Vector3 raw, bool trustPoint = false)
        {
            if (trustPoint)
            {
                if (SnapDown(raw, out Vector3 ground)) return ground;
                return raw + Vector3.up * 0.5f; // 兜底:raw 上方靠重力落定
            }

            if (TryFindClearGround(raw, out Vector3 result))
                return result;
            return raw + Vector3.up * 0.5f; // 兜底:raw 上方靠重力落定
        }

        /// <summary>在 raw 附近向下找地面,并保证落点不压进实体(阻挡)。</summary>
        private bool TryFindClearGround(Vector3 raw, out Vector3 result)
        {
            // 第一优先:raw 正下方
            if (SnapDown(raw, out Vector3 ground) && !Blocked(ground))
            {
                result = ground;
                return true;
            }

            // 环形散开找干净点(避免吸附到阻挡顶面/压墙)
            for (int ring = 1; ring <= 4; ring++)
            {
                float r = ring * 1.5f;
                for (int k = 0; k < 8; k++)
                {
                    float a = k * (Mathf.PI * 0.25f);
                    var probe = raw + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * r;
                    if (SnapDown(probe, out Vector3 p) && !Blocked(p))
                    {
                        result = p;
                        return true;
                    }
                }
            }

            result = Vector3.zero;
            return false;
        }

        /// <summary>
        /// 从 pos 上方 8m 向下找最近的"能站人"表面,吸附到其上方 0.25m。
        /// RaycastAll 不保证顺序,故显式按 distance 升序排序后取第一个法线向上的
        /// 表面(地面/平台顶/阻挡顶),跳过法线向下的表面(天花板/横梁底面),避免吸到屋顶下。
        /// </summary>
        private bool SnapDown(Vector3 pos, out Vector3 result)
        {
            var hits = Physics.RaycastAll(pos + Vector3.up * 8f, Vector3.down, 16f, ~0, QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (var h in hits)
            {
                if (h.collider.GetComponentInParent<NetworkCharacterSetup>() != null) continue; // 跳过角色自身
                if (Vector3.Dot(h.normal, Vector3.up) < 0.5f) continue; // 跳过天花板/墙面/陡坡
                result = h.point + Vector3.up * 0.25f;
                return true;
            }
            result = Vector3.zero;
            return false;
        }

        /// <summary>落点是否压进实体(腰位半径 0.5m 球体检,忽略触发体与自身 collider)。</summary>
        private bool Blocked(Vector3 pos)
        {
            var hits = Physics.OverlapSphere(pos + Vector3.up * 1f, 0.5f, ~0, QueryTriggerInteraction.Ignore);
            foreach (var c in hits)
            {
                if (c.GetComponentInParent<NetworkCharacterSetup>() == this) continue; // 跳过自身
                Debug.Log($"[Spawn] Blocked @ {pos}:命中 {c.name} @ {c.transform.position}");
                return true;
            }
            return false;
        }

        /// <summary>
        /// 远端施法表现(S2-5):在自身 Animator 上复播同名状态。
        /// 多层 Controller 播放前必显式 SetLayerWeight(项目踩坑备忘)。
        /// </summary>
        public void PlayCastVisual(string stateName, int layerIndex)
        {
            if (IsOwner || string.IsNullOrEmpty(stateName)) return;
            var anim = GetComponent<Animator>();
            if (anim == null || layerIndex < 0 || layerIndex >= anim.layerCount) return;
            anim.SetLayerWeight(layerIndex, 1f);
            anim.CrossFadeInFixedTime(stateName, 0.15f, layerIndex);
        }

        private void Disable<T>() where T : Behaviour
        {
            var c = GetComponent<T>();
            if (c != null) c.enabled = false;
        }
    }
}
