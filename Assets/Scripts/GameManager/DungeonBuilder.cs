using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Orchestrates multi-room dungeon generation using a frontier-based
/// graph walk. Places a root room at origin, then iteratively extends
/// corridors from unconnected gates and attaches new rooms at the ends.
///
/// Depends on a DungeonGenerator component on the same GameObject (or
/// assigned via Inspector) for the actual piece-by-piece building.
///
/// Setup:
///   1. Add this script to the same GameObject that has DungeonGenerator.
///   2. Drag the DungeonGenerator into the Generator field (if not on
///      the same object, it uses GetComponent).
///   3. Configure the iteration / room / corridor settings.
///   4. Click "Generate Dungeon" in the context menu.
/// </summary>
public class DungeonBuilder : MonoBehaviour
{
    // ──────────────────────────────────────────────
    //  Inspector Configuration
    // ──────────────────────────────────────────────

    [Header("Generator")]
    [SerializeField, Tooltip("The GameObject that has the DungeonGenerator script. " +
        "Drag the GameObject here (not the script component).")]
    private GameObject generatorObject;

    // Cached reference resolved from generatorObject at generation time.
    private DungeonGenerator generator;

    [Header("Iterations")]
    [SerializeField, Tooltip("Minimum number of expansion cycles (inclusive).")]
    private int minIterations = 3;

    [SerializeField, Tooltip("Maximum number of expansion cycles (inclusive).")]
    private int maxIterations = 8;

    [Header("Rooms")]
    [SerializeField, Tooltip("Minimum width/height for generated rooms (odd, ≥ 3).")]
    private int minRoomSize = 5;

    [SerializeField, Tooltip("Maximum width/height for generated rooms (odd).")]
    private int maxRoomSize = 11;

    [Header("Corridors")]
    [SerializeField, Tooltip("Minimum corridor segment count between rooms.")]
    private int minCorridorLength = 4;

    [SerializeField, Tooltip("Maximum corridor segment count between rooms.")]
    private int maxCorridorLength = 8;

    [SerializeField, Range(0f, 1f), Tooltip("Probability that a gate spawns a corridor " +
        "instead of dead-ending. 0 = no corridors, 1 = every gate extends.")]
    private float corridorChance = 0.7f;

    [Header("Random Seed")]
    [SerializeField, Tooltip("When true, uses a different seed each time.")]
    private bool useRandomSeed = true;

    [SerializeField, Tooltip("Fixed seed for reproducible generation.")]
    private int seed;

    // ──────────────────────────────────────────────
    //  Runtime State
    // ──────────────────────────────────────────────

    private const string ParentName = "GeneratedDungeon";

    /// <summary>Grid cells occupied by rooms (tileSize granularity).</summary>
    private HashSet<Vector2Int> occupiedCells = new HashSet<Vector2Int>();

    /// <summary>Pending gates to process.</summary>
    private Queue<DungeonGenerator.GateInfo> frontier = new Queue<DungeonGenerator.GateInfo>();

    private int roomsPlaced;
    private int corridorsBuilt;

    // ──────────────────────────────────────────────
    //  Public API
    // ──────────────────────────────────────────────

    [ContextMenu("Generate Dungeon (Builder)")]
    public void Generate()
    {
        if (!ResolveGenerator()) return;

        // --- Seed (NOT restored — the whole generation needs it) ---
        if (useRandomSeed)
            Random.InitState(System.Environment.TickCount);
        else
            Random.InitState(seed);

        int targetIterations = Random.Range(minIterations, maxIterations + 1);

        // --- Tear down ---
        TearDown();

        // --- Fresh parent ---
        GameObject root = new GameObject(ParentName);
        root.transform.SetParent(transform, worldPositionStays: false);
        root.transform.localPosition = Vector3.zero;
        Transform parent = root.transform;

        // --- State ---
        occupiedCells.Clear();
        frontier.Clear();
        roomsPlaced = 0;
        corridorsBuilt = 0;

        // --- Place root room ---
        int rootN = RandomOddInRange(minRoomSize, maxRoomSize);
        int rootM = RandomOddInRange(minRoomSize, maxRoomSize);
        DungeonGenerator.GateInfo[] gates = generator.BuildRoom(rootN, rootM, Vector3.zero, parent);
        MarkOccupied(Vector3.zero, rootN, rootM);
        roomsPlaced++;

        foreach (DungeonGenerator.GateInfo gate in gates)
            frontier.Enqueue(gate);

        Debug.Log($"[DungeonBuilder] Starting expansion: target {targetIterations} iterations, " +
                  $"{frontier.Count} gates in frontier.", this);

        // --- Expansion loop ---
        int iterations = 0;
        while (frontier.Count > 0 && iterations < targetIterations)
        {
            DungeonGenerator.GateInfo gate = frontier.Dequeue();

            // Coin flip: dead-end or corridor?
            if (Random.value > corridorChance)
            {
                Debug.Log($"[DungeonBuilder] Gate ({gate.position.x:F0},{gate.position.z:F0}) " +
                          $"dir ({gate.direction.x:F0},{gate.direction.z:F0}) — dead end (missed coin flip).");
                continue;
            }

            int corridorLen = Random.Range(minCorridorLength, maxCorridorLength + 1);
            float ts = generator.TileSize;
            float gateOut = generator.GateOutwardOffset;

            // --- Pick new room size ---
            int newN = RandomOddInRange(minRoomSize, maxRoomSize);
            int newM = RandomOddInRange(minRoomSize, maxRoomSize);

            // --- Find a non-overlapping placement (extend corridor if needed) ---
            Vector3 finalOrigin = Vector3.zero;
            int finalLen = corridorLen;
            bool found = false;

            for (int extend = 0; extend <= 4; extend++)
            {
                int tryLen = corridorLen + extend;
                Vector3 tryEnd = gate.position + gate.direction * ((tryLen + 1) * ts);
                Vector3 tryOrigin = ComputeRoomOrigin(tryEnd, gate.direction, newN, newM, ts, gateOut);

                if (!Overlaps(tryOrigin, newN, newM, ts))
                {
                    finalOrigin = tryOrigin;
                    finalLen = tryLen;
                    found = true;
                    if (extend > 0)
                        Debug.Log($"[DungeonBuilder] Extended corridor by {extend} segment(s) to avoid overlap.");
                    break;
                }
            }

            if (!found)
            {
                Debug.Log($"[DungeonBuilder] Gate ({gate.position.x:F0},{gate.position.z:F0}) " +
                          $"— could not place room even after extending. Dead-ending.");
                iterations++;
                continue;
            }

            // --- Build the corridor ---
            generator.BuildCorridor(gate.position, gate.direction, finalLen, parent);
            corridorsBuilt++;

            Debug.Log($"[DungeonBuilder] Corridor from ({gate.position.x:F0},{gate.position.z:F0}) " +
                      $"dir ({gate.direction.x:F0},{gate.direction.z:F0}), length {finalLen}.");

            // --- Place the new room (gate faces the corridor, room extends away) ---
            DungeonGenerator.GateInfo[] newGates = generator.BuildRoom(newN, newM, finalOrigin, parent);
            MarkOccupied(finalOrigin, newN, newM);
            roomsPlaced++;

            Debug.Log($"[DungeonBuilder] Placed {newN}×{newM} room at ({finalOrigin.x:F0},{finalOrigin.z:F0}).");

            // --- Enqueue new gates (skip the one facing back toward the corridor) ---
            Vector3 backDir = -gate.direction;
            foreach (DungeonGenerator.GateInfo g in newGates)
            {
                if (g.direction != backDir)
                    frontier.Enqueue(g);
            }

            iterations++;
        }

        Debug.Log($"[DungeonBuilder] Done: {roomsPlaced} rooms, {corridorsBuilt} corridors, " +
                  $"{iterations} iterations.", this);
    }

    // ──────────────────────────────────────────────
    //  Setup / Teardown
    // ──────────────────────────────────────────────

    private bool ResolveGenerator()
    {
        // Resolve from the assigned GameObject first, fall back to same GameObject.
        if (generatorObject != null)
            generator = generatorObject.GetComponent<DungeonGenerator>();
        else
            generator = GetComponent<DungeonGenerator>();

        if (generator == null)
        {
            Debug.LogError("[DungeonBuilder] No DungeonGenerator found. " +
                           "Either drag the GameObject into Generator Object, " +
                           "or put both scripts on the same GameObject.", this);
            return false;
        }
        return true;
    }

    private void TearDown()
    {
        Transform existing = transform.Find(ParentName);
        if (existing != null)
            DestroyImmediate(existing.gameObject);
        occupiedCells.Clear();
        frontier.Clear();
    }

    // ──────────────────────────────────────────────
    //  Room Origin Math
    // ──────────────────────────────────────────────

    /// <summary>
    /// Given a corridor end-point and the direction it was travelling,
    /// compute the origin for a new n×m room such that its gate (on the
    /// side facing the corridor) exactly aligns with the corridor end.
    /// </summary>
    /// <summary>
    /// Computes the origin for a new room so that the gate on the side
    /// FACING the incoming corridor aligns with corridorEnd. The room
    /// extends AWAY from the corridor, not back into it.
    ///
    /// direction = which way the corridor is travelling (same as the
    /// parent gate's outward direction).
    /// </summary>
    private Vector3 ComputeRoomOrigin(Vector3 corridorEnd, Vector3 direction,
                                      int n, int m, float ts, float gateOut)
    {
        int midX = n / 2;
        int midZ = m / 2;
        float maxX = (n - 1) * ts;
        float maxZ = (m - 1) * ts;

        if (direction.z < 0)      // corridor goes SOUTH → hit room's NORTH gate
            return new Vector3(corridorEnd.x - midX * ts, 0, corridorEnd.z - maxZ - gateOut);
        else if (direction.z > 0) // corridor goes NORTH → hit room's SOUTH gate
            return new Vector3(corridorEnd.x - midX * ts, 0, corridorEnd.z + gateOut);
        else if (direction.x < 0) // corridor goes WEST  → hit room's EAST gate
            return new Vector3(corridorEnd.x - maxX - gateOut, 0, corridorEnd.z - midZ * ts);
        else                      // corridor goes EAST  → hit room's WEST gate
            return new Vector3(corridorEnd.x + gateOut, 0, corridorEnd.z - midZ * ts);
    }

    // ──────────────────────────────────────────────
    //  Occupancy / Overlap
    // ──────────────────────────────────────────────

    /// <summary>
    /// Marks all grid cells covered by a room (including a 1-cell wall
    /// margin) as occupied so future rooms don't overlap.
    /// </summary>
    private void MarkOccupied(Vector3 origin, int n, int m)
    {
        float ts = generator.TileSize;
        CellRange(origin, n, m, ts, out int xMin, out int xMax, out int zMin, out int zMax);

        for (int x = xMin; x <= xMax; x++)
            for (int z = zMin; z <= zMax; z++)
                occupiedCells.Add(new Vector2Int(x, z));
    }

    /// <summary>
    /// Returns true if any cell in the new room's footprint (including
    /// wall margin) is already occupied.
    /// </summary>
    private bool Overlaps(Vector3 origin, int n, int m, float ts)
    {
        CellRange(origin, n, m, ts, out int xMin, out int xMax, out int zMin, out int zMax);

        for (int x = xMin; x <= xMax; x++)
            for (int z = zMin; z <= zMax; z++)
                if (occupiedCells.Contains(new Vector2Int(x, z)))
                    return true;

        return false;
    }

    /// <summary>
    /// Computes the grid-cell extents of a room including a 1-cell
    /// wall margin on each side.
    /// </summary>
    private void CellRange(Vector3 origin, int n, int m, float ts,
                           out int xMin, out int xMax, out int zMin, out int zMax)
    {
        // Room interior grid cells: origin/ts to origin/ts + (n-1), (m-1).
        // Add 1 cell margin for walls.
        xMin = Mathf.FloorToInt(origin.x / ts) - 1;
        xMax = Mathf.CeilToInt((origin.x + (n - 1) * ts) / ts) + 1;
        zMin = Mathf.FloorToInt(origin.z / ts) - 1;
        zMax = Mathf.CeilToInt((origin.z + (m - 1) * ts) / ts) + 1;
    }

    // ──────────────────────────────────────────────
    //  Helpers
    // ──────────────────────────────────────────────

    private int RandomOddInRange(int min, int max)
    {
        if (min % 2 == 0) min++;
        if (max % 2 == 0) max--;
        int oddCount = (max - min) / 2 + 1;
        return min + Random.Range(0, oddCount) * 2;
    }
}
