using UnityEngine;
using Game.Character;

namespace Game.SkillSystem
{
    /// <summary>
    /// Controls character movement behavior during skill casting.
    /// 方案 C：movement 由 graphData 中的 MovementData 节点控制，
    /// 此控制器降级为简单的速度管理工具。
    /// </summary>
    public class SkillMovementController : MonoBehaviour
    {
        private ICharacterMotor _motor;
        private float _defaultSpeedMultiplier = 1f;

        void Awake()
        {
            _motor = GetComponent<ICharacterMotor>();
            if (_motor != null)
                _defaultSpeedMultiplier = _motor.speedMultiplier;
        }

        /// <summary>
        /// 方案 C：movement 由 graphData 中的 MovementData 节点控制，
        /// 此方法保留签名供兼容，默认恢复全速。
        /// </summary>
        public void ApplySkillMovement(SkillData skill)
        {
            if (_motor == null) return;
            // movement 由节点系统(SkillRunner/MovementData)控制，此处不干预
        }

        /// <summary>
        /// Restore normal movement speed.
        /// Call this when skill cast completes or is cancelled.
        /// </summary>
        public void ClearSkillMovement()
        {
            if (_motor == null) return;
            _motor.speedMultiplier = _defaultSpeedMultiplier;
        }

        /// <summary>
        /// Directly set a speed multiplier value (for external systems).
        /// </summary>
        public void SetSpeedMultiplier(float multiplier)
        {
            if (_motor == null) return;
            _motor.speedMultiplier = multiplier;
        }
    }
}
