using UnityEngine;
using Game.Character;

namespace Game.SkillSystem
{
    /// <summary>
    /// 抛物线飞行弹体（CurvedProjectileNode 的运行时实例）。
    /// 独立组件，不挂 BoxCollider 触发器（落地时自毁）。
    /// 每帧按 gravity 累积垂直速度，y 反向且 |y|<groundSnapY 视为落地。
    /// </summary>
    [AddComponentMenu("Game/Skill System/Parabolic Projectile")]
    public class ParabolicProjectile : MonoBehaviour
    {
        // 远端表现标记(S2-5b):visualOnly 弹体照常飞行/播 VFX,但不落地伤害
        private bool _visualOnly;
        public void SetVisualOnly(bool v) => _visualOnly = v;

        private float _speed;
        private float _gravity;
        private float _groundSnapY;
        private float _vy;
        private float _maxRange;
        private float _traveled;
        private Vector3 _shotDir;   // 含俯仰的发射方向(由 Initialize 注入,不再拍平)
        private GameObject _source;
        private LayerMask _hitMask;
        private float _damage;
        private ShotHitMode _hitMode;
        private int _pierceCount;
        private int _piercedCount;
        private System.Collections.Generic.HashSet<GameObject> _hitSet = new System.Collections.Generic.HashSet<GameObject>();
        private bool _destroyed;
        private SkillData _skill;
        private float _spawnY;       // 出生时 Y,落地判定用

        public void Initialize(CurvedProjectileData node, NodeContext ctx)
        {
            _speed       = node.speed;
            _gravity     = node.gravity;
            _groundSnapY = node.groundSnapY;
            _maxRange    = node.maxRange;

            // ★ 关键修复:用 GameObject 自己的 forward,而不是把 ctx.caster.forward 拍平。
            //   OnHit() 已经把 transform.forward 设成 (pitchRot * forward),这里直接用,
            //   弹体就能正常往上方/下方飞。
            _shotDir = transform.forward.normalized;
            // 初始垂直速度 = 发射方向在 Y 上的分量 × speed(让初速有自然弧度)
            _vy = _shotDir.y * _speed;
            _spawnY = transform.position.y;

            _source       = ctx.source;
            _hitMask      = ctx.hitMask;
            _damage       = node.damage > 0f ? node.damage : ctx.damage;
            _hitMode      = node.hitMode;
            _pierceCount  = node.pierceCount;
            _skill        = ctx.skill;

            // 飞行弹体 prefab 挂为子物体
            if (node.projectilePrefab != null)
            {
                var p = Instantiate(node.projectilePrefab, transform);
                p.transform.localPosition = node.projectileLocalOffset;
                p.transform.localEulerAngles = node.projectileLocalEulerOffset;
                if (node.projectileLocalScale != 1f)
                    p.transform.localScale = p.transform.localScale * node.projectileLocalScale;
            }

            if (node.projectileLifetime > 0f)
                Invoke(nameof(SelfDestroy), node.projectileLifetime);
        }

        void Update()
        {
            if (_destroyed) return;

            float dt = Time.deltaTime;

            // ★ 关键修复:沿 _shotDir 匀速推进(带俯仰),不再把方向拍平。
            //   同时重力在垂直方向额外累加 → 形成抛物线轨迹,可上飞可下飞。
            //   把速度向量拆成"沿 shotDir 的匀速分量"+"纯垂直的重力分量":
            transform.position += _shotDir * (_speed * dt);
            _vy -= _gravity * dt;
            transform.position += Vector3.up * (_vy * dt);

            // 累计飞行距离(用水平分量,避免仰射时被 vy 拉长)
            float horizSpeed = new Vector3(_shotDir.x, 0f, _shotDir.z).magnitude * _speed;
            _traveled += horizSpeed * dt;

            // 落地判定:vy 反向(开始下落) + y 已低于出生 Y - 阈值
            if (_vy < 0f && transform.position.y < _spawnY - _groundSnapY)
            {
                SelfDestroy();
                return;
            }

            if (_traveled >= _maxRange)
            {
                SelfDestroy();
                return;
            }

            // 简易命中检测:每帧 OverlapSphere(小范围)
            Collider[] hits = Physics.OverlapSphere(transform.position, 0.5f, _hitMask);
            for (int i = 0; i < hits.Length; i++)
            {
                var c = hits[i];
                if (c.gameObject == _source) continue;
                if (_hitSet.Contains(c.gameObject)) continue;
                var d = c.GetComponentInParent<IDamageable>();
                if (d == null || d.isDead) continue;
                if (!_visualOnly)
                    d.TakeDamage(_damage, _source, _shotDir);   // visualOnly:命中 VFX 照播,伤害不落地
                _hitSet.Add(c.gameObject);
                _piercedCount++;

                // 命中 VFX — 从 graphData HitVFXData 读取
                if (_skill != null)
                {
                    var (hfxPrefab, _, _, _, _, _, hfxScale, _, _) =
                        SkillData.GetHitVFXFromGraph(_skill);
                    if (hfxPrefab != null)
                    {
                        var go = Instantiate(hfxPrefab,
                            c.ClosestPoint(transform.position),
                            Quaternion.LookRotation(_shotDir));
                        if (hfxScale != 1f) go.transform.localScale *= hfxScale;
                    }
                }

                if (_hitMode == ShotHitMode.Stop) { SelfDestroy(); return; }
                if (_hitMode == ShotHitMode.Pierce && _piercedCount >= _pierceCount) { SelfDestroy(); return; }
            }
        }

        private void SelfDestroy()
        {
            if (_destroyed) return;
            _destroyed = true;
            Destroy(gameObject, 0.1f);
        }
    }
}
