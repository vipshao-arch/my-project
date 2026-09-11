using UnityEngine;
using System;

namespace Game.Character
{
    /// <summary>
    /// 可受伤害接口。实现方：CharacterMotor（玩家）、EnemyMotor（敌人）。
    /// 调用方：HitDetector、VFXProjectile、陷阱等任何需要造成伤害的系统。
    /// </summary>
    public interface IDamageable
    {
        /// <summary>当前生命值</summary>
        float currentHealth { get; }

        /// <summary>最大生命值</summary>
        float maxHealth { get; }

        /// <summary>是否已死亡</summary>
        bool isDead { get; }

        /// <summary>生命值变化时触发 (newHealth)</summary>
        event Action<float> OnHealthChanged;

        /// <summary>死亡时触发</summary>
        event Action OnDeath;

        /// <summary>受到伤害。source 为伤害来源 GameObject，hitDir 为击中方向（用于击退/闪红）。</summary>
        void TakeDamage(float damage, GameObject source, Vector3 hitDir);

        /// <summary>治疗</summary>
        void Heal(float amount);
    }
}
