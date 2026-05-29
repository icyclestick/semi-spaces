using UnityEngine;

/// <summary>
/// Handles Swarm drone melee attacks against the player. Resolves
/// damage exclusively through the IDamageable interface as required
/// by the Semi-Spaces architecture.
///
/// This is a sibling component on the Swarm drone prefab — it does
/// NOT extend EnemyBase. Only SwarmAgent is the "brain"; this is
/// a helper that the brain calls when in attack range.
///
/// Architecture:
///   SwarmAgent.OnThink() detects the player is in range →
///   calls SwarmAttack.TryAttack() →
///   SwarmAttack resolves through IDamageable.TakeDamage().
///
/// Setup:
///   1. Attach this component to the same prefab as SwarmAgent.
///   2. Configure attackDamage and attackCooldown in the Inspector.
///   3. SwarmAgent will find and call this automatically via OnInit().
/// </summary>
public class SwarmAttack : MonoBehaviour
{
    // ──────────────────────────────────────────────
    //  Configuration
    // ──────────────────────────────────────────────

    [Header("Attack")]
    [SerializeField, Tooltip("Damage dealt per attack hit.")]
    private int attackDamage = 5;

    [SerializeField, Tooltip("Minimum seconds between attacks. Prevents per-frame damage spam.")]
    private float attackCooldown = 1.0f;

    // ──────────────────────────────────────────────
    //  Runtime State
    // ──────────────────────────────────────────────

    /// <summary>Countdown timer for the attack cooldown.</summary>
    private float cooldownTimer;

    // ──────────────────────────────────────────────
    //  Public API
    // ──────────────────────────────────────────────

    /// <summary>
    /// Attempts to deal damage to the given target. Respects the cooldown
    /// timer — if the cooldown hasn't elapsed, the attack is silently
    /// skipped.
    ///
    /// Damage is resolved through the IDamageable interface as required
    /// by the Semi-Spaces architecture. If the target does not implement
    /// IDamageable, the attack is ignored.
    /// </summary>
    /// <param name="target">The GameObject to attack (typically the player).</param>
    /// <returns>True if damage was dealt, false if on cooldown or target is not damageable.</returns>
    public bool TryAttack(GameObject target)
    {
        if (target == null) return false;

        // Check cooldown.
        if (cooldownTimer > 0f) return false;

        // Resolve through IDamageable — NEVER reference Health directly.
        if (target.TryGetComponent(out IDamageable damageable))
        {
            damageable.TakeDamage(attackDamage);
            cooldownTimer = attackCooldown;

            if (Debug.isDebugBuild)
            {
                Debug.Log($"[SwarmAttack] '{gameObject.name}' attacked '{target.name}' for {attackDamage} damage.");
            }
            return true;
        }

        return false;
    }

    // ──────────────────────────────────────────────
    //  Lifecycle
    // ──────────────────────────────────────────────

    private void Update()
    {
        // Tick down the cooldown timer.
        cooldownTimer = Mathf.Max(0f, cooldownTimer - Time.deltaTime);
    }
}
