using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Lightweight, reusable contact damage component. Attach to any
/// GameObject with a Collider (set to Trigger) to deal damage on
/// contact via the IDamageable interface.
///
/// Designed for:
///   - Swarm drones (melee contact damage)
///   - Environmental hazards (lava, electric fences)
///   - Traps, projectile AOE zones, etc.
///
/// Optimization:
///   - Per-target cooldown tracking prevents 60x/sec damage spam.
///   - Uses Dictionary with instanceID keys — zero boxing, zero
///     string allocations inside the physics loop.
///   - Stale entries are cleaned periodically to prevent memory
///     buildup from destroyed targets.
///
/// Setup:
///   1. Add a Collider to the enemy/hazard (any shape).
///   2. Check "Is Trigger" on the Collider.
///   3. Attach this script.
///   4. Set damageAmount and damageInterval in the Inspector.
///   5. Ensure the target (e.g., Player) has:
///      - A Rigidbody (kinematic is fine — required for trigger events).
///      - A Health component (or any IDamageable implementation).
///
/// Integration:
///   This resolves damage through IDamageable — fully compatible with
///   Health.cs, and any future Shield.cs or Armor.cs implementations.
///   No direct references to Health are made.
/// </summary>
public class ContactDamage : MonoBehaviour
{
    // ──────────────────────────────────────────────
    //  Configuration
    // ──────────────────────────────────────────────

    [Header("Damage")]
    [SerializeField, Tooltip("Damage dealt per hit (integer, resolves via IDamageable).")]
    private int damageAmount = 10;

    [SerializeField, Tooltip("Minimum time in seconds between damage ticks on the " +
        "SAME target. Prevents instant player erasure. 0.5 = 2 hits/sec max.")]
    private float damageInterval = 0.5f;

    // ──────────────────────────────────────────────
    //  Filtering
    // ──────────────────────────────────────────────

    [Header("Filtering")]
    [SerializeField, Tooltip("If set, only colliders on this layer will receive damage. " +
        "Leave at 'Everything' to damage anything with IDamageable.")]
    private LayerMask targetLayers = ~0; // Default: Everything.

    [SerializeField, Tooltip("If true, this component is disabled after dealing damage " +
        "once (useful for one-shot traps or projectile impacts).")]
    private bool disableAfterHit;

    // ──────────────────────────────────────────────
    //  Internal State
    // ──────────────────────────────────────────────

    /// <summary>
    /// Per-target cooldown tracker. Maps the target's instanceID to the
    /// last time damage was applied. Using instanceID (int) as the key
    /// avoids boxing or string allocations inside the physics loop.
    /// </summary>
    private readonly Dictionary<int, float> lastHitTimes = new Dictionary<int, float>();

    /// <summary>
    /// Timer for periodic cleanup of stale entries (destroyed targets).
    /// </summary>
    private float cleanupTimer;

    /// <summary>How often to sweep for stale dictionary entries (seconds).</summary>
    private const float CLEANUP_INTERVAL = 5f;

    // ──────────────────────────────────────────────
    //  Physics Callbacks
    // ──────────────────────────────────────────────

    /// <summary>
    /// Called every physics frame while another collider stays inside
    /// this trigger. Checks the cooldown timer, then resolves damage
    /// through IDamageable if enough time has passed.
    ///
    /// Why OnTriggerStay instead of OnTriggerEnter?
    ///   OnTriggerEnter fires once when the collider enters. If the
    ///   player stands inside a Swarm drone, they'd only take damage
    ///   once and then be immune. OnTriggerStay fires every physics
    ///   frame, and the cooldown timer controls the actual hit rate.
    /// </summary>
    private void OnTriggerStay(Collider other)
    {
        TryApplyDamage(other.gameObject);
    }

    /// <summary>
    /// Fallback for non-trigger colliders. Works identically to
    /// OnTriggerStay but fires from collision contacts instead.
    /// </summary>
    private void OnCollisionStay(Collision collision)
    {
        TryApplyDamage(collision.gameObject);
    }

    /// <summary>
    /// Cleans up the cooldown entry when a target leaves the trigger,
    /// so re-entering resets the cooldown (fresh contact = immediate hit).
    /// </summary>
    private void OnTriggerExit(Collider other)
    {
        lastHitTimes.Remove(other.gameObject.GetEntityId());
    }

    /// <summary>
    /// Cleans up the cooldown entry when a target stops colliding.
    /// </summary>
    private void OnCollisionExit(Collision collision)
    {
        lastHitTimes.Remove(collision.gameObject.GetEntityId());
    }

    // ──────────────────────────────────────────────
    //  Core Damage Logic
    // ──────────────────────────────────────────────

    /// <summary>
    /// Attempts to deal damage to the target. Checks:
    ///   1. Layer mask filtering.
    ///   2. Per-target cooldown timer.
    ///   3. IDamageable interface resolution.
    ///
    /// Designed for zero heap allocations — all comparisons use
    /// primitive types (int instanceID, float timestamps).
    /// </summary>
    /// <param name="target">The GameObject that entered/stayed in the trigger.</param>
    private void TryApplyDamage(GameObject target)
    {
        // --- Layer check ---
        // Bitwise comparison: if the target's layer isn't in our mask, skip.
        if ((targetLayers.value & (1 << target.layer)) == 0) return;

        // --- Cooldown check ---
        // Use the target's instanceID as a unique, allocation-free key.
        int targetId = target.GetEntityId();
        float currentTime = Time.time;

        if (lastHitTimes.TryGetValue(targetId, out float lastHit))
        {
            if (currentTime - lastHit < damageInterval) return;
        }

        // --- Zero-damage guard ---
        // Skip all work if damage is non-positive (e.g., SetDamage(0) was called).
        if (damageAmount <= 0) return;

        // --- Resolve damage through IDamageable ---
        // TryGetComponent avoids the null-check overhead of GetComponent
        // and returns false without allocating if the component doesn't exist.
        if (!target.TryGetComponent(out IDamageable damageable)) return;

        damageable.TakeDamage(damageAmount);
        lastHitTimes[targetId] = currentTime;

        if (disableAfterHit)
        {
            enabled = false;
        }
    }

    // ──────────────────────────────────────────────
    //  Maintenance
    // ──────────────────────────────────────────────

    private void Update()
    {
        // Periodic cleanup of stale entries from destroyed targets.
        // Without this, the dictionary would grow indefinitely if
        // many unique targets pass through (e.g., destructible props).
        cleanupTimer += Time.deltaTime;
        if (cleanupTimer < CLEANUP_INTERVAL) return;
        cleanupTimer = 0f;

        // Only clean if there are entries to check.
        if (lastHitTimes.Count == 0) return;

        // Build a list of stale keys to remove.
        // We can't modify the dictionary during enumeration.
        List<int> staleKeys = null;

        foreach (var kvp in lastHitTimes)
        {
            // Use the larger of CLEANUP_INTERVAL and damageInterval so entries
            // aren't pruned while their cooldown is still active.
            float staleThreshold = Mathf.Max(CLEANUP_INTERVAL, damageInterval);
            if (Time.time - kvp.Value > staleThreshold)
            {
                if (staleKeys == null) staleKeys = new List<int>(4);
                staleKeys.Add(kvp.Key);
            }
        }

        if (staleKeys != null)
        {
            for (int i = 0; i < staleKeys.Count; i++)
            {
                lastHitTimes.Remove(staleKeys[i]);
            }
        }
    }

    // ──────────────────────────────────────────────
    //  Public API
    // ──────────────────────────────────────────────

    /// <summary>
    /// Updates the damage amount at runtime. Useful for power-ups
    /// or difficulty scaling.
    /// </summary>
    public void SetDamage(int newDamage)
    {
        damageAmount = Mathf.Max(0, newDamage);
    }

    /// <summary>
    /// Updates the damage interval at runtime.
    /// </summary>
    public void SetInterval(float newInterval)
    {
        damageInterval = Mathf.Max(0f, newInterval);
    }
}
