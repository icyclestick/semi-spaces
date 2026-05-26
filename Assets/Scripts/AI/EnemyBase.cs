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
/// Setup:
///   1. Create your enemy prefab with a NavMeshAgent and Health component.
///   2. Attach a subclass of EnemyBase (e.g., SwarmAgent) — NOT this
///      class directly (it's abstract).
///   3. Bake the NavMesh in your scene.
///   4. Tag the Player GameObject as "Player" (or assign manually).
///   5. Set up LayerMasks for line-of-sight raycasting in the Inspector.
///
/// For AI developers (Jen & Ash):
///   Override OnThink() for your decision logic.
///   Override OnEnemyDeath() for cleanup.
///   Use the perception data (IsPlayerVisible, LastKnownPosition) and
///   navigation wrappers (MoveToTarget, StopNavigation) — do NOT
///   access the NavMeshAgent directly.
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
    //  Cached Components
    // ──────────────────────────────────────────────

    /// <summary>The NavMeshAgent driving pathfinding for this enemy.</summary>
    protected NavMeshAgent Agent { get; private set; }

    /// <summary>The Health component handling damage and death.</summary>
    protected Health Health { get; private set; }

    // ──────────────────────────────────────────────
    //  Perception State (read by subclasses)
    // ──────────────────────────────────────────────

    /// <summary>Reference to the Player's Transform. Found via tag on Awake.</summary>
    [SerializeField] private Transform player;

    /// <summary>Public property for accessing the player Transform.</summary>
    protected Transform Player
    {
        get => player;
        protected set => player = value;
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
    //  Internal State
    // ──────────────────────────────────────────────

    /// <summary>Timer for throttling perception updates.</summary>
    private float perceptionTimer;

    // ──────────────────────────────────────────────
    //  Lifecycle
    // ──────────────────────────────────────────────

    /// <summary>
    /// Caches components, finds the player, configures the NavMeshAgent,
    /// and subscribes to the Health death event. Subclasses that override
    /// Awake MUST call base.Awake().
    /// </summary>
    protected virtual void Awake()
    {
        // --- Cache required components ---
        Agent  = GetComponent<NavMeshAgent>();
        Health = GetComponent<Health>();

        // --- Configure NavMeshAgent defaults ---
        Agent.speed           = defaultSpeed;
        Agent.acceleration    = acceleration;
        Agent.stoppingDistance = stoppingDistance;

        // --- Clamp perception tick rate to safe minimum ---
        perceptionTickRate = Mathf.Max(perceptionTickRate, 0.05f);

        // --- Find the player ---
        // Uses the "Player" tag by convention. If your player doesn't
        // have this tag, assign it in the Inspector or override this.
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
        // When Health.cs fires OnDeath, our HandleDeath method runs,
        // which shuts down navigation and notifies the subclass.
        Health.OnDeath += HandleDeath;
    }

    /// <summary>
    /// Runs perception updates on a throttled timer and calls the
    /// subclass decision method. Subclasses that override Update
    /// MUST call base.Update().
    /// </summary>
    protected virtual void Update()
    {
        if (IsDead) return;

        // --- Throttled perception ---
        // We don't need to raycast every single frame. The tick rate
        // controls how often LOS checks run (default: ~7 times/sec).
        perceptionTimer -= Time.deltaTime;
        if (perceptionTimer <= 0f)
        {
            perceptionTimer = perceptionTickRate;
            UpdatePerception();
        }

        // --- Decision tick ---
        // Subclasses implement their AI algorithm here.
        OnThink();
    }

    /// <summary>
    /// Validates Inspector values to ensure safe runtime behavior.
    /// Called automatically when values are changed in the Inspector.
    /// </summary>
    protected virtual void OnValidate()
    {
        // Clamp perception tick rate to prevent per-frame checks
        perceptionTickRate = Mathf.Max(perceptionTickRate, 0.05f);
    }

    /// <summary>
    /// Unsubscribes from the Health death event to prevent leaks.
    /// Subclasses that override OnDestroy MUST call base.OnDestroy().
    /// </summary>
    protected virtual void OnDestroy()
    {
        if (Health != null)
        {
            Health.OnDeath -= HandleDeath;
        }
    }

    // ──────────────────────────────────────────────
    //  Perception
    // ──────────────────────────────────────────────

    /// <summary>
    /// Performs the full perception pipeline:
    ///   1. Range check — is the player within detection range?
    ///   2. FOV check  — is the player inside the vision cone?
    ///   3. LOS check  — is the line-of-sight unobstructed?
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
        float   distance = toPlayer.magnitude;

        // --- Range check ---
        if (distance > detectionRange)
        {
            IsPlayerVisible = false;
            return;
        }

        // --- FOV check ---
        // Calculate the angle between our forward vector and the
        // direction to the player. If it exceeds the half-angle,
        // the player is outside our vision cone.
        float angle = Vector3.Angle(transform.forward, toPlayer);
        if (angle > fieldOfViewAngle)
        {
            IsPlayerVisible = false;
            return;
        }

        // --- Line-of-sight check ---
        // Raycast toward the player. If we hit an obstacle first,
        // the player is occluded.
        Vector3 rayOrigin = transform.position + Vector3.up * 1f; // Eye height.
        Vector3 rayDir    = (Player.position + Vector3.up * 1f) - rayOrigin;

        if (Physics.Raycast(rayOrigin, rayDir.normalized, out RaycastHit hit,
                            distance, obstacleMask, QueryTriggerInteraction.Ignore))
        {
            // Hit an obstacle before reaching the player → blocked.
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
    //  Subclasses use these instead of accessing Agent directly.
    //  This keeps the decision layer decoupled from Unity's
    //  NavMesh implementation details.
    //

    /// <summary>
    /// Commands the NavMeshAgent to pathfind to the given world position.
    /// If the agent is stopped, it is resumed automatically.
    /// </summary>
    /// <param name="destination">World-space target position.</param>
    public void MoveToTarget(Vector3 destination)
    {
        if (IsDead || !Agent.isOnNavMesh) return;

        Agent.isStopped = false;
        Agent.SetDestination(destination);
    }

    /// <summary>
    /// Overrides the NavMeshAgent's speed. Use this for context-sensitive
    /// speed changes (e.g., Duelist retreating at half speed).
    /// </summary>
    /// <param name="speed">New speed in units/sec.</param>
    public void SetSpeed(float speed)
    {
        if (IsDead) return;
        Agent.speed = speed;
    }

    /// <summary>
    /// Resets the NavMeshAgent speed to the Inspector-configured default.
    /// </summary>
    public void ResetSpeed()
    {
        if (IsDead) return;
        Agent.speed = defaultSpeed;
    }

    /// <summary>
    /// Halts the NavMeshAgent immediately. The agent stays on the
    /// NavMesh but stops moving and pathfinding.
    /// </summary>
    public void StopNavigation()
    {
        if (!Agent.isOnNavMesh) return;

        Agent.isStopped      = true;
        Agent.ResetPath();
    }

    /// <summary>
    /// Returns true if the agent has reached its current destination
    /// (within stopping distance and not computing a new path).
    /// </summary>
    public bool HasReachedDestination()
    {
        if (!Agent.isOnNavMesh) return false;
        if (Agent.pathPending)  return false;
        if (!Agent.hasPath)     return false;
        if (Agent.pathStatus != NavMeshPathStatus.PathComplete) return false;

        return Agent.remainingDistance <= Agent.stoppingDistance;
    }

    // ──────────────────────────────────────────────
    //  Utility Helpers
    // ──────────────────────────────────────────────

    /// <summary>
    /// Returns the flat (XZ) distance between this enemy and the player.
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
        if (IsDead) return; // Guard against double-fire.

        IsDead = true;

        // --- Shut down navigation ---
        if (Agent.isOnNavMesh)
        {
            Agent.isStopped = true;
            Agent.ResetPath();
        }
        Agent.enabled = false;

        Debug.Log($"[EnemyBase] '{gameObject.name}' has been eliminated.", this);

        // --- Notify the subclass ---
        // The subclass can stop coroutines, disable VFX, play death
        // animations, etc. The base class does not handle visuals.
        OnEnemyDeath();
    }

    // ──────────────────────────────────────────────
    //  Abstract / Virtual Hooks (for subclasses)
    // ──────────────────────────────────────────────

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
    /// Called once when this enemy is killed. Override this to run
    /// subclass-specific cleanup (stop coroutines, disable particle
    /// effects, trigger death animations, etc.).
    ///
    /// NOTE: Navigation is already disabled by the time this is called.
    /// The Health component handles the actual Destroy(gameObject).
    /// </summary>
    protected virtual void OnEnemyDeath() { }
}
