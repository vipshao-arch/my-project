#if UNITY_EDITOR
using UnityEngine;

namespace Game.Character.EditorTools.CharacterKit
{
    /// <summary>CharacterKit 组装阶段的组件获取/创建工具，避免重复 AddComponent。</summary>
    public static class CharacterKitComponentUtility
    {
        public static T GetOrAdd<T>(GameObject target) where T : Component
        {
            if (target == null) return null;

            var existing = target.GetComponent<T>();
            return existing != null ? existing : target.AddComponent<T>();
        }
    }
}
#endif
