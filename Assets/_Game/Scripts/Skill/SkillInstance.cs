using UnityEngine;
using System;

namespace Game.SkillSystem
{
    public enum SkillState
    {
        Ready,
        Casting,
        Cooldown
    }

    public class SkillInstance
    {
        public SkillData Data { get; private set; }
        public SkillState State { get; private set; }
        public float RemainingCooldown { get; private set; }
        public float RemainingCastTime { get; private set; }
        public float RemainingChannel { get; private set; }

        /// <summary>
        /// Fired when cast time completes and effect should be applied.
        /// </summary>
        public event Action OnCastComplete;

        /// <summary>
        /// Fired when cooldown finishes and skill becomes ready again.
        /// </summary>
        public event Action OnCooldownReady;

        public SkillInstance(SkillData data)
        {
            Data = data;
            State = SkillState.Ready;
        }

        public void Tick(float deltaTime)
        {
            switch (State)
            {
                case SkillState.Casting:
                    RemainingCastTime -= deltaTime;
                    if (RemainingCastTime <= 0f)
                        RemainingCastTime = 0f;

                    // Tick channel
                    if (Data.channelDuration > 0f && RemainingChannel > 0f)
                    {
                        RemainingChannel -= deltaTime;
                        if (RemainingChannel <= 0f)
                            RemainingChannel = 0f;
                    }
                    break;

                case SkillState.Cooldown:
                    RemainingCooldown -= deltaTime;
                    if (RemainingCooldown <= 0f)
                    {
                        RemainingCooldown = 0f;
                        State = SkillState.Ready;
                        OnCooldownReady?.Invoke();
                    }
                    break;
            }
        }

        public bool TryStartCast()
        {
            if (State != SkillState.Ready) return false;
            State = SkillState.Casting;
            RemainingCastTime = Data.castTime;
            RemainingChannel = 0f;
            return true;
        }

        /// <summary>
        /// Cancel current cast (e.g. stunned, interrupted). Enters cooldown.
        /// </summary>
        public void CancelCast()
        {
            if (State != SkillState.Casting) return;
            RemainingCastTime = 0f;
            RemainingChannel = 0f;
            State = SkillState.Cooldown;
            RemainingCooldown = Data.cooldown * 0.5f; // half cooldown on cancel
        }

        public void ForceReady()
        {
            State = SkillState.Ready;
            RemainingCooldown = 0f;
            RemainingCastTime = 0f;
            RemainingChannel = 0f;
        }

        public float CooldownNormalized()
        {
            return Data.cooldown > 0f ? RemainingCooldown / Data.cooldown : 0f;
        }

        public float CastProgressNormalized()
        {
            return Data.castTime > 0f ? 1f - (RemainingCastTime / Data.castTime) : 1f;
        }

        /// <summary>
        /// Force complete the cast and enter cooldown. Called by SkillController
        /// when animation has finished playing.
        /// </summary>
        public void ForceCompleteCast()
        {
            if (State != SkillState.Casting) return;
            CompleteCast();
        }

        private void CompleteCast()
        {
            OnCastComplete?.Invoke();
            State = SkillState.Cooldown;
            RemainingCooldown = Data.cooldown;
        }
    }
}
