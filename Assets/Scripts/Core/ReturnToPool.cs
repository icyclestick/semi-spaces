using UnityEngine;
using UnityEngine.Pool;

/// <summary>
/// A tiny helper script that returns a ParticleSystem to an ObjectPool
/// when it finishes playing. Attach this to any pooled particle prefab.
///
/// Setup:
///   1. Attach this script to your ParticleSystem prefab.
///   2. In the ParticleSystem's main module, set "Stop Action" to "Callback".
///      This tells Unity to call OnParticleSystemStopped() when it finishes.
///   3. The RaycastShooter will automatically assign the Pool reference
///      when it creates the instance — you don't need to set it manually.
///
/// How it works:
///   - The ParticleSystem plays its burst/effect.
///   - When all particles die, Unity calls OnParticleSystemStopped().
///   - This script calls Pool.Release(this), returning the object to the
///     pool instead of destroying it.
///   - Next time the weapon fires, the pool reuses this object instead
///     of instantiating a new one — zero garbage collection.
/// </summary>
[RequireComponent(typeof(ParticleSystem))]
public class ReturnToPool : MonoBehaviour
{
    /// <summary>
    /// Reference to the ObjectPool that owns this instance.
    /// Set by the RaycastShooter when the object is first created.
    /// </summary>
    public IObjectPool<ParticleSystem> Pool { get; set; }

    /// <summary>Cached ParticleSystem reference.</summary>
    private ParticleSystem ps;

    /// <summary>
    /// Caches the ParticleSystem component attached to this GameObject for later use.
    /// </summary>
    private void Awake()
    {
        ps = GetComponent<ParticleSystem>();
    }

    /// <summary>
    /// Called automatically by Unity when the ParticleSystem stops
    /// (requires "Stop Action" = "Callback" in the ParticleSystem's
    /// main module). Releases this object back to the pool.
    /// <summary>
    /// Handles the ParticleSystem stop callback by returning the particle instance to its owning pool or destroying the GameObject if no pool is assigned.
    /// </summary>
    /// <remarks>
    /// Invoked by Unity when the attached ParticleSystem stops (requires the ParticleSystem's Stop Action to be set to "Callback").
    /// </remarks>
    private void OnParticleSystemStopped()
    {
        if (Pool != null)
        {
            Pool.Release(ps);
        }
        else
        {
            // Fallback: if no pool reference exists (shouldn't happen),
            // destroy normally to prevent orphaned objects.
            Destroy(gameObject);
        }
    }
}
