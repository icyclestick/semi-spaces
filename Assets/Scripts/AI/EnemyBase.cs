using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Abstract base class for all enemy types in Semi-Spaces. Provides the
/// shared Perception, Execution, and Health Lifecycle layers described
/// in the game's software architecture. Subclasses (SwarmAgent,
/// DuelistBrain) implement the Decision Layer with their specific
/// AI algorithms.
///
/// Architecture Layers Handled:
///   PERCEPTION   — Player tracking, line-of-sight, Last Known Position.
///   EXECUTION    — NavMeshAgent wrappers for movement commands.
///   LIFECYCLE    — Health event subscription, death shutdown sequence.
///
/// Architecture Layers NOT Handled (subclass responsibility):
///   DECISION     — Boids steering (Swarm), Utility scoring (Duelist).
///
/// Design Decisions:
///   - NavMeshAgent is PRIVATE. Subclasses use wrappers (MoveToTarget,
///     StopNavigation, etc.) to prevent bypassing the execution layer.
///   - Awake/Update/OnDestroy are PRIVATE. Subclasses override virtual
///     hooks (OnInit, OnTick) so missing base calls can never silently
///     break component caching, perception, or death subscriptions.
///   - Perception uses sqrMagnitude and Dot instead of magnitude and
///     Vector3.Angle to minimize per-frame cost with 20+ Swarm drones.
///
/// Setup:
///   1. Create your enemy prefab with a NavMeshAgent and Health component.
///   2. Attach a subclass of EnemyBase (e.g., SwarmAgent) — NOT this
///      class directly (it's abstract).
///   3. Bake the NavMesh in your scene.
///   4. Tag the Player GameObject as "Player" (or assign manually).
///   5. Set up LayerMasks for line-of-sight raycasting in the Inspector.
///
/// For AI developers (Jen &amp; Ash):
///   Override OnThink() for your decision logic.
///   Override OnEnemyDeath() for cleanup.
///   Override OnInit() if you need one-time setup after base initialization.
///   Use the perception data (IsPlayerVisible, LastKnownPosition) and
///   navigation wrappers (MoveToTarget, StopNavigation) — do NOT
///   access the NavMeshAgent directly (it's private).
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(Health))]
public abstract class EnemyBase : MonoBehaviour
{
    // ──────────────────────────────────────────────
    //  Perception Configuration
    // ──────────────────────────────────────────────

    [Header("Perception")]
    [SerializeField, Tooltip("Maximum distance this enemy can detect the player.")]
    private float detectionRange = 25f;

    [SerializeField, Tooltip("Half-angle of the enemy's forward vision cone (in degrees). " +
        "180 = can see in all directions. 60 = narrow forward cone.")]
    private float fieldOfViewAngle = 90f;

    [SerializeField, Tooltip("Layers that block line-of-sight (walls, obstacles). " +
        "The Player's layer should NOT be included here.")]
    private LayerMask obstacleMask;

    [SerializeField, Tooltip("How often perception updates run (in seconds). " +
        "Lower = more responsive but more expensive. 0.1 = 10 checks/sec.")]
    private float perceptionTickRate = 0.15f;

    // ──────────────────────────────────────────────
    //  Navigation Configuration
    // ──────────────────────────────────────────────

    [Header("Navigation")]
    [SerializeField, Tooltip("Default movement speed for the NavMeshAgent.")]
    private float defaultSpeed = 3.5f;

    [SerializeField, Tooltip("Acceleration of the NavMeshAgent.")]
    private float acceleration = 8f;

    [SerializeField, Tooltip("Distance at which the agent considers itself 'at' the destination.")]
    private float stoppingDistance = 1f;

    // ──────────────────────────────────────────────
    //  Cached Components (PRIVATE — use wrappers)
    // ──────────────────────────────────────────────

    /// <summary>
    /// The NavMeshAgent driving pathfinding for this enemy.
    /// PRIVATE — subclasses must use the navigation wrappers
    /// (MoveToTarget, StopNavigation, etc.) instead of accessing
    /// this directly. This enforces the execution layer boundary.
    /// </summary>
    private NavMeshAgent agent;

    /// <summary>The Health component handling damage and death.</summary>
    private Health health;

    // ──────────────────────────────────────────────
    //  Perception State (read by subclasses)
    // ──────────────────────────────────────────────

    /// <summary>Reference to the Player's Transform. Found via tag on Awake.</summary>
    [SerializeField] private Transform player;

    /// <summary>Public property for accessing the player Transform.</summary>
    protected Transform Player
    {
        get => player;
        private set => player = value;
    }

    /// <summary>
    /// True if the player is within detection range, inside the FOV
    /// cone, AND not occluded by obstacles. Updated every perception tick.
    /// </summary>
    public bool IsPlayerVisible { get; private set; }

    /// <summary>
    /// The last world-space position where the player was seen.
    /// Persists after LOS is broken so the enemy can search/pursue.
    /// Initialised to Vector3.zero (no known position).
    /// </summary>
    public Vector3 LastKnownPosition { get; private set; }

    /// <summary>
    /// True once the enemy has detected the player at least once.
    /// Useful for distinguishing "never seen the player" from
    /// "lost sight of the player."
    /// </summary>
    public bool HasDetectedPlayer { get; private set; }

    /// <summary>True once the death sequence has started. Prevents double-processing.</summary>
    public bool IsDead { get; private set; }

    // ──────────────────────────────────────────────
    //  Navigation Accessors (read-only for subclasses)
    // ──────────────────────────────────────────────

    /// <summary>
    /// The NavMeshAgent's current velocity vector (read-only).
    /// Useful for Boids alignment and predictive steering.
    /// </summary>
    protected Vector3 Velocity => agent != null ? agent.velocity : Vector3.zero;

    /// <summary>
    /// Remaining path distance to the current destination (read-only).
    /// Returns float.MaxValue if the agent has no path.
    /// </summary>
    protected float RemainingDistance =>
        agent != null && agent.isOnNavMesh && agent.hasPath
            ? agent.remainingDistance
            : float.MaxValue;

    /// <summary>
    /// True if the NavMeshAgent is currently on a NavMesh (read-only).
    /// </summary>
    protected bool IsOnNavMesh => agent != null && agent.isOnNavMesh;

    /// <summary>
    /// The Health component's current health (read-only convenience).
    /// Useful for Duelist utility scoring (health-based retreat logic).
    /// </summary>
    protected int CurrentHealth => health != null ? health.CurrentHealth : 0;

    /// <summary>
    /// The Health component's max health (read-only convenience).
    /// </summary>
    protected int MaxHealth => health != null ? health.MaxHealth : 0;

    // ──────────────────────────────────────────────
    //  Internal State
    // ──────────────────────────────────────────────

    /// <summary>
    /// Timer for throttling perception updates. Initialised to a random
    /// offset so enemies spawned on the same frame don't all raycast
    /// simultaneously (spreads the load across multiple frames).
    /// </summary>
    private float perceptionTimer;

    /// <summary>
    /// Precomputed squared detection range. Avoids a sqrt per perception
    /// tick by comparing sqrMagnitude directly.
    /// </summary>
    private float detectionRangeSqr;

    /// <summary>
    /// Precomputed dot product threshold for the FOV cone check.
    /// cos(fieldOfViewAngle) — avoids Acos per tick by comparing
    /// a dot product directly.
    /// </summary>
    private float fovDotThreshold;

    // ──────────────────────────────────────────────
    //  Lifecycle (PRIVATE — subclasses use hooks)
    // ──────────────────────────────────────────────
    //
    //  Awake, Update, and OnDestroy are intentionally NOT virtual.
    //  If a subclass overrides them, they can't accidentally skip
    //  component caching, perception ticks, or death cleanup.
    //
    //  Instead, subclasses override:
    //    OnInit()        — called at the end of Awake
    //    OnThink()       — called every frame after perception
    //    OnEnemyDeath()  — called after the death shutdown
    //

    /// <summary>
    /// Caches components, finds the player, configures the NavMeshAgent,
    /// precomputes perception thresholds, subscribes to death events,
    /// and calls the OnInit() hook for subclass setup.
    /// </summary>
    private void Awake()
    {
        // --- Cache required components ---
        agent  = GetComponent<NavMeshAgent>();
        health = GetComponent<Health>();

        // --- Configure NavMeshAgent defaults ---
        agent.speed           = defaultSpeed;
        agent.acceleration    = acceleration;
        agent.stoppingDistance = stoppingDistance;

        // --- Clamp and precompute perception values ---
        perceptionTickRate = Mathf.Max(perceptionTickRate, 0.05f);
        CachePerceptionThresholds();

        // --- Stagger perception start ---
        // Randomise the initial timer so enemies spawned on the same
        // frame don't all fire UpdatePerception() simultaneously.
        perceptionTimer = Random.Range(0f, perceptionTickRate);

        // --- Find the player ---
        if (player == null)
        {
            try
            {
                GameObject playerObj = GameObject.FindWithTag("Player");
                if (playerObj != null)
                {
                    Player = playerObj.transform;
                }
                else
                {
                    Debug.LogWarning($"[EnemyBase] '{gameObject.name}' could not find a GameObject " +
                                     "tagged 'Player'. Assign the tag or set Player manually.", this);
                }
            }
            catch (UnityException ex)
            {
                Debug.LogWarning($"[EnemyBase] '{gameObject.name}' encountered an error finding the Player: {ex.Message}. " +
                                 "Assign Player manually in the Inspector.", this);
            }
        }

        // --- Subscribe to the Health death event ---
        health.OnDeath += HandleDeath;

        // --- Subclass initialisation hook ---
        OnInit();
    }

    /// <summary>
    /// Runs perception updates on a throttled timer and calls the
    /// subclass decision method via OnThink().
    /// </summary>
    private void Update()
    {
        if (IsDead) return;

        // --- Throttled perception ---
        perceptionTimer -= Time.deltaTime;
        if (perceptionTimer <= 0f)
        {
            perceptionTimer = perceptionTickRate;
            UpdatePerception();
        }

        // --- Decision tick ---
        OnThink();
    }

    /// <summary>
    /// Validates Inspector values to ensure safe runtime behavior.
    /// Called automatically when values are changed in the Inspector.
    /// </summary>
    private void OnValidate()
    {
        perceptionTickRate = Mathf.Max(perceptionTickRate, 0.05f);
        CachePerceptionThresholds();
    }

    /// <summary>
    /// Unsubscribes from the Health death event to prevent leaks.
    /// Calls OnCleanup() hook for subclass teardown.
    /// </summary>
    private void OnDestroy()
    {
        if (health != null)
        {
            health.OnDeath -= HandleDeath;
        }

        OnCleanup();
    }

    // ──────────────────────────────────────────────
    //  Perception
    // ──────────────────────────────────────────────

    /// <summary>
    /// Precomputes squared range and FOV dot threshold so the per-tick
    /// perception check avoids expensive sqrt and Acos operations.
    /// Called once in Awake and again from OnValidate when Inspector
    /// values change.
    /// </summary>
    private void CachePerceptionThresholds()
    {
        detectionRangeSqr = detectionRange * detectionRange;
        fovDotThreshold   = Mathf.Cos(fieldOfViewAngle * Mathf.Deg2Rad);
    }

    /// <summary>
    /// Performs the full perception pipeline using optimised math:
    ///   1. Range check — sqrMagnitude vs detectionRange² (no sqrt).
    ///   2. FOV check  — Dot product vs cos(fieldOfViewAngle) (no Acos).
    ///   3. LOS check  — Raycast for obstacle occlusion.
    ///
    /// If all three pass, the player is visible and the Last Known
    /// Position is updated. If any fail, IsPlayerVisible is set to
    /// false but the LKP is preserved for search behavior.
    /// </summary>
    private void UpdatePerception()
    {
        if (Player == null)
        {
            IsPlayerVisible = false;
            return;
        }

        Vector3 toPlayer = Player.position - transform.position;
        float   distSqr  = toPlayer.sqrMagnitude;

        // --- Range check (no sqrt) ---
        if (distSqr > detectionRangeSqr)
        {
            IsPlayerVisible = false;
            return;
        }

        // --- FOV check (dot product, no Acos) ---
        // Normalise the direction vector, then compare its dot product
        // with forward against the precomputed cosine threshold.
        // Dot > threshold means the angle is within the FOV cone.
        float distance = Mathf.Sqrt(distSqr); // Need actual distance for the raycast below.
        Vector3 dirToPlayer = toPlayer / distance; // Manual normalise (reuse the sqrt).

        float dot = Vector3.Dot(transform.forward, dirToPlayer);
        if (dot < fovDotThreshold)
        {
            IsPlayerVisible = false;
            return;
        }

        // --- Line-of-sight check ---
        Vector3 rayOrigin = transform.position + Vector3.up * 1f; // Eye height.
        Vector3 rayDir    = (Player.position + Vector3.up * 1f) - rayOrigin;

        if (Physics.Raycast(rayOrigin, rayDir.normalized, out RaycastHit hit,
                            distance, obstacleMask, QueryTriggerInteraction.Ignore))
        {
            IsPlayerVisible = false;
            return;
        }

        // --- All checks passed: player is visible ---
        IsPlayerVisible     = true;
        HasDetectedPlayer   = true;
        LastKnownPosition   = Player.position;
    }

    // ──────────────────────────────────────────────
    //  Navigation Wrappers (Execution Layer)
    // ──────────────────────────────────────────────
    //
    //  Subclasses use these instead of accessing the NavMeshAgent
    //  directly. The agent field is private — these are the ONLY
    //  way to command movement.
    //

    /// <summary>
    /// Commands the NavMeshAgent to pathfind to the given world position.
    /// If the agent is stopped, it is resumed automatically.
    /// </summary>
    /// <param name="destination">World-space target position.</param>
    public void MoveToTarget(Vector3 destination)
    {
        if (IsDead || !agent.isOnNavMesh) return;

        agent.isStopped = false;
        agent.SetDestination(destination);
    }

    /// <summary>
    /// Overrides the NavMeshAgent's speed. Use this for context-sensitive
    /// speed changes (e.g., Duelist retreating at half speed).
    /// </summary>
    /// <param name="speed">New speed in units/sec.</param>
    public void SetSpeed(float speed)
    {
        if (IsDead) return;
        agent.speed = speed;
    }

    /// <summary>
    /// Resets the NavMeshAgent speed to the Inspector-configured default.
    /// </summary>
    public void ResetSpeed()
    {
        if (IsDead) return;
        agent.speed = defaultSpeed;
    }

    /// <summary>
    /// Halts the NavMeshAgent immediately. The agent stays on the
    /// NavMesh but stops moving and pathfinding.
    /// </summary>
    public void StopNavigation()
    {
        if (!agent.isOnNavMesh) return;

        agent.isStopped = true;
        agent.ResetPath();
    }

    /// <summary>
    /// Returns true if the agent has reached its current destination
    /// (within stopping distance and not computing a new path).
    /// </summary>
    public bool HasReachedDestination()
    {
        if (!agent.isOnNavMesh) return false;
        if (agent.pathPending)  return false;
        if (!agent.hasPath)     return false;
        if (agent.pathStatus != NavMeshPathStatus.PathComplete) return false;

        return agent.remainingDistance <= agent.stoppingDistance;
    }

    // ──────────────────────────────────────────────
    //  Utility Helpers
    // ──────────────────────────────────────────────

    /// <summary>
    /// Returns the distance between this enemy and the player.
    /// Returns float.MaxValue if the player reference is null.
    /// </summary>
    protected float GetDistanceToPlayer()
    {
        if (Player == null) return float.MaxValue;
        return Vector3.Distance(transform.position, Player.position);
    }

    /// <summary>
    /// Returns the direction from this enemy toward the player (normalized).
    /// Returns Vector3.zero if the player reference is null.
    /// </summary>
    protected Vector3 GetDirectionToPlayer()
    {
        if (Player == null) return Vector3.zero;
        return (Player.position - transform.position).normalized;
    }

    // ──────────────────────────────────────────────
    //  Death Lifecycle
    // ──────────────────────────────────────────────

    /// <summary>
    /// Internal handler subscribed to Health.OnDeath. Performs the
    /// shutdown sequence:
    ///   1. Set IsDead flag (stops Update processing).
    ///   2. Disable the NavMeshAgent (stops pathfinding).
    ///   3. Log the death for wave tracking.
    ///   4. Notify the subclass via OnEnemyDeath().
    ///
    /// This is NOT virtual — the shutdown sequence is non-negotiable.
    /// Subclasses use OnEnemyDeath() for custom cleanup.
    /// </summary>
    private void HandleDeath()
    {
        if (IsDead) return;

        IsDead = true;

        // --- Shut down navigation ---
        if (agent.isOnNavMesh)
        {
            agent.isStopped = true;
            agent.ResetPath();
        }
        agent.enabled = false;

        Debug.Log($"[EnemyBase] '{gameObject.name}' has been eliminated.", this);

        // --- Notify the subclass ---
        OnEnemyDeath();
    }

    // ──────────────────────────────────────────────
    //  Virtual Hooks (for subclasses)
    // ──────────────────────────────────────────────

    /// <summary>
    /// Called once at the end of Awake(), after all base initialization
    /// is complete (components cached, NavMeshAgent configured, player
    /// found, death event subscribed). Override this for one-time
    /// subclass setup — no need to call base.
    ///
    /// Example: cache nearby Swarm neighbours, initialise utility
    /// weights, start ambient coroutines.
    /// </summary>
    protected virtual void OnInit() { }

    /// <summary>
    /// Called every frame (after perception updates). This is where
    /// the subclass implements its AI algorithm:
    ///   - SwarmAgent: Boids steering calculation.
    ///   - DuelistBrain: Utility function evaluation.
    ///
    /// Use the perception data (IsPlayerVisible, LastKnownPosition,
    /// GetDistanceToPlayer) and navigation wrappers (MoveToTarget,
    /// StopNavigation) to drive behavior.
    /// </summary>
    protected abstract void OnThink();

    /// <summary>
    /// Called once when this enemy is killed, after the base shutdown
    /// sequence (NavMeshAgent disabled, IsDead set). Override this to
    /// run subclass-specific cleanup (stop coroutines, disable particle
    /// effects, trigger death animations, etc.).
    ///
    /// NOTE: Navigation is already disabled by the time this is called.
    /// The Health component handles the actual Destroy(gameObject).
    /// </summary>
    protected virtual void OnEnemyDeath() { }

    /// <summary>
    /// Called from OnDestroy() after the base has unsubscribed from
    /// Health events. Override this for subclass teardown if needed
    /// (e.g., unsubscribing from external events, clearing static
    /// references). No need to call base.
    /// </summary>
    protected virtual void OnCleanup() { }
}
