using UnityEngine;
using System.Collections.Generic;
using Game.Character;

namespace Game.SkillSystem
{
    /// <summary>
    /// Makes a VFX prefab travel forward at a fixed speed.
    /// Attach to the root of any projectile effect prefab alongside VFXAutoDestroy.
    ///
    /// Usage:
    ///   - Spawn the prefab facing the attack direction (SpawnVFXEntry sets rotation = spawnPoint.rotation)
    ///   - This component moves it forward each frame
    ///   - On collision with an IDamageable target, deals damage via HitDetector
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class VFXProjectile : MonoBehaviour
    {
        [Tooltip("Travel speed in world units per second")]
        public float speed = 20f;

        [Tooltip("Damage dealt on hit. If 0, no damage is applied.")]
        public float damage = 5f;

        [Tooltip("Layers that this projectile can hit.")]
        public LayerMask hitMask = ~0;

        [Tooltip("Lifetime in seconds before auto-destroy. 0 = infinite.")]
        public float lifetime = 3f;

        [Tooltip("Destroy on first hit?")]
        public bool destroyOnHit = true;

        private float _lifeTimer = 0f;
        private HitDetector _ownerHitDetector;
        private GameObject _source;
        private HashSet<GameObject> _hitTargets = new HashSet<GameObject>();

        void Start()
        {
            // Ensure the collider is a trigger so physics movement isn't blocked
            var col = GetComponent<Collider>();
            if (col != null) col.isTrigger = true;
        }

        /// <summary>
        /// Initialize the projectile with damage and source info.
        /// Called by SkillAnimPlayer.SpawnVFXEntry or HitDetector.
        /// </summary>
        // 远端表现标记(S2-5b):visualOnly 弹体照常飞行/播 VFX,但不落地伤害
        private bool _visualOnly;
        public void SetVisualOnly(bool v) => _visualOnly = v;

        public void Initialize(float damage, GameObject source, LayerMask mask)
        {
            this.damage = damage;
            this._source = source;
            this.hitMask = mask;
            _ownerHitDetector = source != null ? source.GetComponent<HitDetector>() : null;
        }

        void Update()
        {
            transform.position += transform.forward * speed * Time.deltaTime;

            if (lifetime > 0f)
            {
                _lifeTimer += Time.deltaTime;
                if (_lifeTimer >= lifetime)
                    Destroy(gameObject);
            }
        }

        void OnTriggerEnter(Collider other)
        {
            // Don't hit the source
            if (_source != null && other.transform.root == _source.transform.root) return;
            if (_hitTargets.Contains(other.gameObject)) return;

            // Layer mask check
            if ((hitMask.value & (1 << other.gameObject.layer)) == 0) return;

            var damageable = other.GetComponentInParent<IDamageable>();
            if (damageable == null || damageable.isDead) return;

            _hitTargets.Add(other.gameObject);

            // Deal damage directly, or route through HitDetector
            Vector3 hitDir = transform.forward;
            if (!_visualOnly)
                damageable.TakeDamage(damage, _source, hitDir);   // visualOnly:伤害不落地

            if (destroyOnHit)
                Destroy(gameObject);
        }
    }
}
