using UnityEngine;

/// <summary>
/// Lightweight kinematic projectile for enemy ranged attacks.
/// Moves via transform.Translate (no Rigidbody) and detects hits
/// using a swept raycast between frames to prevent tunnelling
/// through thin geometry at high speeds.
///
/// Damage is resolved through IDamageable — fully compatible with
/// Health.cs and any future Shield/Armor implementations. No direct
/// Health references are made.
///
/// Lifecycle:
///   - Spawned by an enemy brain (e.g., DuelistBrain) via Instantiate.
///   - Travels forward at configurable speed.
///   - On hit: deals damage via IDamageable, then self-destructs.
///   - Failsafe: auto-destroys after 'lifetime' seconds to prevent leaks.
///
/// Setup:
///   1. Create a Projectile prefab (quad, capsule, VFX, etc.).
///   2. Attach this script.
///   3. Set speed, damage, lifetime in the Inspector.
///   4. Set hitMask to include Player + Environment layers
///      (exclude the Enemy layer so projectiles don't hit the shooter).
///   5. Spawn from your brain script:
///      Instantiate(projectilePrefab, firePoint.position, firePoint.rotation);
///
/// Integration with ContactDamage:
///   EnemyProjectile handles its own damage on impact — do NOT also
///   attach ContactDamage.cs to projectile prefabs.
/// </summary>
public class EnemyProjectile : MonoBehaviour
{
    // ──────────────────────────────────────────────
    //  Configuration
    // ──────────────────────────────────────────────

    [Header("Movement")]
    [SerializeField, Tooltip("Travel speed in units per second.")]
    private float speed = 25f;

    [Header("Damage")]
    [SerializeField, Tooltip("Damage dealt on hit (resolves via IDamageable).")]
    private int damage = 15;

    [Header("Lifetime")]
    [SerializeField, Tooltip("Seconds before the projectile self-destructs. " +
        "Prevents memory leaks from missed shots flying forever.")]
    private float lifetime = 4f;

    [Header("Collision")]
    [SerializeField, Tooltip("Which layers the projectile can hit. " +
        "Set to Player + Environment. Exclude the Enemy layer so " +
        "the projectile doesn't hit the shooter on spawn.")]
    private LayerMask hitMask = ~0;

    // ──────────────────────────────────────────────
    //  Internal State
    // ──────────────────────────────────────────────

    /// <summary>
    /// Position at the start of the current frame. The swept raycast
    /// checks from here to the post-move position to catch collisions
    /// that would otherwise tunnel through thin geometry.
    /// </summary>
    private Vector3 previousPosition;

    /// <summary>Countdown timer for the lifetime failsafe.</summary>
    private float lifetimeTimer;

    // ──────────────────────────────────────────────
    //  Lifecycle
    // ──────────────────────────────────────────────

    private void OnEnable()
    {
        // Cache the starting position for the first frame's swept ray.
        previousPosition = transform.position;
        lifetimeTimer = lifetime;
    }

    private void Update()
    {
        // --- Lifetime failsafe ---
        // Prevent leaked projectiles from flying forever if they miss everything.
        lifetimeTimer -= Time.deltaTime;
        if (lifetimeTimer <= 0f)
        {
            Destroy(gameObject);
            return;
        }

        // --- Move forward ---
        float frameDistance = speed * Time.deltaTime;
        transform.Translate(Vector3.forward * frameDistance);

        // --- Swept raycast (anti-tunnelling) ---
        // Cast from last frame's position toward this frame's position.
        // This catches hits even when the projectile moves further than
        // a collider's thickness in a single frame.
        Vector3 currentPosition = transform.position;
        Vector3 travelVector = currentPosition - previousPosition;
        float travelDistance = travelVector.magnitude;

        if (travelDistance > 0f)
        {
            Vector3 direction = travelVector / travelDistance; // Normalized.

            if (Physics.Raycast(
                    previousPosition,
                    direction,
                    out RaycastHit hit,
                    travelDistance,
                    hitMask,
                    QueryTriggerInteraction.Ignore))
            {
                OnHit(hit);
                return;
            }
        }

        // Cache this frame's position for the next frame's swept ray.
        previousPosition = currentPosition;
    }

    // ──────────────────────────────────────────────
    //  Hit Resolution
    // ──────────────────────────────────────────────

    /// <summary>
    /// Handles a confirmed hit. Resolves damage through IDamageable
    /// if the target implements it, then self-destructs.
    ///
    /// The projectile is destroyed on ANY hit (walls, floors, props)
    /// to prevent it from phasing through geometry. Damage is only
    /// dealt if the hit object has an IDamageable component.
    /// </summary>
    /// <param name="hit">The RaycastHit data from the swept raycast.</param>
    private void OnHit(RaycastHit hit)
    {
        // --- Resolve damage through IDamageable ---
        // TryGetComponent returns false without allocating if the
        // component doesn't exist (e.g., hitting a wall).
        if (hit.collider.TryGetComponent(out IDamageable target))
        {
            target.TakeDamage(damage);

            Debug.Log($"[EnemyProjectile] Hit '{hit.collider.gameObject.name}' " +
                      $"for {damage} damage.", this);
        }
        else
        {
            // Debug.Log($"<color=cyan>[EnemyProjectile] Hit surface '{hit.collider.gameObject.name}' " +
            //           $"(layer: {LayerMask.LayerToName(hit.collider.gameObject.layer)}, " +
            //           $"point: {hit.point})</color>", this);
        }

        // Destroy on any hit — walls, player, props, etc.
        Destroy(gameObject);
    }
}
