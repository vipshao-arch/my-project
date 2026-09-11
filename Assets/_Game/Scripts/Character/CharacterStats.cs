using UnityEngine;
using Unity.Netcode;

namespace Game.Character
{
    /// <summary>
    /// 角色属性组件(2026-07-30 从 CharacterMotor 拆出,联机数值同步落点;S2-4 联机化)。
    ///
    /// 拥有 HP/Stamina/Mana 的运行时状态与变化事件。
    /// 配置来源:CharacterMotor 上的序列化字段(Inspector / PlayerConfigData)在 Awake
    /// 时经 InitFrom 注入——prefab 与现有配置资产零改动。
    /// 恢复推进由 CharacterMotor.Update 驱动(与冲刺等行为耦合保持原时序),
    /// 本组件只做数据持有、变更与事件派发。
    ///
    /// 联机(S2-4):HP 经 NetworkVariable 由 Host 写、各端读;单机/未 Spawn 时
    /// 行为与旧版完全一致(本地字段)。伤害落地权在 Host(NetworkCombatRelay 路由),
    /// 客户端直接写会被拒绝。Stamina/Mana 暂为本地值(后续增量同步)。
    /// </summary>
    [AddComponentMenu("Game/Character System/Character Stats")]
    public class CharacterStats : NetworkBehaviour
    {
        // ── 联机 HP 通道(仅 Spawn 后生效;Host 写,各端读) ──
        private NetworkVariable<float> _netHealth;
        private float _localHealth;
        // ── 联机 Stamina/Mana 通道(A3,同 HP 模式) ──
        private NetworkVariable<float> _netStamina;
        private NetworkVariable<float> _netMana;
        private float _localStamina;
        private float _localMana;
        // ── 联机死亡状态(PvP 修复 2026-09-08):Host 权威写,各端监听做死亡/复活动画表现 ──
        private NetworkVariable<bool> _netDead;

        /// <summary>联机=已 Spawn(HP 走 NetworkVariable);单机=本地字段。</summary>
        private bool Networked => IsSpawned;

        void Awake()
        {
            _netHealth = new NetworkVariable<float>(0f);
            _netHealth.OnValueChanged += OnNetHealthChanged;
            _netStamina = new NetworkVariable<float>(0f);
            _netStamina.OnValueChanged += OnNetStaminaChanged;
            _netMana = new NetworkVariable<float>(0f);
            _netMana.OnValueChanged += OnNetManaChanged;
            _netDead = new NetworkVariable<bool>(false);
            _netDead.OnValueChanged += OnNetDeadChanged;
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer)
            {
                // InitFrom 已在 Awake 完成,Host 推初值
                _netHealth.Value  = _localHealth;
                _netStamina.Value = _localStamina;
                _netMana.Value    = _localMana;
            }
            else
            {
                // Client 用 Host 初值刷新一次 HUD
                onHealthChanged?.Invoke(_netHealth.Value);
                onStaminaChanged?.Invoke(_netStamina.Value);
                onManaChanged?.Invoke(_netMana.Value);
            }
        }

        private void OnNetHealthChanged(float prev, float next)
        {
            if (!IsServer) onHealthChanged?.Invoke(next); // Host 端事件已在写入处派发
        }
        private void OnNetStaminaChanged(float prev, float next)
        {
            if (!IsServer) onStaminaChanged?.Invoke(next);
        }
        private void OnNetManaChanged(float prev, float next)
        {
            if (!IsServer) onManaChanged?.Invoke(next);
        }
        private void OnNetDeadChanged(bool prev, bool next)
        {
            if (!IsServer) onDeadChanged?.Invoke(next); // Host 端事件在 SetDead 写入处已派发
        }

        [Header("Health")]
        public float maxHealth           = 100f;
        public float healthRecovery      = 0f;
        public float healthRecoveryDelay = 3f;

        [Header("Stamina")]
        public float maxStamina      = 100f;
        public float staminaRecovery = 20f;

        [Header("Mana")]
        public float maxMana      = 100f;
        public float manaRecovery = 0.3f;

        // ── 运行时状态(只读暴露,变更走方法) ──
        public float currentHealth  => Networked ? _netHealth.Value  : _localHealth;
        public float currentStamina => Networked ? _netStamina.Value : _localStamina;
        public float currentMana    => Networked ? _netMana.Value    : _localMana;

        // 恢复延迟计时器(由 TickXxxRecovery 读写)
        internal float healthRecoveryDelayTimer;
        internal float staminaRecoveryDelayTimer;
        internal float manaRecoveryDelayTimer;

        // ── 事件 ──
        public System.Action<float> onHealthChanged;
        public System.Action<float> onStaminaChanged;
        public System.Action<float> onManaChanged;
        /// <summary>死亡状态变化事件(dead)——Host 权威写,各端表现层订阅做死亡/复活动画。</summary>
        public System.Action<bool> onDeadChanged;

        /// <summary>从 CharacterMotor 的配置字段注入(Awake 调用,prefab 零改动)。</summary>
        public void InitFrom(CharacterMotor motor)
        {
            if (motor == null) return;
            maxHealth           = motor.maxHealth;
            healthRecovery      = motor.healthRecovery;
            healthRecoveryDelay = motor.healthRecoveryDelay;
            maxStamina          = motor.maxStamina;
            staminaRecovery     = motor.staminaRecovery;
            maxMana             = motor.maxMana;
            manaRecovery        = motor.manaRecovery;
            ResetToMax();
        }

        public void ResetToMax()
        {
            _localHealth   = maxHealth;
            _localStamina  = maxStamina;
            _localMana     = maxMana;
            if (Networked && IsServer)
            {
                _netHealth.Value  = _localHealth;
                _netStamina.Value = _localStamina;
                _netMana.Value    = _localMana;
            }
            healthRecoveryDelayTimer  = 0f;
            staminaRecoveryDelayTimer = 0f;
            manaRecoveryDelayTimer    = 0f;
        }

        /// <summary>
        /// 扣血(返回扣后血量)。死亡判定与表现由调用方(CharacterMotor)负责。
        /// 联机时仅 Host/单机可落地(客户端伤害由 NetworkCombatRelay 路由到 Host)。
        /// </summary>
        public float ApplyDamage(float damage)
        {
            if (Networked && !IsServer) return currentHealth;   // 防御:客户端不落地
            _localHealth = Mathf.Max(0f, currentHealth - damage);
            healthRecoveryDelayTimer = healthRecoveryDelay;
            if (Networked) _netHealth.Value = _localHealth;
            onHealthChanged?.Invoke(_localHealth);
            return _localHealth;
        }

        /// <summary>回血。联机时仅 Host/单机可落地。</summary>
        public void Heal(float amount)
        {
            if (Networked && !IsServer) return;
            _localHealth = Mathf.Min(maxHealth, currentHealth + amount);
            if (Networked) _netHealth.Value = _localHealth;
            onHealthChanged?.Invoke(_localHealth);
        }

        /// <summary>
        /// 死亡状态写入(Host 权威;单机直接本地派发)。
        /// 死亡/复活的最终真相:联机时经 _netDead 同步各端,各端表现层监听 onDeadChanged。
        /// </summary>
        public void SetDead(bool dead)
        {
            if (Networked && !IsServer) return;
            if (Networked) _netDead.Value = dead;
            onDeadChanged?.Invoke(dead);   // Host 端/单机本地派发;Client 端由 OnNetDeadChanged 派发
        }

        public void ReduceStamina(float amount, bool perSecond = false)
        {
            if (Networked && !IsServer) return;   // 联机时仅 Host/单机可落地
            _localStamina = Mathf.Max(0f, currentStamina - (perSecond ? amount * Time.deltaTime : amount));
            staminaRecoveryDelayTimer = 0.25f;
            if (Networked) _netStamina.Value = _localStamina;
            onStaminaChanged?.Invoke(_localStamina);
        }

        public void ReduceMana(float amount)
        {
            if (amount <= 0f) return;
            if (Networked && !IsServer) return;
            _localMana = Mathf.Max(0f, currentMana - amount);
            manaRecoveryDelayTimer = 1.0f;
            if (Networked) _netMana.Value = _localMana;
            onManaChanged?.Invoke(_localMana);
        }

        public void RestoreMana(float amount)
        {
            if (amount <= 0f) return;
            if (Networked && !IsServer) return;
            if (currentMana >= maxMana) return;
            _localMana = Mathf.Min(maxMana, currentMana + amount);
            if (Networked) _netMana.Value = _localMana;
            onManaChanged?.Invoke(_localMana);
        }

        public bool HasMana(float amount) => currentMana >= amount;

        // ── 恢复推进(由 CharacterMotor.Update 驱动,与原实现逐行等价) ──

        public void TickHealthRecovery()
        {
            if (currentHealth <= 0f || healthRecovery <= 0f) return;
            if (Networked && !IsServer) return;   // 联机时回复推进仅在 Host/单机
            if (healthRecoveryDelayTimer > 0f) { healthRecoveryDelayTimer -= Time.deltaTime; return; }
            if (currentHealth >= maxHealth) return;
            _localHealth = Mathf.Min(maxHealth, currentHealth + healthRecovery * Time.deltaTime);
            if (Networked) _netHealth.Value = _localHealth;
            onHealthChanged?.Invoke(_localHealth);
        }

        public void TickStaminaRecovery()
        {
            if (Networked && !IsServer) return;
            if (staminaRecoveryDelayTimer > 0f) { staminaRecoveryDelayTimer -= Time.deltaTime; return; }
            if (currentStamina >= maxStamina) return;
            _localStamina = Mathf.Min(maxStamina, currentStamina + staminaRecovery * Time.deltaTime);
            if (Networked) _netStamina.Value = _localStamina;
            onStaminaChanged?.Invoke(_localStamina);
        }

        public void TickManaRecovery()
        {
            if (Networked && !IsServer) return;
            if (manaRecoveryDelayTimer > 0f) { manaRecoveryDelayTimer -= Time.deltaTime; return; }
            if (currentMana >= maxMana) return;
            _localMana = Mathf.Min(maxMana, currentMana + manaRecovery * Time.deltaTime);
            if (Networked) _netMana.Value = _localMana;
            onManaChanged?.Invoke(_localMana);
        }
    }
}
