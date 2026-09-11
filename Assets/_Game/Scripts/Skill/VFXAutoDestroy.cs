using UnityEngine;

namespace Game.SkillSystem
{
    /// <summary>
    /// Destroys the GameObject after a fixed lifetime.
    /// Attach to any VFX prefab root to ensure automatic cleanup.
    /// </summary>
    public class VFXAutoDestroy : MonoBehaviour
    {
        [Tooltip("Seconds before this GameObject is destroyed")]
        public float lifetime = 3f;

        void Start()
        {
            Destroy(gameObject, lifetime);
        }
    }
}
