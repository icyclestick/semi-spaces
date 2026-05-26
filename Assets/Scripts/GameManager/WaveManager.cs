using System.Collections;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Manages the wave-based spawn/kill lifecycle for Semi-Spaces.
/// Spawns enemies in structured waves, tracks active enemy counts,
/// and broadcasts events for wave progression and win state.
///
/// This script is the "World State Layer" from the architecture proposal.
/// It manages global game state — what wave we're on, how many enemies
/// are alive, and when the game ends.
///
/// Setup:
///   1. Create an empty "WaveManager" GameObject in the scene.
///   2. Attach this script.
///   3. Assign the Swarm and Duelist prefabs in the Inspector.
///   4. Create empty GameObjects at spawn locations, tag them, and
///      drag them into the Spawn Points array.
///   5. Configure each Wave entry (enemy counts + spawn delay).
///   6. Wire the UnityEvents to your HUD / level scripts.
///
/// Death Tracking:
///   The WaveManager subscribes to each spawned enemy's Health.OnDeath
///   event automatically. Enemies do NOT need to know about WaveManager.
///   When an enemy dies, the subscription fires and decrements the count.
///
/// Decoupling:
///   This script contains ZERO UI code. It broadcasts UnityEvents that
///   a separate HUD script can subscribe to in the Inspector.
/// </summary>
public class WaveManager : MonoBehaviour
{
    // ──────────────────────────────────────────────
    //  Wave Data Structure
    // ──────────────────────────────────────────────

    /// <summary>
    /// Defines the composition of a single wave. Each entry in the
    /// waves array represents one wave the player must survive.
    /// Configured entirely in the Inspector.
    /// </summary>
    [System.Serializable]
    public class Wave
    {
        [Tooltip("Number of Swarm drones to spawn in this wave.")]
        public int swarmDroneCount;

        [Tooltip("Number of Duelist anomalies to spawn in this wave.")]
        public int duelistAnomalyCount;

        [Tooltip("Delay in seconds between each individual enemy spawn.")]
        public float spawnDelay = 0.5f;
    }

    // ──────────────────────────────────────────────
    //  Inspector Configuration
    // ──────────────────────────────────────────────

    [Header("Wave Configuration")]
    [SerializeField, Tooltip("The sequence of waves the player must survive. " +
        "Configure enemy counts and spawn timing per wave.")]
    private Wave[] waves;

    [Header("Spawn Points")]
    [SerializeField, Tooltip("Array of Transform positions where enemies can spawn. " +
        "Enemies are assigned to random points from this list.")]
    private Transform[] spawnPoints;

    [Header("Enemy Prefabs")]
    [SerializeField, Tooltip("The Swarm drone prefab. Must have Health + an EnemyBase subclass.")]
    private GameObject swarmPrefab;

    [SerializeField, Tooltip("The Duelist anomaly prefab. Must have Health + an EnemyBase subclass.")]
    private GameObject duelistPrefab;

    [Header("Timing")]
    [SerializeField, Tooltip("Delay in seconds before the first wave starts.")]
    private float initialDelay = 3f;

    [SerializeField, Tooltip("Delay in seconds between waves (after clearing one, before the next starts).")]
    private float timeBetweenWaves = 5f;

    // ──────────────────────────────────────────────
    //  Events (UnityEvent — for HUD / Level wiring)
    // ──────────────────────────────────────────────
    //
    //  These events let Jyesh build a HUD and level flow without
    //  touching this script. Wire them in the Inspector.
    //

    [Header("Events")]
    [SerializeField, Tooltip("Fired when a new wave begins. Passes the wave number (1-indexed for display).")]
    private UnityEvent<int> onWaveStarted;

    [SerializeField, Tooltip("Fired when all waves are cleared. The player wins.")]
    private UnityEvent onGameWon;

    [SerializeField, Tooltip("Fired when an enemy is killed. Passes (remainingEnemies, totalInWave).")]
    private UnityEvent<int, int> onEnemyKilled;

    [SerializeField, Tooltip("Fired when a wave is fully cleared.")]
    private UnityEvent onWaveCleared;

    // ──────────────────────────────────────────────
    //  State Tracking
    // ──────────────────────────────────────────────

    /// <summary>Index of the current wave (0-based).</summary>
    private int currentWaveIndex;

    /// <summary>Number of enemies still alive in the current wave.</summary>
    private int activeEnemiesCount;

    /// <summary>Total enemies spawned in the current wave (for event data).</summary>
    private int totalEnemiesInWave;

    /// <summary>True once all waves have been cleared.</summary>
    private bool gameWon;


    /// <summary>Reference to the active spawn coroutine.</summary>
    private Coroutine spawnCoroutine;

    // ──────────────────────────────────────────────
    //  Public Accessors
    // ──────────────────────────────────────────────

    /// <summary>Current wave number (1-indexed for display).</summary>
    public int CurrentWave => currentWaveIndex + 1;

    /// <summary>Total number of waves configured.</summary>
    public int TotalWaves => waves != null ? waves.Length : 0;

    /// <summary>Number of enemies currently alive.</summary>
    public int ActiveEnemies => activeEnemiesCount;

    /// <summary>True if the player has won (all waves cleared).</summary>
    public bool IsGameWon => gameWon;

    // ──────────────────────────────────────────────
    //  Lifecycle
    // ──────────────────────────────────────────────

    private void Start()
    {
        // --- Validate configuration ---
        if (waves == null || waves.Length == 0)
        {
            Debug.LogError("[WaveManager] No waves configured. " +
                           "Add at least one Wave entry in the Inspector.", this);
            return;
        }

        if (spawnPoints == null || spawnPoints.Length == 0)
        {
            Debug.LogError("[WaveManager] No spawn points assigned. " +
                           "Drag Transform references into the Spawn Points array.", this);
            return;
        }

        if (swarmPrefab == null && duelistPrefab == null)
        {
            Debug.LogError("[WaveManager] No enemy prefabs assigned. " +
                           "Assign at least one prefab.", this);
            return;
        }

        // --- Begin the game loop ---
        currentWaveIndex = 0;
        spawnCoroutine = StartCoroutine(GameLoopCoroutine());
    }

    // ──────────────────────────────────────────────
    //  Game Loop
    // ──────────────────────────────────────────────

    /// <summary>
    /// Master coroutine that drives the entire wave lifecycle:
    ///   1. Wait for initial delay.
    ///   2. Start wave → spawn enemies → wait for all killed.
    ///   3. Inter-wave pause → next wave.
    ///   4. After final wave cleared → trigger win state.
    ///
    /// The "wait for all killed" step doesn't loop or poll —
    /// it just yields while activeEnemiesCount > 0.
    /// OnEnemyDied() handles the decrement externally.
    /// </summary>
    private IEnumerator GameLoopCoroutine()
    {
        // Pre-game countdown (gives the player a moment to orient).
        yield return new WaitForSeconds(initialDelay);

        // --- Wave loop ---
        while (currentWaveIndex < waves.Length)
        {
            // Spawn the current wave.
            yield return StartCoroutine(SpawnWave(waves[currentWaveIndex]));

            // Wait until every enemy in this wave is dead.
            // No polling — this yield resumes each frame and checks
            // the count that OnEnemyDied() is decrementing.
            yield return new WaitUntil(() => activeEnemiesCount <= 0);

            // Wave cleared.
            Debug.Log($"[WaveManager] Wave {currentWaveIndex + 1}/{waves.Length} cleared!", this);
            onWaveCleared?.Invoke();

            // Advance to next wave.
            currentWaveIndex++;

            // If there are more waves, pause before starting the next one.
            if (currentWaveIndex < waves.Length)
            {
                yield return new WaitForSeconds(timeBetweenWaves);
            }
        }

        // --- All waves cleared: WIN ---
        gameWon = true;
        Debug.Log("[WaveManager] All waves cleared. Player wins!", this);
        onGameWon?.Invoke();
    }

    // ──────────────────────────────────────────────
    //  Wave Spawning
    // ──────────────────────────────────────────────

    /// <summary>
    /// Spawns all enemies for a single wave. Instantiates Swarm drones
    /// first, then Duelist anomalies, with a configurable delay between
    /// each spawn. Subscribes to each enemy's Health.OnDeath event so
    /// we're notified when they die.
    /// </summary>
    /// <param name="wave">The Wave data to spawn.</param>
    private IEnumerator SpawnWave(Wave wave)
    {

        // Calculate total enemies for this wave.
        totalEnemiesInWave = wave.swarmDroneCount + wave.duelistAnomalyCount;
        activeEnemiesCount = 0;

        Debug.Log($"[WaveManager] Starting Wave {currentWaveIndex + 1}/{waves.Length}: " +
                  $"{wave.swarmDroneCount} Swarm + {wave.duelistAnomalyCount} Duelist " +
                  $"= {totalEnemiesInWave} total.", this);

        // Notify HUD — "WAVE 3" splash screen, etc.
        onWaveStarted?.Invoke(currentWaveIndex + 1);

        // --- Spawn Swarm drones ---
        for (int i = 0; i < wave.swarmDroneCount; i++)
        {
            if (swarmPrefab != null)
            {
                SpawnEnemy(swarmPrefab);
            }
            else
            {
                Debug.LogWarning("[WaveManager] Swarm prefab is null, skipping spawn.", this);
            }

            // Stagger spawns so they don't all appear on the same frame.
            if (wave.spawnDelay > 0f)
            {
                yield return new WaitForSeconds(wave.spawnDelay);
            }
        }

        // --- Spawn Duelist anomalies ---
        for (int i = 0; i < wave.duelistAnomalyCount; i++)
        {
            if (duelistPrefab != null)
            {
                SpawnEnemy(duelistPrefab);
            }
            else
            {
                Debug.LogWarning("[WaveManager] Duelist prefab is null, skipping spawn.", this);
            }

            if (wave.spawnDelay > 0f)
            {
                yield return new WaitForSeconds(wave.spawnDelay);
            }
        }

    }

    /// <summary>
    /// Instantiates a single enemy at a random spawn point and wires
    /// up the death subscription. This is the ONLY place Instantiate
    /// is called for enemies.
    /// </summary>
    /// <param name="prefab">The enemy prefab to spawn.</param>
    private void SpawnEnemy(GameObject prefab)
    {
        // Pick a random spawn point.
        Transform point = spawnPoints[Random.Range(0, spawnPoints.Length)];

        // Instantiate at the spawn point's position and rotation.
        GameObject enemy = Instantiate(prefab, point.position, point.rotation);

        // --- Subscribe to death ---
        // We listen to the Health component's OnDeath event so the
        // enemy doesn't need to know about WaveManager at all.
        // This is fully decoupled — the enemy just dies, and we hear it.
        Health health = enemy.GetComponent<Health>();
        if (health != null)
        {
            // Self-unsubscribing closure: the handler removes itself
            // from OnDeath when it fires, so the dead enemy's Health
            // component doesn't retain a reference to WaveManager.
            System.Action handler = null;
            handler = () =>
            {
                health.OnDeath -= handler;
                OnEnemyDied();
            };
            health.OnDeath += handler;
        }
        else
        {
            Debug.LogWarning($"[WaveManager] Spawned '{prefab.name}' has no Health component. " +
                             "It won't be tracked for wave completion.", this);
        }

        activeEnemiesCount++;

        Debug.Log($"[WaveManager] Spawned '{enemy.name}' at '{point.name}'. " +
                  $"Active: {activeEnemiesCount}/{totalEnemiesInWave}", this);
    }

    // ──────────────────────────────────────────────
    //  Death Tracking
    // ──────────────────────────────────────────────

    /// <summary>
    /// Called when any tracked enemy dies (via Health.OnDeath subscription).
    /// Decrements the active count and checks if the wave is cleared.
    ///
    /// This method is public so it can also be called manually or wired
    /// via UnityEvent in the Inspector as a fallback.
    /// </summary>
    public void OnEnemyDied()
    {
        if (gameWon) return;

        activeEnemiesCount--;

        // Clamp to zero — safety net against double-fire edge cases.
        // A negative count means something fired OnDeath more than once
        // for the same enemy, which is a bug worth investigating.
        if (activeEnemiesCount < 0)
        {
            Debug.LogWarning($"[WaveManager] activeEnemiesCount went to {activeEnemiesCount}! " +
                             "A death event may have fired twice. Clamping to 0.", this);
            activeEnemiesCount = 0;
        }

        Debug.Log($"[WaveManager] Enemy eliminated. " +
                  $"Remaining: {activeEnemiesCount}/{totalEnemiesInWave}", this);

        // Notify HUD — update kill counter, remaining enemies display, etc.
        onEnemyKilled?.Invoke(activeEnemiesCount, totalEnemiesInWave);

        // Note: wave completion is handled by the WaitUntil in
        // GameLoopCoroutine. We don't need to explicitly trigger
        // the next wave here — the coroutine resumes automatically
        // when activeEnemiesCount hits zero.
    }
}
