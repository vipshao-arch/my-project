using System;
using UnityEngine;

namespace Game.Character
{
    /// <summary>
    /// 运动状态名配置(2026-07-30 建立)。
    ///
    /// 消除 CharacterMotor/CharacterLadderAction 中硬编码的 Animator 状态名
    /// ("Idle"/"Jump"/"JumpMove"/"Roll"/"Falling" 等)。
    /// 挂入 CharacterAnimSetAsset.locomotionStates;角色不配置时使用内置默认名。
    ///
    /// 注意:roll/landHigh/quickStop 同时用于状态识别(shortNameHash 比较),
    /// 改名必须与 Animator Controller 中的实际状态名一致。
    /// </summary>
    [Serializable]
    public class LocomotionStateNames
    {
        [Header("NormalState 层状态名")]
        public string idle     = "Idle";
        public string jump     = "Jump";
        public string jumpMove = "JumpMove";
        public string roll     = "Roll";
        public string landHigh = "LandHigh";
        public string quickStop = "QuickStop";
        public string falling  = "Falling";

        // ── 状态识别 hash(运行时惰性计算) ──
        [NonSerialized] private int _rollHash, _landHighHash, _quickStopHash;
        [NonSerialized] private bool _hashesReady;

        public int RollHash     { get { EnsureHashes(); return _rollHash; } }
        public int LandHighHash { get { EnsureHashes(); return _landHighHash; } }
        public int QuickStopHash { get { EnsureHashes(); return _quickStopHash; } }

        private void EnsureHashes()
        {
            if (_hashesReady) return;
            _rollHash      = Animator.StringToHash(roll);
            _landHighHash  = Animator.StringToHash(landHigh);
            _quickStopHash = Animator.StringToHash(quickStop);
            _hashesReady   = true;
        }

        public void CopyFrom(LocomotionStateNames src)
        {
            if (src == null) return;
            idle      = src.idle;
            jump      = src.jump;
            jumpMove  = src.jumpMove;
            roll      = src.roll;
            landHigh  = src.landHigh;
            quickStop = src.quickStop;
            falling   = src.falling;
            _hashesReady = false;
        }
    }
}
