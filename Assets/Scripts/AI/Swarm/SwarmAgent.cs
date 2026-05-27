using System.Collections.Generic;
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
///   1. Create Swarm drone prefab with NavMeshAgent + Health + SwarmAttack.
///   2. Attach this script.
///   3. Configure perception (detectionRange, FOV) and navigation
///      (speed, acceleration) on the EnemyBase fields in the Inspector.
///   4. Set Health.maxHealth to a low value (Swarm drones are fragile).
///   5. Tune Boids weights and radii below to taste.
/// </summary>
public class SwarmAgent : EnemyBase
{
    // ──────────────────────────────────────────────
    //  Boids Configuration
    // ──────────────────────────────────────────────

    [Header("Boids — Weights")]
    [SerializeField, Tooltip("How strongly drones repel nearby neighbours.")]
    private float separationWeight = 1.5f;

    [SerializeField, Tooltip("How strongly drones match neighbour headings.")]
    private float alignmentWeight = 1.0f;

    [SerializeField, Tooltip("How strongly drones steer toward the group center.")]
    private float cohesionWeight = 1.0f;

    [SerializeField, Tooltip("How strongly drones pursue the player.")]
    private float pursuitWeight = 2.0f;

    [Header("Boids — Radii")]
    [SerializeField, Tooltip("Radius for finding Boids neighbours (alignment & cohesion).")]
    private float neighborRadius = 8f;

    [SerializeField, Tooltip("Inner radius for strong separation repulsion. " +
        "Should be smaller than neighborRadius.")]
    private float separationRadius = 3f;

    [Header("Boids — Limits")]
    [SerializeField, Tooltip("Maximum magnitude of the combined steering vector.")]
    private float maxSteerForce = 10f;

    [SerializeField, Tooltip("How far ahead to project the steering target. " +
        "Higher values make movement smoother but less responsive.")]
    private float steerTargetDistance = 5f;

    // ──────────────────────────────────────────────
    //  Attack Configuration
    // ──────────────────────────────────────────────

    [Header("Attack")]
    [SerializeField, Tooltip("Distance at which the drone can hit the player.")]
    private float attackRange = 2.5f;

    // ──────────────────────────────────────────────
    //  Cached References
    // ──────────────────────────────────────────────

    /// <summary>Sibling attack component for dealing damage.</summary>
    private SwarmAttack swarmAttack;

    // ──────────────────────────────────────────────
    //  Lifecycle Hooks
    // ──────────────────────────────────────────────

    /// <summary>
    /// Called once after EnemyBase has finished all its initialisation
    /// (NavMeshAgent configured, player found, death event subscribed).
    /// Registers with SwarmFormation and caches the attack component.
    /// </summary>
    protected override void OnInit()
    {
        // Register with the centralised formation manager.
        SwarmFormation.Instance.Register(this);

        // Cache the sibling attack component.
        swarmAttack = GetComponent<SwarmAttack>();
        if (swarmAttack == null)
        {
            Debug.LogWarning($"[SwarmAgent] '{gameObject.name}' is missing a SwarmAttack " +
                             "component. Attacks will be disabled.", this);
        }
    }

    /// <summary>
    /// Called every frame after the perception system has updated
    /// IsPlayerVisible, LastKnownPosition, etc. Runs the Boids
    /// algorithm and triggers attacks when in range.
    /// </summary>
    protected override void OnThink()
    {
        // Idle until the player has been spotted at least once.
        if (!HasDetectedPlayer) return;

        // --- Gather neighbours ---
        List<SwarmAgent> neighbours = SwarmFormation.Instance.GetNeighbours(this, neighborRadius);

        // --- Calculate Boids steering forces ---
        Vector3 separation = CalculateSeparation(neighbours);
        Vector3 alignment  = CalculateAlignment(neighbours);
        Vector3 cohesion   = CalculateCohesion(neighbours);
        Vector3 pursuit    = CalculatePursuit();

        // --- Combine weighted forces ---
        Vector3 steering = separation * separationWeight
                         + alignment  * alignmentWeight
                         + cohesion   * cohesionWeight
                         + pursuit    * pursuitWeight;

        // Clamp to maximum steering force.
        if (steering.sqrMagnitude > maxSteerForce * maxSteerForce)
        {
            steering = steering.normalized * maxSteerForce;
        }

        // --- Execute movement through EnemyBase wrapper ---
        // Only issue a move command when steering has meaningful magnitude.
        // Normalising a near-zero vector would amplify tiny residual forces
        // into full-speed jitter when the drone is already well-positioned.
        const float kMinSteerSqr = 0.01f;
        if (steering.sqrMagnitude > kMinSteerSqr)
        {
            Vector3 targetPosition = transform.position + steering.normalized * steerTargetDistance;
            MoveToTarget(targetPosition);
        }

        // --- Attack if in range and player is visible ---
        TryAttackPlayer();
    }

    /// <summary>
    /// Called once when this drone is killed. Navigation is already
    /// disabled by the time this runs. Unregisters from SwarmFormation.
    /// </summary>
    protected override void OnEnemyDeath()
    {
        // Guard: only unregister if the formation singleton still exists.
        // Avoids creating a new GameObject during scene teardown.
        if (SwarmFormation.HasInstance)
        {
            SwarmFormation.Instance.Unregister(this);
        }
    }

    /// <summary>
    /// Called on destruction after base cleanup (health events
    /// unsubscribed). Safety unregister in case death didn't fire.
    /// </summary>
    protected override void OnCleanup()
    {
        // Guard: only unregister if the formation singleton still exists.
        // During scene teardown, the SwarmFormation may have already been
        // destroyed. Calling Instance here would auto-create a new
        // GameObject mid-teardown, causing Unity warnings.
        if (SwarmFormation.HasInstance)
        {
            SwarmFormation.Instance.Unregister(this);
        }
    }

    // ──────────────────────────────────────────────
    //  Boids — Separation
    // ──────────────────────────────────────────────

    /// <summary>
    /// Steers away from neighbours that are too close (within
    /// separationRadius). Uses inverse-distance weighting so
    /// closer drones exert stronger repulsion.
    /// </summary>
    private Vector3 CalculateSeparation(List<SwarmAgent> neighbours)
    {
        Vector3 force = Vector3.zero;
        int count = 0;

        float separationRadiusSqr = separationRadius * separationRadius;

        for (int i = 0; i < neighbours.Count; i++)
        {
            // Guard: neighbour may have been destroyed mid-frame.
            if (neighbours[i] == null) continue;

            Vector3 offset = transform.position - neighbours[i].transform.position;
            float distSqr = offset.sqrMagnitude;

            if (distSqr < separationRadiusSqr && distSqr > 0.001f)
            {
                // Inverse-distance weighting: closer = stronger push.
                force += offset / distSqr;
                count++;
            }
        }

        if (count > 0)
        {
            force /= count;
        }

        return force;
    }

    // ──────────────────────────────────────────────
    //  Boids — Alignment
    // ──────────────────────────────────────────────

    /// <summary>
    /// Steers toward the average velocity heading of nearby neighbours.
    /// Uses the Velocity property from EnemyBase (read-only NavMeshAgent
    /// velocity accessor).
    /// </summary>
    private Vector3 CalculateAlignment(List<SwarmAgent> neighbours)
    {
        if (neighbours.Count == 0) return Vector3.zero;

        Vector3 averageVelocity = Vector3.zero;
        int validCount = 0;

        for (int i = 0; i < neighbours.Count; i++)
        {
            // Guard: neighbour may have been destroyed mid-frame.
            if (neighbours[i] == null) continue;
            averageVelocity += neighbours[i].Velocity;
            validCount++;
        }

        if (validCount == 0) return Vector3.zero;

        averageVelocity /= validCount;

        // Return the desired steering adjustment (difference from our velocity).
        return averageVelocity - Velocity;

    // ──────────────────────────────────────────────
    //  Boids — Cohesion
    // ──────────────────────────────────────────────

    /// <summary>
    /// Steers toward the centroid (average position) of nearby
    /// neighbours, pulling the drone toward the group center.
    /// </summary>
    private Vector3 CalculateCohesion(List<SwarmAgent> neighbours)
    {
        if (neighbours.Count == 0) return Vector3.zero;

        Vector3 centroid = Vector3.zero;
        int validCount = 0;

        for (int i = 0; i < neighbours.Count; i++)
        {
            // Guard: neighbour may have been destroyed mid-frame.
            if (neighbours[i] == null) continue;
            centroid += neighbours[i].transform.position;
            validCount++;
        }

        if (validCount == 0) return Vector3.zero;

        centroid /= validCount;

        // Steer toward the centroid.
        return (centroid - transform.position).normalized;

    // ──────────────────────────────────────────────
    //  Boids — Pursuit
    // ──────────────────────────────────────────────

    /// <summary>
    /// Steers toward the player's current position when visible, or
    /// toward the LastKnownPosition when line-of-sight is broken.
    /// This gives drones a "search" behavior after losing sight.
    /// </summary>
    private Vector3 CalculatePursuit()
    {
        Vector3 targetPosition;

        if (IsPlayerVisible && Player != null)
        {
            // Steer directly toward the player's current position.
            targetPosition = Player.position;
        }
        else
        {
            // Player not visible — head to last known position.
            targetPosition = LastKnownPosition;
        }

        Vector3 toTarget = targetPosition - transform.position;

        // Avoid zero-length vectors when already at the target.
        if (toTarget.sqrMagnitude < 0.01f) return Vector3.zero;

        return toTarget.normalized;
    }

    // ──────────────────────────────────────────────
    //  Attack
    // ──────────────────────────────────────────────

    /// <summary>
    /// Checks if the player is within attack range and visible, then
    /// delegates to SwarmAttack to deal damage through IDamageable.
    /// </summary>
    private void TryAttackPlayer()
    {
        if (swarmAttack == null) return;
        if (!IsPlayerVisible) return;
        if (Player == null) return;

        float distance = GetDistanceToPlayer();
        if (distance <= attackRange)
        {
            swarmAttack.TryAttack(Player.gameObject);
        }
    }
}
