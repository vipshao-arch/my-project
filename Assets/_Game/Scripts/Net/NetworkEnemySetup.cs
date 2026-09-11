using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;
using Game.Character;
using Game.SkillSystem;

namespace Game.Net
{
    /// <summary>
    /// 敌人联机装配(S2-3,2026-07-30):挂敌人 prefab 根节点。
    ///
    /// Host 权威架构:
    ///   Host(IsServer):EnemyAI/EnemyMotor/NavMeshAgent/SkillAnimPlayer 全部本机跑,
    ///     位置经服务器权威 NetworkTransform 广播、动画参数经 NetworkAnimator 广播;
    ///   Client:禁用 AI/Motor/NavMeshAgent/SkillAnimPlayer,Rigidbody kinematic,
    ///     表现完全由同步驱动。HitDetector 保留:本地玩家攻击仍需在客户端检测到接触,
    ///     伤害数值由 S2-4 命中权威链路在 Host 复核后落地。
    /// </summary>
    public class NetworkEnemySetup : NetworkBehaviour
    {
        // ── 血量同步(S2-4):Host 写,Client 镜像进 EnemyMotor 供血条/表现 ──
        private NetworkVariable<float> _netHealth;
        // ── 死亡同步(A2):Host 写,Client 镜像尸体表现 ──
        private NetworkVariable<bool> _netDead;
        private EnemyMotor _motor;

        void Awake()
        {
            _motor = GetComponent<EnemyMotor>();
            _netHealth = new NetworkVariable<float>(0f);
            _netDead = new NetworkVariable<bool>(false);
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer)
            {
                // Host:保持现状,并把血量/死亡写入同步通道
                if (_motor != null)
                {
                    // 初值写 maxHealth:动态 Spawn 的敌人必然满血出生;
                    // EnemyMotor.Start 才 currentHealth=maxHealth,OnNetworkSpawn 先于 Start,
                    // 读 currentHealth 会拿到字段初始 0 → 客户端灰条(实测坑 07-31)
                    _netHealth.Value = _motor.maxHealth;
                    _motor.OnHealthChanged += OnHostHealthChanged;
                    _motor.OnDeath += OnHostDeath;
                }
                return;
            }

            // ── Client:表现专用 ──
            Disable<EnemyAI>();
            Disable<EnemyMotor>();
            Disable<SkillAnimPlayer>();
            Disable<WeaponSwitcher>();
            SetupClientHealthBar();

            var agent = GetComponent<NavMeshAgent>();
            if (agent != null) agent.enabled = false;

            var rb = GetComponent<Rigidbody>();
            if (rb != null) rb.isKinematic = true;

            if (_motor != null)
            {
                _motor.SetHealthFromNetwork(_netHealth.Value);
                _netHealth.OnValueChanged += OnNetHealthChanged;
                // 中途加入时敌人已是尸体:立即镜像;否则等事件
                if (_netDead.Value) _motor.SetDeadFromNetwork();
                _netDead.OnValueChanged += OnNetDeadChanged;
            }
        }

        public override void OnNetworkDespawn()
        {
            if (_motor != null)
            {
                _motor.OnHealthChanged -= OnHostHealthChanged;
                _motor.OnDeath -= OnHostDeath;
            }
            _netHealth.OnValueChanged -= OnNetHealthChanged;
            _netDead.OnValueChanged -= OnNetDeadChanged;
        }

        private void OnHostHealthChanged(float hp) => _netHealth.Value = hp;
        private void OnNetHealthChanged(float prev, float next) => _motor.SetHealthFromNetwork(next);
        private void OnHostDeath() => _netDead.Value = true;
        private void OnNetDeadChanged(bool prev, bool next) { if (next) _motor.SetDeadFromNetwork(); }

        /// <summary>
        /// 客户端补建头顶血条:EnemyHealthBar 原由 EnemyAI.Start 运行时自动添加,
        /// 客户端 EnemyAI 已禁用(Start 不会执行),需在此补建;
        /// 数据源 = EnemyMotor(由上方 _netHealth 镜像驱动,SetHealthFromNetwork 派事件)。
        /// </summary>
        private void SetupClientHealthBar()
        {
            var ai = GetComponent<EnemyAI>();
            if (ai != null && !ai.showHealthBar) return;
            var hb = GetComponent<EnemyHealthBar>();
            if (hb == null) hb = gameObject.AddComponent<EnemyHealthBar>();
            if (ai != null)
            {
                hb.barWidth     = ai.healthBarWidth;
                hb.barHeight    = ai.healthBarHeight;
                hb.heightOffset = ai.healthBarHeightOffset;
                hb.canvasScale  = ai.healthBarScale;
            }
        }

        private void Disable<T>() where T : Behaviour
        {
            var c = GetComponent<T>();
            if (c != null) c.enabled = false;
        }
    }
}
