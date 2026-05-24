using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// A generic, reusable health component that can be attached to any
/// GameObject — player, enemy, destructible prop, etc. Implements the
/// IDamageable interface so damage dealers can resolve hits through a
/// shared contract without knowing the concrete type.
///
/// Setup:
///   1. Attach this script to any GameObject that should have health.
///   2. Set 'maxHealth' in the Inspector.
///   3. Call TakeDamage(amount) via the IDamageable interface.
///   4. (Optional) Subscribe to OnDeath / OnHealthChanged for game logic.
///   5. (Optional) Wire the onDeathEvent UnityEvent in the Inspector for
///      designer-friendly, no-code death triggers.
/// </summary>
public class Health : MonoBehaviour, IDamageable
{
    // ──────────────────────────────────────────────
    //  Configuration
    // ──────────────────────────────────────────────

    [Header("Health")]
    [SerializeField, Tooltip("Maximum (and starting) health for this entity.")]
    private int maxHealth = 100;

    // ──────────────────────────────────────────────
    //  Runtime State
    // ──────────────────────────────────────────────

    /// <summary>The entity's current health. Clamped between 0 and maxHealth.</summary>
    private int currentHealth;

    /// <summary>Tracks whether this entity has already died to prevent double-kills.</summary>
    private bool isDead;

    // ──────────────────────────────────────────────
    //  Public Accessors
    // ──────────────────────────────────────────────

    /// <summary>Current health value (read-only).</summary>
    public int CurrentHealth => currentHealth;

    /// <summary>Maximum health value (read-only).</summary>
    public int MaxHealth => maxHealth;

    /// <summary>True if this entity is dead.</summary>
    public bool IsDead => isDead;

    // ──────────────────────────────────────────────
    //  Events (C# — for code subscribers)
    // ──────────────────────────────────────────────

    /// <summary>
    /// Fired whenever health changes. Passes (currentHealth, maxHealth)
    /// so UI scripts can update health bars without polling.
    /// </summary>
    public event System.Action<int, int> OnHealthChanged;

    /// <summary>
    /// Fired once when health reaches zero. Subscribers can run custom
    /// death logic (ragdoll, loot drop, score update, etc.).
    /// </summary>
    public event System.Action OnDeath;

    // ──────────────────────────────────────────────
    //  Events (UnityEvent — for Inspector wiring)
    // ──────────────────────────────────────────────

    [Header("Events")]
    [SerializeField, Tooltip("Fired in the Inspector when this entity dies. " +
        "Use this to trigger level progression, spawn effects, update score, etc.")]
    private UnityEvent onDeathEvent;

    [SerializeField, Tooltip("Fired in the Inspector when health changes. " +
        "Useful for wiring up UI health bars without code.")]
    private UnityEvent<int, int> onHealthChangedEvent;

    // ──────────────────────────────────────────────
    //  Lifecycle
    // ──────────────────────────────────────────────

    private void Awake()
    {
        // Initialize current health to the configured maximum.
        currentHealth = maxHealth;
    }

    // ──────────────────────────────────────────────
    //  IDamageable Implementation
    // ──────────────────────────────────────────────

    /// <summary>
    /// Reduces health by the given amount. If health drops to zero or
    /// below, triggers the death sequence exactly once.
    /// This is the IDamageable contract — all damage in Semi-Spaces
    /// flows through this method.
    /// </summary>
    /// <param name="damageAmount">Positive damage value to subtract.</param>
    public void TakeDamage(int damageAmount)
    {
        // Ignore damage on an already-dead entity.
        if (isDead) return;

        // Guard against negative damage values being passed accidentally.
        if (damageAmount <= 0)
        {
            Debug.LogWarning($"[Health] TakeDamage called with non-positive value ({damageAmount}) " +
                             $"on '{gameObject.name}'. Ignored.", this);
            return;
        }

        // Apply damage, clamped to zero.
        currentHealth = Mathf.Max(currentHealth - damageAmount, 0);

        // Notify both C# and UnityEvent subscribers.
        OnHealthChanged?.Invoke(currentHealth, maxHealth);
        onHealthChangedEvent?.Invoke(currentHealth, maxHealth);

        Debug.Log($"[Health] '{gameObject.name}' took {damageAmount} damage. " +
                  $"Health: {currentHealth}/{maxHealth}", this);

        // Check for death.
        if (currentHealth <= 0)
        {
            Die();
        }
    }

    // ──────────────────────────────────────────────
    //  Public API
    // ──────────────────────────────────────────────

    /// <summary>
    /// Restores health by the given amount, clamped to maxHealth.
    /// </summary>
    /// <param name="healAmount">Positive heal value to add.</param>
    public void Heal(int healAmount)
    {
        if (isDead) return;

        if (healAmount <= 0)
        {
            Debug.LogWarning($"[Health] Heal called with non-positive value ({healAmount}) " +
                             $"on '{gameObject.name}'. Ignored.", this);
            return;
        }

        currentHealth = Mathf.Min(currentHealth + healAmount, maxHealth);

        // Notify both C# and UnityEvent subscribers.
        OnHealthChanged?.Invoke(currentHealth, maxHealth);
        onHealthChangedEvent?.Invoke(currentHealth, maxHealth);

        Debug.Log($"[Health] '{gameObject.name}' healed for {healAmount}. " +
                  $"Health: {currentHealth}/{maxHealth}", this);
    }

    // ──────────────────────────────────────────────
    //  Death
    // ──────────────────────────────────────────────

    /// <summary>
    /// Handles the death of this entity. Fires both C# and UnityEvent
    /// death events, logs the death, and destroys the GameObject.
    /// </summary>
    private void Die()
    {
        isDead = true;

        Debug.Log($"[Health] '{gameObject.name}' has died.", this);

        // Notify C# subscribers (AI scripts, score manager, etc.).
        OnDeath?.Invoke();

        // Notify Inspector-wired subscribers (level triggers, VFX, etc.).
        onDeathEvent?.Invoke();

        // Destroy the owning GameObject.
        Destroy(gameObject);
    }
}
