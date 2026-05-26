using UnityEngine;

/// <summary>
/// The Swarm enemy brain — implements the Boids algorithm for group
/// movement and player pursuit. Inherits from EnemyBase, which provides
/// perception, navigation wrappers, and health lifecycle automatically.
///
/// Architecture:
///   EnemyBase handles: component caching, perception, NavMesh, death.
///   SwarmAgent handles: Boids steering (separation, alignment, cohesion, pursuit).
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
///   1. Create Swarm drone prefab with NavMeshAgent + Health.
///   2. Attach this script.
///   3. Configure perception (detectionRange, FOV) and navigation
///      (speed, acceleration) on the EnemyBase fields in the Inspector.
///   4. Set Health.maxHealth to a low value (Swarm drones are fragile).
/// </summary>
public class SwarmAgent : EnemyBase
{
    // ──────────────────────────────────────────────
    //  Swarm Configuration
    // ──────────────────────────────────────────────

    // TODO (Jen): Add your Boids tuning variables here.
    // Example fields you'll likely need:
    //
    // [Header("Boids")]
    // [SerializeField] private float separationWeight = 1.5f;
    // [SerializeField] private float alignmentWeight  = 1.0f;
    // [SerializeField] private float cohesionWeight    = 1.0f;
    // [SerializeField] private float pursuitWeight     = 2.0f;
    // [SerializeField] private float neighborRadius    = 5f;

    // ──────────────────────────────────────────────
    //  Lifecycle Hooks
    // ──────────────────────────────────────────────

    /// <summary>
    /// Called once after EnemyBase has finished all its initialisation
    /// (NavMeshAgent configured, player found, death event subscribed).
    /// Use this for one-time Swarm setup.
    /// </summary>
    protected override void OnInit()
    {
        // TODO (Jen): One-time setup here.
        // Example: find nearby SwarmAgents, cache references,
        // register with a SwarmFormation manager, etc.
    }

    /// <summary>
    /// Called every frame after the perception system has updated
    /// IsPlayerVisible, LastKnownPosition, etc. This is where
    /// the Boids algorithm runs.
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
    /// </summary>
    protected override void OnThink()
    {
        // TODO (Jen): Implement your Boids algorithm here.
        //
        // Pseudocode:
        //   1. Find all SwarmAgent neighbours within neighborRadius.
        //   2. Calculate separation vector (steer away from nearby drones).
        //   3. Calculate alignment vector (match heading of nearby drones).
        //   4. Calculate cohesion vector (steer toward group center).
        //   5. Calculate pursuit vector (steer toward predicted player position).
        //   6. Combine: steering = sep * w1 + align * w2 + cohesion * w3 + pursuit * w4
        //   7. MoveToTarget(transform.position + steering);
        //
        // Example skeleton:
        //
        // if (!HasDetectedPlayer) return; // Idle until player is spotted.
        //
        // Vector3 separation = CalculateSeparation();
        // Vector3 alignment  = CalculateAlignment();
        // Vector3 cohesion   = CalculateCohesion();
        // Vector3 pursuit    = CalculatePursuit();
        //
        // Vector3 steering = separation * separationWeight
        //                   + alignment * alignmentWeight
        //                   + cohesion  * cohesionWeight
        //                   + pursuit   * pursuitWeight;
        //
        // MoveToTarget(transform.position + steering.normalized * 5f);
    }

    /// <summary>
    /// Called once when this drone is killed. Navigation is already
    /// disabled by the time this runs. Use for Swarm-specific cleanup.
    /// </summary>
    protected override void OnEnemyDeath()
    {
        // TODO (Jen): Cleanup here.
        // Example: notify SwarmFormation that this drone died,
        // play a death VFX, remove from neighbour lists, etc.
    }

    /// <summary>
    /// Called on destruction after base cleanup (health events
    /// unsubscribed). Use for final teardown if needed.
    /// </summary>
    protected override void OnCleanup()
    {
        // TODO (Jen): Final teardown here if needed.
        // Example: unsubscribe from static events, clear cached lists.
    }
}
