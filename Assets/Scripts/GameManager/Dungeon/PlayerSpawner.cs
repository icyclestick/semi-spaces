using UnityEngine;

/// <summary>
/// Triggers dungeon generation and places the player at the center of
/// the root room.
///
/// Two modes (checked in order):
///   A) If Player Prefab is assigned → spawns a new player from the prefab.
///   B) Otherwise → finds an existing player in the scene (by tag "Player"
///      or by finding a FirstPersonController) and teleports them.
///
/// Roguelikes use the teleport approach — the player already exists in the
/// scene with all their components configured, and the spawner just moves
/// them to the right spot.
///
/// Setup:
///   1. Add this script to your LevelManager GameObject.
///   2. Drag the DungeonBuilder into the Dungeon Builder field.
///   3. (Option A) Drag a player prefab into Player Prefab, OR
///      (Option B) Leave it empty and tag your scene player "Player".
///   4. Click "Generate Level & Spawn Player".
/// </summary>
public class PlayerSpawner : MonoBehaviour
{
    [Header("References")]
    [SerializeField, Tooltip("The DungeonBuilder that handles generation.")]
    private DungeonBuilder dungeonBuilder;

    [SerializeField, Tooltip("Optional: EnemySpawner to populate rooms after generation.")]
    private EnemySpawner enemySpawner;

    [SerializeField, Tooltip("Optional: a player prefab to spawn. If left empty, the " +
        "spawner finds an existing player in the scene and teleports them instead.")]
    private GameObject playerPrefab;

    [Header("Spawn Settings")]
    [SerializeField, Tooltip("Height above the floor to place the player.")]
    private float spawnHeight = 3f;

    // ──────────────────────────────────────────────
    //  State
    // ──────────────────────────────────────────────

    private GameObject spawnedPlayer;

    // ──────────────────────────────────────────────
    //  Public API
    // ──────────────────────────────────────────────

    [ContextMenu("Generate Full Level")]
    public void GenerateAndPlace()
    {
        if (dungeonBuilder == null)
        {
            Debug.LogError("[PlayerSpawner] Dungeon Builder is not assigned.", this);
            return;
        }

        // --- Generate the dungeon ---
        dungeonBuilder.Generate();

        // --- Compute spawn position ---
        Vector3 spawnPos = dungeonBuilder.RootCenter + Vector3.up * spawnHeight;

        // --- Get or create the player ---
        GameObject player = GetOrCreatePlayer();
        if (player == null)
        {
            Debug.LogError("[PlayerSpawner] No player found and no prefab assigned. " +
                           "Either tag your scene player 'Player' or assign a Player Prefab.", this);
            return;
        }

        // --- Teleport to spawn ---
        // Disable character controller briefly so the teleport doesn't fight physics.
        CharacterController cc = player.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;

        player.transform.position = spawnPos;
        player.transform.rotation = Quaternion.identity;

        if (cc != null) cc.enabled = true;

        Debug.Log($"[PlayerSpawner] Player placed at {spawnPos}.", this);

        // --- Spawn enemies (if EnemySpawner is assigned) ---
        if (enemySpawner != null)
        {
            enemySpawner.SpawnEnemies();
        }
        else
        {
            Debug.Log("[PlayerSpawner] No EnemySpawner assigned — skipping enemy spawn.", this);
        }
    }

    // ──────────────────────────────────────────────
    //  Helpers
    // ──────────────────────────────────────────────

    private GameObject GetOrCreatePlayer()
    {
        // --- Always prefer the existing scene player ---
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player == null)
        {
            FirstPersonController fpc = FindObjectOfType<FirstPersonController>();
            if (fpc != null) player = fpc.gameObject;
        }

        if (player != null)
        {
            // Destroy any previously-spawned player since we're using the scene one.
            if (spawnedPlayer != null)
            {
                DestroyImmediate(spawnedPlayer);
                spawnedPlayer = null;
            }
            return player;
        }

        // --- Fallback: spawn from prefab if no scene player exists ---
        if (playerPrefab != null)
        {
            if (spawnedPlayer != null)
                DestroyImmediate(spawnedPlayer);

            spawnedPlayer = Instantiate(playerPrefab);
            spawnedPlayer.name = playerPrefab.name;
            return spawnedPlayer;
        }

        return null;
    }
}
