using UnityEngine;
using Game.Character;

/// <summary>
/// Swings this GameObject back and forth like a pendulum around its local Z-axis.
/// Detects characters via trigger colliders on child objects and applies knockback force.
/// Hit detection happens in Update; force is applied in FixedUpdate via IHittable interface.
/// </summary>
public class vPendulum : MonoBehaviour
{
    [Tooltip("Maximum swing angle in degrees (half-arc from centre)")]
    public float angle = 90f;

    [Tooltip("Number of full oscillations per second")]
    public float speed = 1.5f;

    [Tooltip("Seconds to wait before starting the swing")]
    public float startDelay = 0f;

    [Header("Knockback")]
    [Tooltip("Tag of the character to knock back")]
    public string characterTag = "Player";

    [Tooltip("Horizontal knockback force")]
    public float knockbackForce = 15f;

    [Tooltip("Upward force component on hit")]
    public float knockbackUpForce = 3f;

    [Tooltip("Knockback state protection duration (seconds)")]
    public float knockbackDuration = 0.6f;

    [Tooltip("Minimum swing angular speed (deg/s) required to trigger knockback")]
    public float minSwingSpeed = 30f;

    [Tooltip("Cooldown seconds between hits on the same collider")]
    public float hitCooldown = 0.8f;

    private float _timer;
    private Quaternion _startRotation;
    private float _prevSwing;
    private float _swingVelocity;   // 带符号角速度（deg/s）
    private float _angularSpeed;
    private Transform _hitHead;

    public void RegisterHitHead(Transform head) => _hitHead = head;

    private System.Collections.Generic.Dictionary<Collider, float> _hitTimes
        = new System.Collections.Generic.Dictionary<Collider, float>();

    // 待执行的击退，在 FixedUpdate 里统一施加，避免被 CharacterMotor 的 ControlSpeed 覆盖
    private struct PendingHit
    {
        public Rigidbody rb;
        public Vector3 force;
        public IHittable hittable;
    }
    private PendingHit? _pendingHit;

    private void Start()
    {
        _startRotation = transform.localRotation;
        _timer = -startDelay;
    }

    private void Update()
    {
        _timer += Time.deltaTime;
        if (_timer < 0f) return;

        float swing = Mathf.Sin(_timer * speed * Mathf.PI * 2f) * angle;
        transform.localRotation = _startRotation * Quaternion.Euler(0f, 0f, swing);

        if (Time.deltaTime > 0f)
        {
            _swingVelocity = (swing - _prevSwing) / Time.deltaTime;
            _angularSpeed  = Mathf.Abs(_swingVelocity);
        }
        _prevSwing = swing;
    }

    private void FixedUpdate()
    {
        if (_pendingHit == null) return;
        var hit = _pendingHit.Value;
        _pendingHit = null;

        if (hit.hittable != null)
        {
            hit.hittable.ReceiveKnockback(hit.force, knockbackDuration);
        }
        else
        {
            // 兜底：没有 IHittable 的刚体直接施加冲量
            hit.rb.velocity = Vector3.zero;
            hit.rb.AddForce(hit.force, ForceMode.Impulse);
        }
    }

    // 由子物体 PendulumHitForwarder 调用
    public void ReceiveHit(Collider other)
    {
        if (!other.CompareTag(characterTag)) return;

        float now = Time.time;
        if (_hitTimes.TryGetValue(other, out float lastHit) && now - lastHit < hitCooldown) return;
        _hitTimes[other] = now;

        if (_angularSpeed < minSwingSpeed) return;

        var rb = other.GetComponent<Rigidbody>();
        if (rb == null) rb = other.GetComponentInParent<Rigidbody>();
        if (rb == null) return;

        // 击退方向：摆锤摆动切线方向（世界空间）
        Transform headTransform = _hitHead != null ? _hitHead : transform;
        Vector3 toHead   = headTransform.position - transform.position;
        Vector3 worldZ   = transform.TransformDirection(Vector3.forward);
        Vector3 swingDir = Vector3.Cross(worldZ, toHead.normalized);
        if (_swingVelocity < 0f) swingDir = -swingDir;
        swingDir.y = 0f;
        if (swingDir.sqrMagnitude < 0.001f) swingDir = transform.right;
        swingDir.Normalize();

        Vector3 force = swingDir * knockbackForce + Vector3.up * knockbackUpForce;

        // 优先通过 IHittable 接口处理，其次兜底用 Rigidbody
        var hittable = rb.GetComponent<IHittable>() ?? rb.GetComponentInChildren<IHittable>();

        _pendingHit = new PendingHit { rb = rb, force = force, hittable = hittable };
    }
}
