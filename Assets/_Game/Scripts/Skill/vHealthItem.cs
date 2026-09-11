using UnityEngine;

/// <summary>
/// Base class for collectible health items.
/// Attach to a pickup GameObject; override OnCollect() for game-specific logic.
/// </summary>
public class vHealthItem : MonoBehaviour
{
    [Tooltip("Amount of health to restore on collection")]
    public float healthAmount = 30f;

    [Tooltip("Destroy this object after collection")]
    public bool destroyOnCollect = true;

    protected virtual void OnTriggerEnter(Collider other)
    {
        // Only respond to player layer or tag
        if (!other.CompareTag("Player"))
            return;

        OnCollect(other.gameObject);

        if (destroyOnCollect)
            Destroy(gameObject);
    }

    /// <summary>Override to apply health gain or play effects.</summary>
    protected virtual void OnCollect(GameObject collector) { }
}
