using UnityEngine;

/// <summary>
/// Contract interface for any object that can receive damage.
/// All damageable entities in Semi-Spaces must implement this
/// interface to ensure consistent damage resolution across
/// player weapons, Swarm AI attacks, and Duelist AI strikes.
///
/// Usage:
///   Implement this interface on any MonoBehaviour that should
///   respond to damage. The canonical implementation is Health.cs,
///   but shields, armor, or phase-shift objects may implement
///   their own variant.
///
/// Damage dealers should resolve hits like this:
///   if (hit.collider.TryGetComponent(out IDamageable target))
///       target.TakeDamage(damageAmount);
/// </summary>
public interface IDamageable
{
    /// <summary>
    /// Inflicts damage on this entity.
    /// </summary>
    /// <param name="damageAmount">Positive integer damage to apply.</param>
    void TakeDamage(int damageAmount);
}
