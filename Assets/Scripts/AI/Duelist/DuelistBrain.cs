using UnityEngine;

/// <summary>
/// The Duelist enemy brain — implements Utility-Based Agent architecture
/// with priority-based subsumption overrides. Inherits from EnemyBase,
/// which provides perception, navigation wrappers, and health lifecycle.
///
/// Architecture:
///   EnemyBase handles: component caching, perception, NavMesh, death.
///   DuelistBrain handles: utility function scoring + priority overrides.
///
/// IMPORTANT — Architectural Constraints:
///   - Do NOT override Awake(), Update(), or OnDestroy(). They are private
///     in EnemyBase. Use the hooks below instead.
///   - Do NOT access the NavMeshAgent directly. It is private in EnemyBase.
///     Use MoveToTarget(), StopNavigation(), SetSpeed(), etc.
///   - Do NOT create your own Health component reference. EnemyBase auto-requires it.
///     Use CurrentHealth / MaxHealth if you need to read health values.
///
/// Available Hooks (override these):
///   OnInit()        — One-time setup after base initialisation (Awake).
///   OnThink()       — Your AI logic, called every frame after perception.
///   OnEnemyDeath()  — Cleanup when killed (navigation already disabled).
///   OnCleanup()     — Teardown on destruction (health events already unsubscribed).
///
/// Available Perception Data (read-only, updated automatically):
///   IsPlayerVisible, LastKnownPosition, HasDetectedPlayer,
///   GetDistanceToPlayer(), GetDirectionToPlayer(), Player (Transform).
///
/// Available Navigation Wrappers:
///   MoveToTarget(Vector3), StopNavigation(), SetSpeed(float), ResetSpeed(),
///   HasReachedDestination(), Velocity (Vector3), RemainingDistance (float),
///   IsOnNavMesh (bool).
///
/// Available Health Data (read-only):
///   CurrentHealth (int), MaxHealth (int), IsDead (bool).
///
/// Setup:
///   1. Create Duelist anomaly prefab with NavMeshAgent + Health.
///   2. Attach this script.
///   3. Configure perception (detectionRange, FOV) and navigation
///      (speed, acceleration) on the EnemyBase fields in the Inspector.
///   4. Set Health.maxHealth to a high value (Duelists are tanky).
/// </summary>
public class DuelistBrain : EnemyBase
{
    // ──────────────────────────────────────────────
    //  Duelist Configuration
    // ──────────────────────────────────────────────

    // TODO (Ash): Add your utility weight variables here.
    // Example fields you'll likely need:
    //
    // [Header("Utility Weights")]
    // [SerializeField] private float attackWeight   = 1.0f;
    // [SerializeField] private float strafeWeight   = 1.2f;
    // [SerializeField] private float retreatWeight  = 1.5f;
    // [SerializeField] private float coverWeight    = 1.3f;
    // [SerializeField] private float waitWeight     = 0.5f;
    //
    // [Header("Subsumption Thresholds")]
    // [SerializeField] private float criticalHealthPercent = 0.2f;
    // [SerializeField] private float aggressiveRange = 5f;

    // ──────────────────────────────────────────────
    //  Lifecycle Hooks
    // ──────────────────────────────────────────────

    /// <summary>
    /// Called once after EnemyBase has finished all its initialisation
    /// (NavMeshAgent configured, player found, death event subscribed).
    /// Use this for one-time Duelist setup.
    /// </summary>
    protected override void OnInit()
    {
        // TODO (Ash): One-time setup here.
        // Example: cache cover points, initialise action list,
        // set starting state, pre-compute utility weights, etc.
    }

    /// <summary>
    /// Called every frame after the perception system has updated
    /// IsPlayerVisible, LastKnownPosition, etc. This is where
    /// the Utility scoring and Subsumption overrides run.
    ///
    /// Available perception data you can read:
    ///   IsPlayerVisible      — true if player is in range + FOV + LOS
    ///   LastKnownPosition    — last place the player was seen
    ///   HasDetectedPlayer    — true if player has ever been spotted
    ///   GetDistanceToPlayer()— current distance to the player
    ///   GetDirectionToPlayer() — normalized direction to the player
    ///   Player               — the player's Transform
    ///
    /// Available navigation commands:
    ///   MoveToTarget(Vector3) — pathfind to a position
    ///   StopNavigation()      — halt immediately
    ///   SetSpeed(float)       — change movement speed
    ///   Velocity              — current NavMeshAgent velocity (read-only)
    ///
    /// Available health data (for utility scoring):
    ///   CurrentHealth         — current HP (int, read-only)
    ///   MaxHealth             — max HP (int, read-only)
    /// </summary>
    protected override void OnThink()
    {
        // TODO (Ash): Implement your Utility-Based Agent here.
        //
        // Step 1: SUBSUMPTION OVERRIDES (priority layer)
        // These hard-override the utility system for survival instincts.
        //
        // if (CurrentHealth < MaxHealth * criticalHealthPercent)
        // {
        //     // FORCE retreat — survival override, ignores utility scores.
        //     ExecuteRetreat();
        //     return;
        // }
        //
        // Step 2: UTILITY SCORING (decision layer)
        // Score every available action against weighted input variables.
        //
        // float attackScore  = ScoreAttack(distance, los, health);
        // float strafeScore  = ScoreStrafe(distance, timeExposed);
        // float retreatScore = ScoreRetreat(health, playerAggression);
        // float coverScore   = ScoreCover(los, timeExposed);
        // float waitScore    = ScoreWait(distance, los);
        //
        // Step 3: EXECUTE the highest-scoring action.
        //
        // Action best = GetHighestScoringAction();
        // switch (best)
        // {
        //     case Action.Attack:  ExecuteAttack();  break;
        //     case Action.Strafe:  ExecuteStrafe();  break;
        //     case Action.Retreat: ExecuteRetreat(); break;
        //     case Action.Cover:   ExecuteCover();   break;
        //     case Action.Wait:    ExecuteWait();    break;
        // }
    }

    /// <summary>
    /// Called once when this Duelist is killed. Navigation is already
    /// disabled by the time this runs. Use for Duelist-specific cleanup.
    /// </summary>
    protected override void OnEnemyDeath()
    {
        // TODO (Ash): Cleanup here.
        // Example: stop attack coroutines, play death animation,
        // disable weapon VFX, drop loot, etc.
    }

    /// <summary>
    /// Called on destruction after base cleanup (health events
    /// unsubscribed). Use for final teardown if needed.
    /// </summary>
    protected override void OnCleanup()
    {
        // TODO (Ash): Final teardown here if needed.
        // Example: unsubscribe from external events, release pooled assets.
    }
}
