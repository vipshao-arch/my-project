// =====================================================================
//  SkillPreviewEvents —— 预览事件 record struct(V3 计划 §2.3)
//  V3.1.6: SkillVFXEvent 新增 VFXInstance 字段,回调可加入离屏场景
//
//  4 个值类型事件,SkillPreviewRuntime 触发,主 wizard 订阅。
//  全 readonly struct 避免每帧 GC,事件总线用 System.Action<T>。
// =====================================================================

using UnityEngine;

namespace Game.SkillSystem.EditorTools
{
    /// <summary>VFX 触发事件(粒子 prefab 在指定时刻被实例化)。</summary>
    public readonly struct SkillVFXEvent
    {
        public readonly double T;
        public readonly GameObject Prefab;
        public readonly GameObject VFXInstance;
        public readonly Vector3 Pos;
        public readonly Quaternion Rot;
        public readonly float Scale;
        public readonly string Bone;
        public readonly float AutoDestroySec;
        /// <summary>VFXEntry.worldSpace: true=世界空间不跟随骨骼, false=挂骨骼跟随。</summary>
        public readonly bool WorldSpace;
        /// <summary>spawnBone Transform(Runtime 已查找好,OnPreviewVFX 无需重复 Find)。</summary>
        public readonly Transform SpawnBoneTransform;
        public SkillVFXEvent(double t, GameObject prefab, GameObject instance, Vector3 pos, Quaternion rot, float scale, string bone, float autoDestroySec, bool worldSpace = true, Transform spawnBoneTransform = null)
        {
            T = t; Prefab = prefab; VFXInstance = instance; Pos = pos; Rot = rot; Scale = scale; Bone = bone; AutoDestroySec = autoDestroySec; WorldSpace = worldSpace; SpawnBoneTransform = spawnBoneTransform;
        }
    }

    /// <summary>SFX 触发事件(临时 AudioSource + 静音标记)。</summary>
    public readonly struct SkillSFXEvent
    {
        public readonly double T;
        public readonly AudioClip Clip;
        public readonly float Volume;
        public readonly float PitchRandomPercent;
        public SkillSFXEvent(double t, AudioClip clip, float volume, float pitchRandomPercent)
        {
            T = t; Clip = clip; Volume = volume; PitchRandomPercent = pitchRandomPercent;
        }
    }

    /// <summary>命中框事件(近战/AOE 命中范围可视化)。</summary>
    public readonly struct SkillHitEvent
    {
        public readonly double T;
        public readonly SkillNodeData Source;
        public readonly Vector3 Center;
        public readonly float Radius;
        public readonly float Angle;            // 度,<360 视为扇形
        public readonly bool DealsDamage;
        public readonly float DamageMultiplier;
        public readonly float DurationHint;     // 攻击体存在时长(给 Gizmo 淡出兜底)
        public SkillHitEvent(double t, SkillNodeData source, Vector3 center, float radius, float angle,
            bool dealsDamage, float damageMultiplier, float durationHint)
        {
            T = t; Source = source; Center = center; Radius = radius; Angle = angle;
            DealsDamage = dealsDamage; DamageMultiplier = damageMultiplier; DurationHint = durationHint;
        }
    }

    /// <summary>数值变化事件(预留:Buff/Status 改值时通知 UI)。</summary>
    public readonly struct SkillValueChange
    {
        public readonly double T;
        public readonly SkillNodeData Source;
        public readonly string FieldName;
        public readonly object OldValue;
        public readonly object NewValue;
        public SkillValueChange(double t, SkillNodeData source, string fieldName, object oldValue, object newValue)
        {
            T = t; Source = source; FieldName = fieldName; OldValue = oldValue; NewValue = newValue;
        }
    }
}
