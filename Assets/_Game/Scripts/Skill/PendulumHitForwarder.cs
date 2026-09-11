using UnityEngine;

/// <summary>
/// 挂在摆锤锤头子物体上，将 Trigger 事件转发给父节点的 vPendulum。
/// </summary>
[RequireComponent(typeof(Collider))]
public class PendulumHitForwarder : MonoBehaviour
{
    private vPendulum _pendulum;

    private void Awake()
    {
        _pendulum = GetComponentInParent<vPendulum>();
        if (_pendulum == null)
            Debug.LogWarning("[PendulumHitForwarder] No vPendulum found in parent.", this);
        else
            _pendulum.RegisterHitHead(transform);

        var col = GetComponent<Collider>();
        if (col != null) col.isTrigger = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        _pendulum?.ReceiveHit(other);
    }

    // OnTriggerStay 不转发，避免持续在摆锤内触发重复击退
}
