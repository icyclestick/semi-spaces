using UnityEngine;

/// <summary>
/// The Duelist enemy brain — implements a Maximum Expected Utility (MEU)
/// scoring system with a hard-wired subsumption priority override layer.
/// Inherits from EnemyBase, which provides perception, navigation wrappers,
/// and health lifecycle. DuelistBrain is purely the Decision Layer.
///
/// Architecture:
///   EnemyBase handles: component caching, perception, NavMesh, death.
///   DuelistBrain handles: utility function scoring + priority overrides.
///
/// State Machine (via utility scoring):
///   IDLE       — Player not yet detected. Duelist patrols or stands by.
///   SEARCH     — Player was detected but LOS is broken. Move to LKP.
///   ATTACK     — Close range, high health, player visible. Overlap + damage.
///   REPOSITION — Mid-range or poor LOS. Strafe to a better angle.
///   RETREAT    — Low health or overwhelmed. Fall back and disengage.
///
/// Golden Rules (Aisaiah's Architecture Constraints):
///   - Do NOT override Awake(), Update(), or OnDestroy() — they are private
///     in EnemyBase. Use the hooks below instead.
///   - Do NOT access the NavMeshAgent directly — it is private in EnemyBase.
///     Use MoveToTarget(), StopNavigation(), SetSpeed(), etc.
///   - Do NOT create your own Health reference — EnemyBase auto-requires it.
///     Use CurrentHealth / MaxHealth for health-based scoring.
///   - Damage MUST flow through IDamageable — never reference Health directly.
///
/// Setup:
///   1. Create a Duelist anomaly prefab with NavMeshAgent + Health component.
///   2. Attach this script (NOT EnemyBase directly — it's abstract).
///   3. Configure EnemyBase fields in the Inspector:
///      - Perception: detectionRange, fieldOfViewAngle, obstacleMask.
///      - Navigation: defaultSpeed (try 4.5), acceleration, stoppingDistance.
///   4. Configure DuelistBrain Inspector fields (Utility Weights section).
///   5. Set Health.maxHealth to a high value (Duelists are tanky — try 200).
///   6. Bake NavMesh in your scene.
///   7. Tag the Player GameObject as "Player".
/// </summary>
public class DuelistBrain : EnemyBase
{
    // ──────────────────────────────────────────────
    //  Duelist Actions (MEU candidates)
    // ──────────────────────────────────────────────

    /// <summary>
    /// Enumeration of all actions the Duelist can score and execute.
    /// OnThink() computes a utility score for each, then executes the
    /// highest-scoring one (subject to subsumption overrides).
    /// </summary>
    private enum DuelistAction
    {
        Attack,
        Retreat,
        Reposition,
    }

    // ──────────────────────────────────────────────
    //  Inspector — Utility Weights
    // ──────────────────────────────────────────────

    [Header("Utility Weights")]
    [Tooltip("Base utility multiplier for the Attack action. Higher = more aggressive.")]
    [SerializeField] private float attackWeight = 1.0f;

    [Tooltip("Base utility multiplier for the Retreat action. Higher = more survivable.")]
    [SerializeField] private float retreatWeight = 1.5f;

    [Tooltip("Base utility multiplier for the Reposition action. Higher = more tactical.")]
    [SerializeField] private float repositionWeight = 1.2f;

    // ──────────────────────────────────────────────
    //  Inspector — Subsumption Thresholds
    // ──────────────────────────────────────────────

    [Header("Subsumption Thresholds")]
    [Tooltip("Health fraction below which the survival override forces a retreat " +
             "regardless of utility scores. 0.25 = retreat when below 25% HP.")]
    [SerializeField, Range(0.01f, 0.5f)] private float criticalHealthFraction = 0.25f;

    [Tooltip("Distance (in units) at which the Duelist considers itself 'in melee range' " +
             "and Attack scores are boosted. Should be slightly larger than the attack radius.")]
    [SerializeField, Min(0.5f)] private float meleeRange = 5f;

    [Tooltip("Distance (in units) beyond which the Duelist scores Reposition " +
             "higher than Attack (too far to engage effectively).")]
    [SerializeField, Min(1f)] private float engagementRange = 12f;

    // ──────────────────────────────────────────────
    //  Inspector — Attack Configuration
    // ──────────────────────────────────────────────

    [Header("Attack")]
    [Tooltip("Radius of the physics OverlapSphere used to detect targets during an attack.")]
    [SerializeField, Min(0.1f)] private float attackRadius = 2.5f;

    [Tooltip("Damage dealt per successful melee hit via IDamageable.TakeDamage().")]
    [SerializeField, Min(1)] private int attackDamage = 20;

    [Tooltip("Minimum time (seconds) between successive attack overlaps. " +
             "Prevents hitting the player every frame.")]
    [SerializeField, Min(0.05f)] private float attackCooldown = 1.0f;

    [Tooltip("LayerMask for the attack OverlapSphere. Set this to the Player layer " +
             "so the overlap only hits valid targets and ignores geometry.")]
    [SerializeField] private LayerMask attackTargetMask;

    // ──────────────────────────────────────────────
    //  Inspector — Retreat Configuration
    // ──────────────────────────────────────────────

    [Header("Retreat")]
    [Tooltip("How far (units) the Duelist tries to flee from the player when retreating.")]
    [SerializeField, Min(1f)] private float retreatDistance = 15f;

    [Tooltip("Speed multiplier applied to the NavMeshAgent during retreat. " +
             "< 1.0 = slower retreat; > 1.0 = sprinting retreat.")]
    [SerializeField, Min(0.1f)] private float retreatSpeedMultiplier = 1.4f;

    // ──────────────────────────────────────────────
    //  Inspector — Reposition Configuration
    // ──────────────────────────────────────────────

    [Header("Reposition")]
    [Tooltip("How many random candidate positions to sample when looking for " +
             "a reposition target. Higher = better choices, more CPU per frame.")]
    [SerializeField, Range(2, 12)] private int repositionSamples = 6;

    [Tooltip("Radius around the Duelist in which reposition candidates are sampled.")]
    [SerializeField, Min(1f)] private float repositionRadius = 8f;

    // ──────────────────────────────────────────────
    //  Inspector — Decision Hysteresis
    // ──────────────────────────────────────────────

    [Header("Decision Hysteresis")]
    [Tooltip("Minimum time (seconds) the Duelist must commit to its current action " +
             "before the MEU system is allowed to switch to a different one. " +
             "Prevents rapid yo-yo thrashing between states. " +
             "Subsumption override (critical HP) always bypasses this timer.")]
    [SerializeField, Min(0.1f)] private float decisionHoldTime = 0.6f;

    // ──────────────────────────────────────────────
    //  Runtime State
    // ──────────────────────────────────────────────

    /// <summary>Cooldown timer for the attack overlap. Counts down each frame.</summary>
    private float attackTimer;

    /// <summary>Current reposition destination. Recomputed when the Duelist arrives
    /// or switches away from Reposition.</summary>
    private Vector3 repositionTarget;

    /// <summary>True when a valid reposition destination has been chosen and the
    /// Duelist has not yet arrived.</summary>
    private bool hasRepositionTarget;

    /// <summary>The last action executed in the previous OnThink() frame.
    /// Used to detect action transitions and reset per-action state.</summary>
    private DuelistAction lastAction = DuelistAction.Reposition;

    /// <summary>
    /// Counts down from decisionHoldTime each frame. The MEU system may only
    /// switch to a different action when this reaches zero, preventing rapid
    /// thrashing (yo-yo effect) caused by utility scores oscillating near a
    /// tie boundary. Resets every time a state switch occurs.
    /// </summary>
    private float decisionTimer;

    // ──────────────────────────────────────────────
    //  OnInit — One-time setup
    // ──────────────────────────────────────────────

    /// <summary>
    /// Called once at the end of EnemyBase.Awake(), after all base
    /// initialization is complete (NavMeshAgent configured, player found,
    /// death event subscribed). Initialises Duelist-specific state.
    /// </summary>
    protected override void OnInit()
    {
        // Stagger the attack cooldown slightly so Duelists spawned at the same
        // moment don't all swing on the exact same frame.
        attackTimer = Random.Range(0f, attackCooldown);

        // No reposition target assigned yet.
        hasRepositionTarget = false;

        Debug.Log($"[DuelistBrain] '{gameObject.name}' initialized. " +
                  $"MaxHealth={MaxHealth}, AttackDamage={attackDamage}, " +
                  $"MeleeRange={meleeRange}, EngagementRange={engagementRange}", this);
    }

    // ──────────────────────────────────────────────
    //  OnThink — MEU Decision Loop
    // ──────────────────────────────────────────────

    /// <summary>
    /// Called every frame by EnemyBase after the perception layer updates
    /// IsPlayerVisible, LastKnownPosition, etc.
    ///
    /// Pipeline:
    ///   1. IDLE / SEARCH guard — if the player has never been detected,
    ///      do nothing. If detected but not visible, move toward the
    ///      Last Known Position.
    ///   2. SUBSUMPTION OVERRIDE — if health is critical, force Retreat
    ///      regardless of utility scores (survival instinct).
    ///   3. MEU SCORING — compute a utility score for each candidate action
    ///      based on current perception data and health state.
    ///   4. EXECUTE — call the execution method for the highest-scoring action.
    /// </summary>
    protected override void OnThink()
    {
        // Tick both the attack cooldown and the decision hysteresis timer
        // every frame, regardless of which action is currently running.
        if (attackTimer > 0f)
            attackTimer -= Time.deltaTime;

        if (decisionTimer > 0f)
            decisionTimer -= Time.deltaTime;

        // ── GUARD: Duelist has never spotted the player ──────────────────────
        if (!HasDetectedPlayer)
        {
            // Stand idle — the perception system will alert us when the player
            // enters range + FOV + LOS. No need to waste nav calls.
            StopNavigation();
            return;
        }

        // ── GUARD: Player spotted before but not currently visible ───────────
        if (!IsPlayerVisible)
        {
            // Investigate the Last Known Position.
            ExecuteSearch();
            return;
        }

        // ── STEP 1: SUBSUMPTION OVERRIDE (priority layer) ───────────────────
        // Hard-override the utility system when health is critical.
        // This is an instinctive self-preservation response that bypasses
        // BOTH the utility scores AND the hysteresis timer — the AI must
        // always be able to flee immediately when its life is in danger.
        if (CurrentHealth < MaxHealth * criticalHealthFraction)
        {
            ExecuteRetreat(forced: true);
            return;
        }

        // ── STEP 2: MEU SCORING (decision layer) ────────────────────────────
        float distance   = GetDistanceToPlayer();
        float healthFrac = MaxHealth > 0 ? (float)CurrentHealth / MaxHealth : 1f;

        float attackScore     = ScoreAttack(distance, healthFrac);
        float retreatScore    = ScoreRetreat(distance, healthFrac);
        float repositionScore = ScoreReposition(distance, healthFrac);

        // ── STEP 3: SELECT — with hysteresis guard ───────────────────────────
        // Compute the best action the scores would select this frame.
        DuelistAction candidate = SelectBestAction(attackScore, retreatScore, repositionScore);

        // Only commit to a state switch when the decision timer has fully
        // elapsed. While the timer is still running, the Duelist keeps
        // executing its current action even if a different action now scores
        // higher. This prevents rapid yo-yo thrashing at score tie-boundaries.
        DuelistAction chosen;
        if (candidate != lastAction && decisionTimer <= 0f)
        {
            // Switch is approved — start the commitment timer for the new state.
            Debug.Log($"[DuelistBrain] '{gameObject.name}' switching: " +
                      $"{lastAction} → {candidate} | " +
                      $"scores: Attack={attackScore:F2} Retreat={retreatScore:F2} " +
                      $"Reposition={repositionScore:F2} | " +
                      $"dist={distance:F1} hp={CurrentHealth}/{MaxHealth}", this);

            lastAction    = candidate;
            decisionTimer = decisionHoldTime;   // Commit to this state for at least decisionHoldTime seconds.
            chosen        = candidate;
        }
        else
        {
            // Hold current state — either we want to stay, or the hold timer
            // hasn't expired yet.
            chosen = lastAction;
        }

        // ── STEP 4: EXECUTE ──────────────────────────────────────────────────
        switch (chosen)
        {
            case DuelistAction.Attack:
                ExecuteAttack();
                break;

            case DuelistAction.Retreat:
                ExecuteRetreat(forced: false);
                break;

            case DuelistAction.Reposition:
                ExecuteReposition();
                break;
        }
    }

    // ──────────────────────────────────────────────
    //  MEU Scoring Functions
    // ──────────────────────────────────────────────
    //
    //  Each scoring function returns a raw utility value in [0, ∞).
    //  The base Inspector weight is folded into each score so designers
    //  can tune aggression vs. survivability without touching code.
    //
    //  Variable conventions:
    //    distance  — GetDistanceToPlayer()         (0 = touching player)
    //    healthFrac — CurrentHealth / MaxHealth     (0.0 = dead, 1.0 = full HP)
    //

    /// <summary>
    /// Scores the Attack action.
    ///
    /// Attack utility rises as:
    ///   - Distance decreases (melee proximity is key).
    ///   - Health increases (healthy Duelists are bold).
    ///
    /// Formula:
    ///   proximityFactor = 1 - saturate(distance / meleeRange)
    ///       → 1.0 when right on top of the player, 0.0 at meleeRange.
    ///   boldnessFactor  = healthFrac (healthy = bold)
    ///   score           = weight * proximityFactor * boldnessFactor
    /// </summary>
    private float ScoreAttack(float distance, float healthFrac)
    {
        // Proximity utility: linear falloff from 1 to 0 over meleeRange.
        float proximityFactor = Mathf.Clamp01(1f - (distance / meleeRange));

        // Boldness: directly proportional to remaining health.
        float boldnessFactor = healthFrac;

        return attackWeight * proximityFactor * boldnessFactor;
    }

    /// <summary>
    /// Scores the Retreat action.
    ///
    /// Retreat utility rises as:
    ///   - Health decreases (wounded Duelists disengage).
    ///   - Distance decreases (player is dangerously close).
    ///
    /// Formula:
    ///   woundedFactor   = 1 - healthFrac (inverse health)
    ///   pressureFactor  = 1 - saturate(distance / meleeRange)
    ///       → spiked when the player is inside melee range.
    ///   score           = weight * (woundedFactor + pressureFactor * 0.5)
    ///
    /// The 0.5 coefficient on pressureFactor means woundedness dominates
    /// the retreat decision; proximity only nudges it.
    /// </summary>
    private float ScoreRetreat(float distance, float healthFrac)
    {
        // Inverse health: higher score the lower the HP.
        float woundedFactor = 1f - healthFrac;

        // Pressure from player proximity inside melee range.
        float pressureFactor = Mathf.Clamp01(1f - (distance / meleeRange));

        return retreatWeight * (woundedFactor + pressureFactor * 0.5f);
    }

    /// <summary>
    /// Scores the Reposition action.
    ///
    /// Reposition utility rises as:
    ///   - Distance is in the mid-range "dead zone" (too far to attack,
    ///     too close to retreat cleanly).
    ///   - Health is moderate (neither bold enough to attack nor desperate
    ///     enough to flee — tactically repositioning is the rational choice).
    ///
    /// Formula:
    ///   rangeFactor     = how far distance is from the optimal engagement band
    ///   modestFactor    = 1 - |healthFrac - 0.5| * 2
    ///       → peaks at 0.5 HP (half health), falls to 0 at full or zero HP.
    ///   score           = weight * rangeFactor * modestFactor
    /// </summary>
    private float ScoreReposition(float distance, float healthFrac)
    {
        // Prefer repositioning when in the band between melee and engagement range.
        float midpoint  = (meleeRange + engagementRange) * 0.5f;
        float bandwidth = (engagementRange - meleeRange) * 0.5f;
        // Gaussian-style falloff: peaks when distance == midpoint.
        float rangeFactor = Mathf.Clamp01(1f - Mathf.Abs(distance - midpoint) / bandwidth);

        // Tactical modesty: peaks at ~50% HP where neither fight nor flight wins.
        float modestFactor = 1f - Mathf.Abs(healthFrac - 0.5f) * 2f;
        modestFactor = Mathf.Clamp01(modestFactor);

        return repositionWeight * rangeFactor * modestFactor;
    }

    // ──────────────────────────────────────────────
    //  Action Selection
    // ──────────────────────────────────────────────

    /// <summary>
    /// Returns the DuelistAction with the highest utility score.
    /// Ties are broken in the order: Attack > Retreat > Reposition.
    /// </summary>
    private static DuelistAction SelectBestAction(
        float attackScore, float retreatScore, float repositionScore)
    {
        if (attackScore >= retreatScore && attackScore >= repositionScore)
            return DuelistAction.Attack;

        if (retreatScore >= repositionScore)
            return DuelistAction.Retreat;

        return DuelistAction.Reposition;
    }

    // ──────────────────────────────────────────────
    //  Execution Methods
    // ──────────────────────────────────────────────

    /// <summary>
    /// SEARCH: Move toward the Last Known Position of the player.
    /// Resets the reposition target flag so a fresh one is picked if we
    /// transition into Reposition next.
    /// </summary>
    private void ExecuteSearch()
    {
        hasRepositionTarget = false;
        ResetSpeed();
        MoveToTarget(LastKnownPosition);
    }

    /// <summary>
    /// ATTACK: Close in on the player, then, when in range and off
    /// cooldown, fire a Physics.OverlapSphere to deal damage via IDamageable.
    ///
    /// Two-phase execution:
    ///   Phase 1 — Advance: pathfind directly toward the player.
    ///   Phase 2 — Strike:  when inside attackRadius and cooldown expired,
    ///             perform an OverlapSphere and call TakeDamage on each
    ///             IDamageable hit.
    ///
    /// The physics overlap is a "dummy" approximation — it represents the
    /// Duelist's physical strike volume, not a hitscan ray. A real implementation
    /// would tie this to an animation event.
    /// </summary>
    private void ExecuteAttack()
    {
        hasRepositionTarget = false;

        // Guard: player may have been destroyed mid-frame (checklist item 5).
        if (Player == null) return;

        // Always close in on the player so we stay in melee range.
        ResetSpeed();
        MoveToTarget(Player.position);

        // Only swing if within attack radius and the cooldown has elapsed.
        float distance = GetDistanceToPlayer();
        if (distance > attackRadius || attackTimer > 0f) return;

        // ── ATTACK OVERLAP (dummy physics) ──────────────────────────────────
        // Centre the sphere on the Duelist's position (slightly forward to
        // model a forward arm-swing). attackTargetMask limits hits to the
        // Player layer so we don't accidentally damage other enemies.
        Vector3 attackOrigin = transform.position + transform.forward * (attackRadius * 0.5f);

        Collider[] hits = Physics.OverlapSphere(
            attackOrigin,
            attackRadius,
            attackTargetMask,
            QueryTriggerInteraction.Ignore);

        bool hitSomething = false;
        foreach (Collider hit in hits)
        {
            // Guard: a collider's GameObject may be destroyed between OverlapSphere
            // and this iteration — TryGetComponent handles that safely.
            if (hit == null) continue;

            // Resolve damage through the IDamageable interface — never reference
            // Health directly (Aisaiah's Golden Rule).
            if (hit.TryGetComponent(out IDamageable target))
            {
                target.TakeDamage(attackDamage);
                hitSomething = true;

                Debug.Log($"[DuelistBrain] '{gameObject.name}' struck '{hit.gameObject.name}' " +
                          $"for {attackDamage} damage.", this);
            }
        }

        // Reset cooldown regardless of whether we hit anything — the swing has
        // already happened (prevents rapid-fire swings in empty space).
        attackTimer = attackCooldown;

        if (!hitSomething)
        {
            Debug.Log($"[DuelistBrain] '{gameObject.name}' attacked but missed " +
                      $"(OverlapSphere at {attackOrigin}, radius={attackRadius}).", this);
        }
    }

    /// <summary>
    /// RETREAT: Run directly away from the player's current position.
    ///
    /// The retreat vector is computed as the direction FROM the player TO the
    /// Duelist, projected forward by retreatDistance. This gives a destination
    /// behind the Duelist relative to the threat.
    ///
    /// Speed is boosted by retreatSpeedMultiplier for a sprint effect. Speed is
    /// reset when the action changes (handled via lastAction comparison in OnThink).
    /// </summary>
    /// <param name="forced">If true, this was triggered by the subsumption override
    /// (critical HP) rather than normal utility scoring.</param>
    private void ExecuteRetreat(bool forced)
    {
        hasRepositionTarget = false;

        // ── Flee direction ────────────────────────────────────────────────────
        // Explicitly computed as the world vector FROM the player TO the Duelist
        // (i.e. transform.position - Player.position), so the destination is
        // always behind the Duelist relative to the threat.
        //
        // We do NOT use GetDirectionToPlayer() here because that returns the
        // direction *toward* the player — negating a helper that already does
        // vector math is confusing and was the root cause of the retreat bug.
        //
        // Null guard: if the player was destroyed mid-frame, fall back to the
        // Duelist's own backward direction so the agent still moves somewhere.
        Vector3 fleeDir;
        if (Player != null)
        {
            fleeDir = (transform.position - Player.position).normalized;
        }
        else
        {
            fleeDir = -transform.forward;   // Sensible fallback — back away from facing direction.
        }

        // ── Find the furthest reachable retreat point ─────────────────────────
        // Strategy: try the full retreatDistance first. If that point is off the
        // NavMesh, step the candidate progressively closer to the Duelist (where
        // the surface is guaranteed valid). The agent retreats as far as the map
        // geometry allows rather than freezing in place.
        //
        // Each iteration shrinks the target distance by one step-width and checks
        // whether SamplePosition can snap the candidate to the walkable surface.
        // The snap radius equals one step-width so it's large enough to catch the
        // walkable edge but small enough not to skip to a distant region.
        //
        // Default values (retreatDistance=15, retreatSteps=5) try distances:
        //   15 m → 12 m → 9 m → 6 m → 3 m → 0 m (Duelist's own position).
        const int retreatSteps = 5;
        float stepSize    = retreatDistance / retreatSteps;
        float snapRadius  = stepSize;   // Per-candidate NavMesh snap radius.

        Vector3 retreatPos   = transform.position;  // Worst-case: hold position.
        bool    foundRetreat = false;

        for (int step = 0; step <= retreatSteps; step++)
        {
            float   tryDist   = retreatDistance - step * stepSize;
            Vector3 candidate = transform.position + fleeDir * tryDist;

            if (UnityEngine.AI.NavMesh.SamplePosition(
                    candidate,
                    out UnityEngine.AI.NavMeshHit navHit,
                    snapRadius,
                    UnityEngine.AI.NavMesh.AllAreas))
            {
                retreatPos   = navHit.position;
                foundRetreat = true;

                // Only log when we had to reduce the distance — keeps the console
                // clean during normal play while still surfacing edge cases.
                if (step > 0)
                {
                    Debug.Log($"[DuelistBrain] '{gameObject.name}' retreat clamped to " +
                              $"{tryDist:F1} m (full {retreatDistance:F1} m was off NavMesh).", this);
                }
                break;
            }
        }

        if (!foundRetreat)
        {
            // All steps failed — surrounded by non-walkable geometry.
            // MoveToTarget(transform.position) is a safe no-op for the NavMeshAgent.
            Debug.LogWarning($"[DuelistBrain] '{gameObject.name}' retreat: no walkable " +
                             "point found at any fallback distance. Agent holding position.", this);
        }

        // ── Execute ───────────────────────────────────────────────────────────
        SetSpeed(GetBaseSpeed() * retreatSpeedMultiplier);
        MoveToTarget(retreatPos);

        if (forced)
        {
            Debug.Log($"[DuelistBrain] '{gameObject.name}' FORCED RETREAT (critical HP: " +
                      $"{CurrentHealth}/{MaxHealth}).", this);
        }
    }

    /// <summary>
    /// REPOSITION: Pick a random nearby NavMesh position to strafe to, giving
    /// the Duelist a flanking or angle-change behaviour.
    ///
    /// Samples <repositionSamples> random directions in a circle around the
    /// Duelist and selects the candidate closest to the optimal engagement range
    /// from the player. Uses NavMesh.SamplePosition to ensure the target is
    /// reachable.
    ///
    /// A new target is only picked when:
    ///   - No current reposition target exists, OR
    ///   - The Duelist has arrived at its current target.
    /// </summary>
    private void ExecuteReposition()
    {
        ResetSpeed();

        // Pick a new target only when needed.
        if (!hasRepositionTarget || HasReachedDestination())
        {
            hasRepositionTarget = TryPickRepositionTarget(out repositionTarget);
        }

        if (hasRepositionTarget)
        {
            MoveToTarget(repositionTarget);
        }
        else if (Player != null)
        {
            // Fallback: couldn't find a valid NavMesh point — approach the player.
            // Guard: Player may be destroyed mid-frame (checklist item 5).
            MoveToTarget(Player.position);
        }
    }

    /// <summary>
    /// Samples <repositionSamples> candidate positions in a ring around
    /// the Duelist, then picks the one closest to the ideal engagement band
    /// (halfway between meleeRange and engagementRange). Uses NavMesh.SamplePosition
    /// so we only target reachable ground.
    /// </summary>
    /// <param name="chosen">The selected world-space position, if found.</param>
    /// <returns>True if a valid reposition target was found on the NavMesh.</returns>
    private bool TryPickRepositionTarget(out Vector3 chosen)
    {
        float idealDist = (meleeRange + engagementRange) * 0.5f;
        float bestScore = float.MaxValue;
        bool  found     = false;
        chosen = transform.position;

        for (int i = 0; i < repositionSamples; i++)
        {
            // Sample a direction uniformly around a full circle.
            float   angle     = (360f / repositionSamples) * i + Random.Range(-15f, 15f);
            Vector3 dir       = Quaternion.Euler(0f, angle, 0f) * transform.forward;
            Vector3 candidate = transform.position + dir * repositionRadius;

            // Snap the candidate to the nearest NavMesh point (max 2 unit offset).
            if (!UnityEngine.AI.NavMesh.SamplePosition(candidate, out UnityEngine.AI.NavMeshHit navHit, 2f,
                    UnityEngine.AI.NavMesh.AllAreas))
            {
                continue;
            }

            // Score by how close the candidate is to the ideal engagement distance
            // from the player (lower = better for our "distance-to-ideal" metric).
            // Guard: Player may be destroyed mid-frame; skip scoring against it.
            if (Player == null) break;

            float distFromPlayer = Vector3.Distance(navHit.position, Player.position);
            float score          = Mathf.Abs(distFromPlayer - idealDist);

            if (score < bestScore)
            {
                bestScore = score;
                chosen    = navHit.position;
                found     = true;
            }
        }

        return found;
    }

    // ──────────────────────────────────────────────
    //  Helpers
    // ──────────────────────────────────────────────

    /// <summary>
    /// Returns the NavMeshAgent's current speed so retreat can multiply it.
    /// We read Velocity.magnitude instead of the agent's speed property
    /// (which is private) to get a frame-accurate value. Falls back to a
    /// reasonable constant if the agent is not moving.
    ///
    /// NOTE: EnemyBase exposes Velocity as a read-only accessor.
    /// NavMeshAgent.speed is private in EnemyBase so we can't read it directly;
    /// however, ResetSpeed() and SetSpeed() give us full control.
    /// The 3.5f fallback matches EnemyBase's default inspector value.
    /// </summary>
    private float GetBaseSpeed()
    {
        float speed = Velocity.magnitude;
        return speed > 0.01f ? speed : 3.5f;
    }

    // ──────────────────────────────────────────────
    //  Death & Cleanup Hooks
    // ──────────────────────────────────────────────

    /// <summary>
    /// Called once when this Duelist is killed, after EnemyBase has already:
    ///   - Set IsDead = true.
    ///   - Disabled the NavMeshAgent.
    /// Use for Duelist-specific cleanup only.
    /// </summary>
    protected override void OnEnemyDeath()
    {
        // Reset the attack timer so any pooled re-use starts clean.
        attackTimer = 0f;
        hasRepositionTarget = false;

        Debug.Log($"[DuelistBrain] '{gameObject.name}' has been eliminated. " +
                  $"Final action was: {lastAction}.", this);
    }

    /// <summary>
    /// Called from OnDestroy() after EnemyBase has unsubscribed from Health events.
    /// Add any final teardown here (e.g., unsubscribe from external managers).
    /// </summary>
    protected override void OnCleanup()
    {
        // No external subscriptions to clean up in this implementation.
        // Add unsubscriptions here if DuelistBrain ever registers with a
        // wave manager, objective tracker, etc.
    }

#if UNITY_EDITOR
    // ──────────────────────────────────────────────
    //  Editor Gizmos (Scene View Debugging)
    // ──────────────────────────────────────────────

    /// <summary>
    /// Draws debug geometry in the Scene View to assist with tuning utility ranges.
    /// Only compiled into the Editor — zero runtime overhead in builds.
    ///
    /// Visualises:
    ///   Red sphere    — attackRadius (the OverlapSphere for melee).
    ///   Yellow wire   — meleeRange (distance at which Attack scores peak).
    ///   Cyan wire     — engagementRange (mid-range boundary).
    ///   Magenta line  — current reposition target (if one is set).
    /// </summary>
    private void OnDrawGizmosSelected()
    {
        // Attack overlap sphere — solid red at the forward strike origin.
        Gizmos.color = new Color(1f, 0.2f, 0.2f, 0.4f);
        Vector3 attackOrigin = transform.position + transform.forward * (attackRadius * 0.5f);
        Gizmos.DrawSphere(attackOrigin, attackRadius);

        // Melee range wire — yellow halo.
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, meleeRange);

        // Engagement range wire — cyan halo.
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, engagementRange);

        // Reposition target — magenta line.
        if (hasRepositionTarget)
        {
            Gizmos.color = Color.magenta;
            Gizmos.DrawLine(transform.position, repositionTarget);
            Gizmos.DrawSphere(repositionTarget, 0.3f);
        }
    }
#endif
}
