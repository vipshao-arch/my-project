using UnityEngine;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;

namespace Game.Net
{
    /// <summary>
    /// 联机引导(S2-1,2026-07-30):开发期 Host/Client 直连脚手架。
    ///
    /// 架构 = Listen Server(房主即 Host):StartHost=建房,StartClient=加入,Shutdown=离开。
    /// Play 时自动创建 NetworkManager(含 UnityTransport,默认 127.0.0.1:7777,DontDestroyOnLoad),
    /// 任何场景零配置可用;左上角开发 GUI 提供建房/加入/离开与连接状态。
    ///
    /// 玩家 prefab:编辑器下自动加载 graves_Character 并设为 PlayerPrefab
    /// (需其挂 NetworkObject 才会在连接时自动 Spawn,S2-2 装配)。
    ///
    /// Relay/Lobby 依赖 UGS 账号(测试环境缺失),当前以局域网直连替代,
    /// 上线前增量补回(UnityTransport 已就位,切换 Relay 仅改连接数据)。
    /// </summary>
    public class NetworkGameManager : MonoBehaviour
    {
        [Tooltip("Client 加入的目标地址(Host 忽略)")]
        public string address = "127.0.0.1";
        public ushort port = 7777;
        [Tooltip("开发期左上角直连 GUI")]
        public bool showDevGui = true;
        [Tooltip("延迟模拟(ms,0=关闭)。TR-4.4:单机模拟弱网联调")]
        public int simulatedLatencyMs = 0;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void EnsureExists()
        {
            if (NetworkManager.Singleton != null) return;

            var go = new GameObject("NetworkGameManager");
            DontDestroyOnLoad(go);

            // 顺序:先加 Transport。
            // 坑:NetworkConfig 是序列化字段,运行时 AddComponent 不会创建(编辑器才填),
            // 必须手动 new 并接 Transport,否则 StartHost → CanStart 内部 NRE。
            var transport = go.AddComponent<UnityTransport>();
            var nm = go.AddComponent<NetworkManager>();
            if (nm.NetworkConfig == null)
                nm.NetworkConfig = new NetworkConfig();
            nm.NetworkConfig.NetworkTransport = transport;
            go.AddComponent<NetworkGameManager>();

            // 玩家 prefab:统一经 Resources 加载(编辑器与独立包同路径; graves 变体,
            // 由 Character Kit → Setup → Network Setup（或 Tools/Net/Maintenance/Build Player Resources Entry） 生成,挂了 NetworkObject 才会自动 Spawn)
            // 玩家/敌人 prefab:统一经 Resources 加载(编辑器与独立包同路径;变体由
            // Character Kit → Setup → Network Setup（或 Tools/Net/Maintenance/Build Player Resources Entry） 生成,GlobalObjectIdHash 已写入)。
            // 注意:PlayerPrefab 也必须注册进 Prefabs——客户端生成远端玩家走
            // Prefabs.NetworkPrefabOverrideLinks 查找,PlayerPrefab 字段不在其中特判。
            // 玩家 prefab:遍历角色表注册全部联机入口(NetworkPlayer_<角色名> 变体,
            // 由 Character Kit → Network Setup / Tools/Net/Maintenance/Build Character Player Entries 生成,
            // 写入 GlobalObjectIdHash)。PlayerPrefab 默认设为第一个角色——房主建房前按选择改,
            // 加入方由 ConnectionApprovalCallback 按 PlayerPrefabHash 指定。
            int registeredPlayers = 0;
            for (int i = 0; i < CharacterRoster.Count; i++)
            {
                var characterPrefab = CharacterRoster.LoadPrefab(i);
                if (characterPrefab == null || characterPrefab.GetComponent<NetworkObject>() == null)
                {
                    Debug.LogWarning($"[Net] 角色 {CharacterRoster.All[i].displayName} 联机入口缺失:" +
                                     $"Resources/{CharacterRoster.All[i].resourcesPrefab}");
                    continue;
                }
                nm.NetworkConfig.Prefabs.Add(new NetworkPrefab { Prefab = characterPrefab });
                if (registeredPlayers == 0) nm.NetworkConfig.PlayerPrefab = characterPrefab;
                registeredPlayers++;
            }

            // 兼容兜底:角色表为空时回退旧单入口 NetworkPlayer(避免联机完全失效)
            if (registeredPlayers == 0)
            {
                var playerPrefab = Resources.Load<GameObject>("NetworkPlayer");
                if (playerPrefab != null && playerPrefab.GetComponent<NetworkObject>() != null)
                {
                    nm.NetworkConfig.PlayerPrefab = playerPrefab;
                    nm.NetworkConfig.Prefabs.Add(new NetworkPrefab { Prefab = playerPrefab });
                }
                else
                {
                    Debug.LogWarning("[Net] Resources/NetworkPlayer 缺失或无 NetworkObject,联机将不生成玩家;" +
                                     "执行 Character Kit → Setup → Network Setup（或 Tools/Net/Maintenance/Build Player Resources Entry）");
                }
            }

            // 敌人 prefab:注册进 NetworkPrefabs(Host 动态 Spawn,不走场景网络物体——
            // in-scene 实例继承 prefab 的 GlobalObjectIdHash=0,客户端无法映射)
            var enemyPrefab = Resources.Load<GameObject>("NetworkEnemy");
            if (enemyPrefab != null && enemyPrefab.GetComponent<NetworkObject>() != null)
                nm.NetworkConfig.Prefabs.Add(new NetworkPrefab { Prefab = enemyPrefab });
            else
                Debug.LogWarning("[Net] Resources/NetworkEnemy 缺失,联机将不生成敌人;" +
                                 "执行 Character Kit → Setup → Network Setup（或 Tools/Net/Maintenance/Build Player Resources Entry）");

            // 战斗路由 prefab:运行时 new GameObject+AddComponent 的 Spawn 带 hash=0,
            // 客户端无法实例化——必须注册 prefab
            var relayPrefab = Resources.Load<GameObject>("NetworkCombatRelay");
            if (relayPrefab != null && relayPrefab.GetComponent<NetworkObject>() != null)
                nm.NetworkConfig.Prefabs.Add(new NetworkPrefab { Prefab = relayPrefab });

            // 对战管理 prefab(阶段三)
            var matchPrefab = Resources.Load<GameObject>("NetworkMatchManager");
            if (matchPrefab != null && matchPrefab.GetComponent<NetworkObject>() != null)
                nm.NetworkConfig.Prefabs.Add(new NetworkPrefab { Prefab = matchPrefab });
        }

        // ── CLI 自动化(联调冒烟):-autohost / -autojoin,启动后自动建房/加入 ──
        private static string _autoAction;
        private float _autoTimer = -1f;

        /// <summary>建房模式:0=合作 PvE 1=FFA 大乱斗 2=团队 PvP(建房入口指定,建房后自动切换)。</summary>
        private static int _createMode = 0;

        void Start()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || nm.NetworkConfig == null) return;
            var transport = nm.NetworkConfig.NetworkTransport as UnityTransport;
            if (transport != null)
                transport.SetConnectionData(address, port);

            nm.OnClientDisconnectCallback += OnClientDisconnected;
            nm.OnClientConnectedCallback += OnClientConnected;
            // 必须显式开启 ConnectionApproval:NGO 1.x 里仅赋 ConnectionApprovalCallback
            // 不会自动开启,否则回调不触发、加入方的角色选择(PlayerPrefabHash)全部失效。
            nm.NetworkConfig.ConnectionApproval = true;
            nm.ConnectionApprovalCallback = ApprovalCheck;

            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "-autohost") _autoAction = "host";
                else if (args[i] == "-autojoin") _autoAction = "client";
            }
            if (_autoAction != null) _autoTimer = 0f;
        }

        private void OnClientConnected(ulong clientId)
        {
            var nm = NetworkManager.Singleton;
            if (nm.IsServer)
                Debug.Log($"[Net] 客户端接入:id={clientId} 在线={nm.ConnectedClientsIds.Count}");
        }

        /// <summary>
        /// 连接审批(Host/Server 侧):读取加入方 ConnectionData 里的角色 id,
        /// 指定对应角色的 PlayerPrefabHash,由 NGO 按该 prefab 自动 Spawn 玩家。
        /// </summary>
        private void ApprovalCheck(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
        {
            var nm = NetworkManager.Singleton;

            // 房主自身:ConnectionData 为空,角色由 NetworkConfig.PlayerPrefab
            // (ApplySelectedCharacterToHost 已按选择设置)决定;此处不设 PlayerPrefabHash,
            // 保持 null → 走默认 PlayerPrefab,避免覆盖房主自己的选择。
            if (request.Payload == null || request.Payload.Length == 0)
            {
                response.Approved = true;
                response.CreatePlayerObject = nm != null && nm.NetworkConfig.PlayerPrefab != null;
                response.Position = NetworkSpawnPlacer.GetHostAssignedSpawnPos(0, out _); // 房主=第 0 号出生位
                return;
            }

            string charId = CharacterSelection.ParseConnectionData(request.Payload);
            int idx = CharacterRoster.IndexOf(charId);
            var prefab = CharacterRoster.LoadPrefab(idx);
            var no = prefab != null ? prefab.GetComponent<NetworkObject>() : null;
            if (no != null)
            {
                response.Approved = true;
                response.CreatePlayerObject = true;
                response.PlayerPrefabHash = no.PrefabIdHash;
                response.Position = NetworkSpawnPlacer.GetHostAssignedSpawnPos((int)request.ClientNetworkId, out _);
                Debug.Log($"[Net] 批准连接 {request.ClientNetworkId} 使用角色 {CharacterRoster.All[idx].displayName}({charId})");
            }
            else
            {
                response.Approved = false;
                response.Reason = $"角色 {charId} 联机入口缺失,请先执行 Character Kit → Network Setup";
                Debug.LogError($"[Net] 拒绝连接:{response.Reason}");
            }
        }

        /// <summary>房主自己的角色:建房前把 PlayerPrefab 设为所选角色(本地玩家不走审批)。</summary>
        private static void ApplySelectedCharacterToHost(NetworkManager nm)
        {
            var prefab = CharacterRoster.LoadPrefab(CharacterSelection.SelectedIndex);
            if (prefab != null && prefab.GetComponent<NetworkObject>() != null)
                nm.NetworkConfig.PlayerPrefab = prefab;
        }

        private string _clientStatus = "";
        private float _connectWatchdog = -1f;
        private bool _wasConnected;   // A5:区分"连不上"与"连上后断开"(房主离开)
        private const float ConnectTimeoutSeconds = 8f;

        /// <summary>
        /// 连接超时看门狗:UTP 在"无房可连"时会静默重试且不派发断开回调
        /// (Windows ICMP 不可达还伴随 socket 刷屏),必须自行超时收口。
        /// </summary>
        void Update()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null) return;

            // CLI 自动化:延迟 2s 等场景/相机就绪后执行
            if (_autoAction != null && _autoTimer >= 0f)
            {
                _autoTimer += Time.unscaledDeltaTime;
                if (_autoTimer > 2f)
                {
                    string act = _autoAction;
                    _autoAction = null;
                    if (act == "host")
                    {
                        CleanupScenePlayer();
                        ApplySelectedCharacterToHost(nm);
                        if (TryStartHost(nm, port))
                        {
                            SpawnCombatRelay();
                            SpawnSceneEnemies();
                        }
                        Debug.Log("[Net] CLI autohost 已执行");
                    }
                    else
                    {
                        CleanupScenePlayer();
                        nm.NetworkConfig.ConnectionData = CharacterSelection.BuildConnectionData();
                        nm.StartClient();
                        Debug.Log("[Net] CLI autojoin 已发起");
                    }
                }
            }

            if (nm.IsConnectedClient) _wasConnected = true;
            bool connecting = nm.IsListening && !nm.IsHost && !nm.IsServer && !nm.IsConnectedClient;
            if (connecting)
            {
                _connectWatchdog = _connectWatchdog < 0f ? 0f : _connectWatchdog + Time.unscaledDeltaTime;
                if (_connectWatchdog > ConnectTimeoutSeconds)
                {
                    _connectWatchdog = -1f;
                    _clientStatus = "连接失败:确认房主已建房、地址端口正确";
                    Debug.LogWarning($"[Net] {_clientStatus}(超时 {ConnectTimeoutSeconds:F0}s,已自动断开)");
                    nm.Shutdown();
                }
            }
            else
            {
                _connectWatchdog = -1f;
            }
        }

        /// <summary>
        /// 本端 Client 连接失败/断开:给明确反馈并收掉连接,防底层重试刷屏。
        /// 预期场景:没点建房就点加入 / 地址端口不对 / 房主已离开 —— 属正常错误。
        /// </summary>
        private void OnClientDisconnected(ulong clientId)
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || nm.IsHost || nm.IsServer) return;
            if (clientId != nm.LocalClientId) return;   // 只处理本端
            // A5:曾连上后断开=房主离开/网络中断,提示可重进;未连上=连接失败
            _clientStatus = _wasConnected
                ? "与房主断开(房主离开或网络中断),可重新加入"
                : "连接失败/已断开:确认房主已建房、地址端口正确";
            _wasConnected = false;
            Debug.LogWarning($"[Net] {_clientStatus}");
            if (nm.IsListening) nm.Shutdown();
        }

#if UNITY_EDITOR
        /// <summary>开发冒烟:Play 模式下一键 Host(验证联机启动链路,等效 GUI 建房按钮)。</summary>
        [UnityEditor.MenuItem("Tools/Net/Debug Start Host")]
        private static void DebugStartHost()
        {
            if (NetworkManager.Singleton == null || NetworkManager.Singleton.IsListening) return;
            CleanupScenePlayer();
            var nm = NetworkManager.Singleton;
            var mgr = nm.GetComponent<NetworkGameManager>();
            if (TryStartHost(nm, mgr != null ? mgr.port : (ushort)7777))
            {
                SpawnCombatRelay();
                SpawnSceneEnemies();
                Debug.Log("[Net] Debug Host 已启动");
            }
        }

        /// <summary>开发冒烟:循环切换 合作→FFA→团队 模式(走 GM 分发:Host 直调/Client 经 ServerRpc)。</summary>
        [UnityEditor.MenuItem("Tools/Net/Debug Cycle Mode")]
        private static void DebugCycleMode()
        {
            if (NetworkMatchManager.Instance != null)
                NetworkMatchManager.Instance.GmSetMode((NetworkMatchManager.CurrentMode + 1) % 3);
        }

        /// <summary>开发冒烟:无房状态下点加入,验证连接失败的优雅处理(等效 GUI 加入按钮)。</summary>
        [UnityEditor.MenuItem("Tools/Net/Debug Start Client")]
        private static void DebugStartClient()
        {
            if (NetworkManager.Singleton == null || NetworkManager.Singleton.IsListening) return;
            var nm = NetworkManager.Singleton;
            var mgr = nm.GetComponent<NetworkGameManager>();
            if (mgr != null) mgr._clientStatus = "连接中...";
            CleanupScenePlayer();
            nm.StartClient();
            Debug.Log("[Net] Debug Client 已发起连接(无房时应随后报连接失败)");
        }
#endif

        /// <summary>
        /// 联机开场清理:销毁场景中预放的本地玩家(单机用),避免与 NGO 按 PlayerPrefab
        /// 自动 Spawn 的联机玩家重复。场景预放敌人保留(成为 Host 权威场景网络物体)。
        /// </summary>
        private static void CleanupScenePlayer()
        {
            foreach (var setup in FindObjectsByType<NetworkCharacterSetup>(FindObjectsSortMode.None))
            {
                var no = setup.GetComponent<NetworkObject>();
                if (no != null && !no.IsSpawned)
                {
                    // 关键(2026-09-08):先 SetActive(false) 立即移出 NGO 场景物体扫描,再 Destroy。
                    // 只 Destroy 会延迟到帧末生效,而 StartHost 是同步调用,其内部的
                    // ServerSpawnSceneObjectsOnStartSweep 会先扫到尚未销毁的场景角色,
                    // 导致"玩家 prefab + 场景预置角色"重复出生(重叠/卡阻挡)。
                    Debug.Log($"[Net] 清理场景预置角色 {setup.name}(单机遗留),联网由玩家 prefab 接管");
                    setup.gameObject.SetActive(false);
                    Destroy(setup.gameObject);
                }
            }
            // 场景预放敌人同样清除(两端都清):敌人由 Host 动态 Spawn 下发,避免
            // 场景网络物体 hash=0 与双份残留问题
            foreach (var setup in FindObjectsByType<NetworkEnemySetup>(FindObjectsSortMode.None))
            {
                var no = setup.GetComponent<NetworkObject>();
                if (no != null && !no.IsSpawned)
                {
                    setup.gameObject.SetActive(false);
                    Destroy(setup.gameObject);
                }
            }
        }

        /// <summary>Host 生成命中权威路由(S2-4):Spawn 后各端自动复制,ServerRpc 通道就绪。</summary>
        private static void SpawnCombatRelay()
        {
            if (NetworkCombatRelay.Instance == null)
            {
                var prefab = Resources.Load<GameObject>("NetworkCombatRelay");
                if (prefab == null)
                {
                    Debug.LogError("[Net] Resources/NetworkCombatRelay 缺失,伤害路由不可用;" +
                                   "执行 Character Kit → Setup → Network Setup（或 Tools/Net/Maintenance/Build Player Resources Entry）");
                }
                else
                {
                    var go = Object.Instantiate(prefab);
                    DontDestroyOnLoad(go);
                    go.GetComponent<NetworkObject>().Spawn();
                }
            }
            // 对战管理(阶段三,合作模式下空转)
            if (NetworkMatchManager.Instance == null)
            {
                var prefab = Resources.Load<GameObject>("NetworkMatchManager");
                if (prefab != null)
                {
                    var go = Object.Instantiate(prefab);
                    DontDestroyOnLoad(go);
                    go.GetComponent<NetworkObject>().Spawn();
                }
            }

            // PvP 建房入口:把建房前选定的模式应用到对战管理器
            // (Host 上 Spawn 是同步的,此处 Instance 已就绪)
            if (_createMode != 0 && NetworkMatchManager.Instance != null)
            {
                Debug.Log($"[Net] 建房模式:{(_createMode == 1 ? "FFA 大乱斗" : "团队 PvP")},切换中…");
                NetworkMatchManager.Instance.SetMode(_createMode);
            }
        }

        /// <summary>
        /// Host 动态生成敌人(替代场景预放):按脚手架出生标记落位,
        /// 无标记回退默认坐标。Spawn 后各端自动复制。public:GM 面板"刷一批敌人"复用。
        /// </summary>
        public static void SpawnSceneEnemies()
        {
            var enemyPrefab = Resources.Load<GameObject>("NetworkEnemy");
            if (enemyPrefab == null) return;
            // 无敌人标记的场景(对战竞技场)= 不生成敌人
            if (GameObject.Find("EnemySpawn_Melee") == null && GameObject.Find("EnemySpawn_HighGround") == null)
            {
                Debug.Log("[Net] 场景无 EnemySpawn 标记,跳过敌人生成(对战场景)");
                return;
            }
            SpawnEnemyAt(enemyPrefab, "EnemySpawn_Melee",       new Vector3(4f, 0.1f, 14f));
            SpawnEnemyAt(enemyPrefab, "EnemySpawn_HighGround",  new Vector3(0f, 3.1f, 20f));
        }

        private static void SpawnEnemyAt(GameObject prefab, string markerName, Vector3 fallback)
        {
            var marker = GameObject.Find(markerName);
            Vector3 pos = marker != null ? marker.transform.position : fallback;
            var go = Object.Instantiate(prefab, pos, Quaternion.identity);
            go.GetComponent<NetworkObject>().Spawn();
        }

        /// <summary>
        /// Host 启动(带空闲端口探测):bind 是异步的,StartHost 返回值抓不住 socket 失败;
        /// 先用 UDP 试绑定探测,从偏好端口起最多试 10 个,避开残留 socket 占用。
        /// </summary>
        private static bool TryStartHost(NetworkManager nm, ushort preferredPort)
        {
            var transport = (UnityTransport)nm.NetworkConfig.NetworkTransport;
            for (int i = 0; i < 10; i++)
            {
                ushort candidate = (ushort)(preferredPort + i);
                if (!IsUdpPortFree(candidate)) continue;
                if (transport != null)
                    // 关键(2026-09-10 修复跨机联机连不上)：Host 必须监听 0.0.0.0(所有网卡)。
                    // SetConnectionData 第三参 listenAddress 缺省时 = Address(默认 127.0.0.1)，
                    // 导致 Host 只监听回环地址，局域网其他机器连不进来(netstat 可见
                    // UDP 127.0.0.1:7777 而非 0.0.0.0:7777)。显式传 0.0.0.0 监听全部网卡。
                    transport.SetConnectionData(transport.ConnectionData.Address, candidate, "0.0.0.0");
                NetworkSpawnPlacer.PrepareRandomOrder();   // 房主建房前随机洗牌出生点(host 权威分配)
                if (nm.StartHost())
                {
                    if (candidate != preferredPort)
                        Debug.Log($"[Net] 端口 {preferredPort} 被占用,已自动改用 {candidate}");
                    return true;
                }
                break; // CanStart 失败(IsListening 等)不必再试端口
            }
            Debug.LogError($"[Net] 建房失败:端口 {preferredPort}~{preferredPort + 9} 均不可用或被占用。" +
                           "上次会话 socket 可能未释放,等待约 10 秒重试。");
            return false;
        }

        /// <summary>UDP 端口空闲探测(试绑定即释放)。</summary>
        private static bool IsUdpPortFree(int port)
        {
            try
            {
                var probe = new System.Net.Sockets.UdpClient(port);
                probe.Close();
                return true;
            }
            catch { return false; }
        }

        /// <summary>获取本机首个非回环 IPv4(跨机联机时房主告知对方填此地址)。</summary>
        private static string GetLocalIPv4()
        {
            try
            {
                var host = System.Net.Dns.GetHostEntry(System.Net.Dns.GetHostName());
                foreach (var ip in host.AddressList)
                    if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                        return ip.ToString();
            }
            catch { }
            return "";
        }

        /// <summary>退出 Play/对象销毁时兜底 Shutdown,释放端口防下次 bind 冲突。</summary>
        void OnDestroy()
        {
            var nm = NetworkManager.Singleton;
            if (nm != null && nm.IsListening)
                nm.Shutdown();
        }

        // ── 分页签调试窗(连接/GM):收起为一行小条,展开后页签切换,避免多面板重叠 ──
        private static int _debugTab;          // 0=连接 1=GM
        private static bool _debugExpanded = true;

        void OnGUI()
        {
            if (!showDevGui || NetworkManager.Singleton == null) return;
            var nm = NetworkManager.Singleton;

            if (!_debugExpanded)
            {
                GUILayout.BeginArea(new Rect(8, 8, 80, 28), GUI.skin.box);
                if (GUILayout.Button("调试 ▸")) _debugExpanded = true;
                GUILayout.EndArea();
                return;
            }

            bool hasGm = NetworkMatchManager.Instance != null;
            bool gmTab = _debugTab == 1 && hasGm;
            GUILayout.BeginArea(new Rect(8, 8, 250, gmTab ? 250 : 300), GUI.skin.box);

            // ── 页签条 ──
            GUILayout.BeginHorizontal();
            if (GUILayout.Button((_debugTab == 0 ? "<b>[连接]</b>" : "连接"), GUILayout.Width(64))) _debugTab = 0;
            GUI.enabled = hasGm;
            if (GUILayout.Button((gmTab ? "<b>[GM]</b>" : "GM"), GUILayout.Width(52))) _debugTab = 1;
            GUI.enabled = true;
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("▾", GUILayout.Width(26))) _debugExpanded = false;
            GUILayout.EndHorizontal();

            if (gmTab) NetworkMatchManager.Instance.DrawGmPanel();
            else DrawConnectionPanel(nm);

            GUILayout.EndArea();
        }

        /// <summary>连接页:建房/加入/状态/延迟模拟。</summary>
        private void DrawConnectionPanel(NetworkManager nm)
        {
            // ── 角色选择:建房/加入前先选定使用角色 ──
            GUILayout.Label("<b>选择角色</b>");
            GUILayout.BeginHorizontal();
            for (int i = 0; i < CharacterRoster.Count; i++)
            {
                string sel = CharacterSelection.SelectedIndex == i ? "▶ " : "";
                if (GUILayout.Button(sel + CharacterRoster.All[i].displayName))
                    CharacterSelection.SelectedIndex = i;
            }
            GUILayout.EndHorizontal();
            GUILayout.Space(4);

            if (!nm.IsListening)
            {
                // 本机局域网 IP 提示：跨机联机时，房主把此 IP 告诉对方，对方填进"地址"栏
                string localIp = GetLocalIPv4();
                GUILayout.Label($"本机 IP: {(string.IsNullOrEmpty(localIp) ? "未检测到" : localIp)}");
                GUILayout.Space(2);

                // ── 建房模式(建房入口):合作 PvE / FFA 大乱斗 / 团队 PvP ──
                GUILayout.BeginHorizontal();
                GUILayout.Label("模式", GUILayout.Width(34));
                if (GUILayout.Button(_createMode == 0 ? "▶合作" : "合作")) _createMode = 0;
                if (GUILayout.Button(_createMode == 1 ? "▶FFA" : "FFA")) _createMode = 1;
                if (GUILayout.Button(_createMode == 2 ? "▶团队" : "团队")) _createMode = 2;
                GUILayout.EndHorizontal();
                GUILayout.Space(2);

                if (GUILayout.Button("建房 (Host)"))
                {
                    CleanupScenePlayer();
                    ApplySelectedCharacterToHost(nm);
                    if (TryStartHost(nm, port))
                    {
                        SpawnCombatRelay();
                        SpawnSceneEnemies();
                    }
                }
                GUILayout.BeginHorizontal();
                address = GUILayout.TextField(address);
                string portStr = GUILayout.TextField(port.ToString(), GUILayout.Width(56));
                if (ushort.TryParse(portStr, out ushort p) && p != port)
                {
                    port = p;
                    var transport = (UnityTransport)nm.NetworkConfig.NetworkTransport;
                    if (transport != null) transport.SetConnectionData(address, port);
                }
                GUILayout.EndHorizontal();
                if (GUILayout.Button("加入 (Client)"))
                {
                    _clientStatus = "连接中...";
                    // 加入前把 GUI 填的地址/端口应用到 transport。此前仅"端口变化"才同步，
                    // 只改 IP(地址)不触发 SetConnectionData → 改了房主 IP 仍连 127.0.0.1。
                    var transport = (UnityTransport)nm.NetworkConfig.NetworkTransport;
                    if (transport != null) transport.SetConnectionData(address, port);
                    nm.NetworkConfig.ConnectionData = CharacterSelection.BuildConnectionData();
                    CleanupScenePlayer();
                    nm.StartClient();
                }
                if (!string.IsNullOrEmpty(_clientStatus))
                    GUILayout.Label(_clientStatus);
            }
            else
            {
                string role = nm.IsHost ? "Host(房主)" : nm.IsServer ? "Server" : "Client";
                GUILayout.Label($"状态:{role} 在线 {nm.ConnectedClientsIds.Count}");
                if (GUILayout.Button("离开 (Shutdown)")) { _clientStatus = ""; nm.Shutdown(); }
            }

            // ── TR-4.4 延迟模拟(弱网联调,单机可用) ──
            GUILayout.BeginHorizontal();
            GUILayout.Label("延迟ms", GUILayout.Width(48));
            string latStr = GUILayout.TextField(simulatedLatencyMs.ToString(), GUILayout.Width(56));
            if (int.TryParse(latStr, out int lat)) simulatedLatencyMs = Mathf.Max(0, lat);
            if (GUILayout.Button("应用"))
            {
                var transport = (UnityTransport)nm.NetworkConfig.NetworkTransport;
                if (simulatedLatencyMs > 0)
                    transport.SetDebugSimulatorParameters(simulatedLatencyMs, 0, 0);
                else
                    transport.SetDebugSimulatorParameters(0, 0, 0);
            }
            GUILayout.EndHorizontal();
        }
    }
}
