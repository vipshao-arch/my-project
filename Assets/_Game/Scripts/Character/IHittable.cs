using UnityEngine;

namespace Game.Character
{
    /// <summary>
    /// 可被外力击退的接口。
    /// 实现方：CharacterMotor（普通状态）、CharacterLadderAction（梯子状态由其内部转发）。
    /// 调用方：vPendulum、陷阱、爆炸等任何需要施加击退的系统。
    /// </summary>
    public interface IHittable
    {
        /// <summary>施加击退冲量。impulse 为世界空间速度向量，duration 为保护时长（秒）。</summary>
        void ReceiveKnockback(Vector3 impulse, float duration = 0.5f);
    }
}
