using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Spawns enemies randomly inside each generated room after dungeon
/// generation completes. Each room picks ONE enemy type from the list;
/// each type has its own min/max count range.
///
/// Setup:
///   1. Add this script to the same GameObject as DungeonBuilder.
///   2. Drag the DungeonBuilder into the Dungeon Builder field.
///   3. Add enemy type entries in the Inspector — each needs a prefab
///      plus a min/max count.
///   4. Set Min Enemies Per Room / Max Enemies Per Room as a global cap.
///   5. Click "Spawn Enemies" after generation, or call SpawnEnemies()
///      from PlayerSpawner after GenerateAndPlace().
///
/// By default the root room (room 0) is skipped so the player doesn't
/// spawn inside a pack of enemies. Uncheck Skip Root Room to include it.
/// </summary>
public class EnemySpawner : MonoBehaviour
{
    [Header("References")]
    [SerializeField, Tooltip("The DungeonBuilder that holds room data after generation.")]
    private DungeonBuilder dungeonBuilder;

    [SerializeField, Tooltip("Parent transform for spawned enemies. If null, spawns at root.")]
    private Transform enemiesParent;

    [Header("Global Caps")]
    [SerializeField, Tooltip("Floor — per-room count won't drop below this. Set to 0 to use only per-type mins.")]
    private int minEnemiesPerRoom = 0;

    [SerializeField, Tooltip("Ceiling — per-room count won't exceed this. Set high (e.g. 100) to use only per-type maxes.")]
    private int maxEnemiesPerRoom = 100;

    [SerializeField, Tooltip("If true, the root room (room 0) gets no enemies.")]
    private bool skipRootRoom = true;

    [SerializeField, Tooltip("Vertical offset (Y) when spawning enemies. Increase until they " +
        "spawn fully above the floor and drop down cleanly.")]
    private float verticalSpawnOffset = 6f;

    [Header("Enemy Types")]
    [SerializeField, Tooltip("List of enemy types. Each room picks ONE type at random.")]
    private List<EnemyTypeEntry> enemyTypes = new List<EnemyTypeEntry>();

    // ──────────────────────────────────────────────
    //  Data
    // ──────────────────────────────────────────────

    [System.Serializable]
    public class EnemyTypeEntry
    {
        [Tooltip("The enemy prefab to spawn.")]
        public GameObject prefab;

        [Tooltip("Minimum count of this enemy type in a room.")]
        public int minCount = 1;

        [Tooltip("Maximum count of this enemy type in a room.")]
        public int maxCount = 3;
    }

    // ──────────────────────────────────────────────
    //  Public API
    // ──────────────────────────────────────────────

    [ContextMenu("Generate Dungeon & Spawn Enemies")]
    public void GenerateAndSpawn()
    {
        if (dungeonBuilder != null)
            dungeonBuilder.Generate();
        SpawnEnemies();
    }

    [ContextMenu("Spawn Enemies (after dungeon exists)")]
    public void SpawnEnemies()
    {
        if (dungeonBuilder == null)
        {
            Debug.LogError("[EnemySpawner] Dungeon Builder is not assigned.", this);
            return;
        }

        if (enemyTypes.Count == 0)
        {
            Debug.LogWarning("[EnemySpawner] No enemy types configured. Add entries in the Inspector.", this);
            return;
        }

        // --- Validate all prefabs ---
        for (int i = 0; i < enemyTypes.Count; i++)
        {
            if (enemyTypes[i].prefab == null)
            {
                Debug.LogError($"[EnemySpawner] Enemy type {i} has no prefab assigned.", this);
                return;
            }
        }

        // --- Clean up old enemies ---
        if (enemiesParent != null)
        {
            for (int i = enemiesParent.childCount - 1; i >= 0; i--)
                DestroyImmediate(enemiesParent.GetChild(i).gameObject);
        }

        // --- Spawn per room ---
        int totalSpawned = 0;
        var rooms = dungeonBuilder.PlacedRooms;

        if (rooms.Count == 0)
        {
            Debug.LogWarning("[EnemySpawner] No rooms found. Generate the dungeon first " +
                           "(use 'Generate Dungeon & Spawn Enemies' or run DungeonBuilder).", this);
            return;
        }

        for (int roomIdx = 0; roomIdx < rooms.Count; roomIdx++)
        {
            if (skipRootRoom && roomIdx == 0)
                continue;

            DungeonBuilder.RoomInfo room = rooms[roomIdx];
            totalSpawned += SpawnInRoom(room);
        }

        Debug.Log($"[EnemySpawner] Spawned {totalSpawned} enemies across {rooms.Count} rooms.", this);
    }

    // ──────────────────────────────────────────────
    //  Spawn Logic
    // ──────────────────────────────────────────────

    private int SpawnInRoom(DungeonBuilder.RoomInfo room)
    {
        // --- Pick one enemy type for this room ---
        EnemyTypeEntry type = enemyTypes[Random.Range(0, enemyTypes.Count)];

        // --- Determine count (clamped by global caps and type ranges) ---
        // Per-type counts, clamped by global caps.
        // Set global caps high (e.g. 100) if you want per-type to be the sole limit.
        int minCount = Mathf.Max(minEnemiesPerRoom, type.minCount);
        int maxCount = Mathf.Clamp(type.maxCount, minCount, maxEnemiesPerRoom);
        if (minCount > maxCount) minCount = maxCount;

        int count = Random.Range(minCount, maxCount + 1);
        Debug.Log($"[EnemySpawner] Room {room.origin}: type={type.prefab.name}, " +
                  $"min={minCount} max={maxCount} → picked {count} (global caps: {minEnemiesPerRoom}/{maxEnemiesPerRoom})");
        if (count <= 0) return 0;

        // --- Room bounds (world-space rectangle at y=0) ---
        float tileSize = dungeonBuilder.TileSize;
        float minX = room.origin.x;
        float maxX = room.origin.x + (room.width - 1) * tileSize;
        float minZ = room.origin.z;
        float maxZ = room.origin.z + (room.height - 1) * tileSize;

        // Shrink spawn area a bit so enemies don't spawn inside walls.
        float wallOut = tileSize * 0.5f;
        minX += wallOut;
        maxX -= wallOut;
        minZ += wallOut;
        maxZ -= wallOut;

        Transform parent = enemiesParent != null ? enemiesParent : transform;

        int spawned = 0;
        for (int i = 0; i < count; i++)
        {
            float x = Random.Range(minX, maxX);
            float z = Random.Range(minZ, maxZ);
            Vector3 pos = new Vector3(x, verticalSpawnOffset, z);

            GameObject enemy = Instantiate(type.prefab, pos, Quaternion.identity, parent);
            enemy.name = type.prefab.name;
            spawned++;
        }

        return spawned;
    }
}
