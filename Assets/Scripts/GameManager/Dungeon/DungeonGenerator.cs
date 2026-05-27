using UnityEngine;

/// <summary>
/// Builds a single rectangular dungeon room from the DungeonKit modular
/// pieces: corners at the four corners, walls along each edge, a gate at
/// the midpoint of each side, and corridors extending outward from gates.
///
/// Also serves as the building service for DungeonBuilder, which orchestrates
/// multi-room dungeons by calling BuildRoom / BuildCorridor with computed
/// origins.
///
/// Setup:
///   1. Add this script to a GameObject.
///   2. Drag template-corner, template-wall, gate, floor, corridor-wide,
///      and corridor-wide-end from the DungeonKit folder into the Prefab slots.
///   3. Set Tile Size to match the actual dimensions of the kit pieces.
///   4. Click "Generate Dungeon" in the context menu for a single-room test,
///      or use DungeonBuilder for multi-room generation.
/// </summary>
public class DungeonGenerator : MonoBehaviour
{
    // ──────────────────────────────────────────────
    //  Inspector Configuration
    // ──────────────────────────────────────────────

    [Header("Prefabs")]
    [SerializeField, Tooltip("The corner piece (template-corner.fbx).")]
    private GameObject cornerPrefab;

    [SerializeField, Tooltip("The straight wall piece (template-wall.fbx).")]
    private GameObject wallPrefab;

    [SerializeField, Tooltip("The gate piece to place at the midpoint of each side " +
        "(e.g. gate.fbx, gate-door.fbx, gate-door-window.fbx, gate-metal-bars.fbx).")]
    private GameObject gatePrefab;

    [SerializeField, Tooltip("The floor tile piece (template-floor.fbx).")]
    private GameObject floorPrefab;

    [Header("Corridor")]
    [SerializeField, Tooltip("The corridor segment piece (corridor-wide.fbx).")]
    private GameObject corridorPrefab;

    [SerializeField, Tooltip("The corridor end-cap piece (corridor-wide-end.fbx).")]
    private GameObject corridorEndPrefab;

    [SerializeField, Tooltip("How many corridor segments to place extending outward from each gate.")]
    private int corridorLength = 3;

    [Header("Grid Settings")]
    [SerializeField, Tooltip("Tile size in world units. Must match the actual size " +
        "of the wall/corner pieces in the modular kit (typically 4).")]
    private float tileSize = 4f;

    [SerializeField, Tooltip("How far to push walls outward from the grid edge. " +
        "Default is half the tile size so the wall face sits on the outer boundary.")]
    private float wallOutwardOffset = 2f;

    [SerializeField, Tooltip("How far to push gates outward from the grid edge. " +
        "Usually less than the wall offset so the gate sits slightly recessed.")]
    private float gateOutwardOffset = 1f;

    [SerializeField, Tooltip("Minimum grid width/height (inclusive). Must be ≥ 5.")]
    private int minSize = 5;

    [SerializeField, Tooltip("Maximum grid width/height (inclusive). Must be ≤ 30.")]
    private int maxSize = 30;

    [Header("Random Seed")]
    [SerializeField, Tooltip("When true, uses a different seed each time. " +
        "When false, uses the fixed Seed value below for reproducible layouts.")]
    private bool useRandomSeed = true;

    [SerializeField, Tooltip("Fixed seed for reproducible generation. " +
        "Only used when Use Random Seed is unchecked.")]
    private int seed;

    // ──────────────────────────────────────────────
    //  Generated State (read-only in Inspector)
    // ──────────────────────────────────────────────

    [Header("Generated (read-only)")]
    [SerializeField, Tooltip("The width of the last generated room (grid columns).")]
    private int lastWidth;

    [SerializeField, Tooltip("The height of the last generated room (grid rows).")]
    private int lastHeight;

    // ──────────────────────────────────────────────
    //  Hierarchy
    // ──────────────────────────────────────────────

    private const string RoomParentName = "DungeonRoom";

    /// <summary>Public accessor for tile size (used by DungeonBuilder for footprint checks).</summary>
    public float TileSize => tileSize;

    /// <summary>Public accessor for gate outward offset.</summary>
    public float GateOutwardOffset => gateOutwardOffset;

    // ──────────────────────────────────────────────
    //  Public API
    // ──────────────────────────────────────────────

    /// <summary>
    /// Destroys any previously generated room and builds a new one
    /// with freshly randomised dimensions.
    /// </summary>
    [ContextMenu("Generate Dungeon")]
    public void Generate()
    {
        ValidateConfiguration();

        Random.State previousState = Random.state;
        if (useRandomSeed)
            Random.InitState(System.Environment.TickCount);
        else
            Random.InitState(seed);

        int n = RandomOddInRange(minSize, maxSize);
        int m = RandomOddInRange(minSize, maxSize);

        Random.state = previousState;

        GenerateRoomAndCorridors(n, m);
    }

    /// <summary>
    /// Destroys any previously generated room and builds a new one
    /// with the given exact dimensions.
    /// </summary>
    public void Generate(int width, int height)
    {
        ValidateConfiguration();
        if (width < 3) width = 3;
        if (height < 3) height = 3;
        GenerateRoomAndCorridors(width, height);
    }

    /// <summary>
    /// Tears down the old room, builds a fresh one at origin,
    /// then extends corridors outward from each gate.
    /// </summary>
    private void GenerateRoomAndCorridors(int n, int m)
    {
        // --- Tear down any existing room ---
        Transform existing = transform.Find(RoomParentName);
        if (existing != null)
            DestroyImmediate(existing.gameObject);

        // --- Create fresh parent ---
        GameObject room = new GameObject(RoomParentName);
        room.transform.SetParent(transform, worldPositionStays: false);
        room.transform.localPosition = Vector3.zero;
        Transform roomT = room.transform;

        // --- Build room + get gate data ---
        GateInfo[] gates = BuildRoom(n, m, Vector3.zero, roomT);

        // --- Build corridors (if prefabs are assigned) ---
        if (corridorPrefab != null && corridorEndPrefab != null && corridorLength > 0)
        {
            foreach (GateInfo gate in gates)
            {
                BuildCorridor(gate.position, gate.direction, corridorLength, roomT);
            }
        }
    }

    // ──────────────────────────────────────────────
    //  Validation
    // ──────────────────────────────────────────────

    private void ValidateConfiguration()
    {
        if (cornerPrefab == null)
        {
            Debug.LogError("[DungeonGenerator] Corner Prefab is not assigned. " +
                           "Drag template-corner from the DungeonKit folder.", this);
        }

        if (wallPrefab == null)
        {
            Debug.LogError("[DungeonGenerator] Wall Prefab is not assigned. " +
                           "Drag template-wall from the DungeonKit folder.", this);
        }

        if (gatePrefab == null)
        {
            Debug.LogError("[DungeonGenerator] Gate Prefab is not assigned. " +
                           "Drag a gate variant from the DungeonKit folder.", this);
        }

        if (tileSize <= 0f)
        {
            Debug.LogWarning("[DungeonGenerator] Tile Size is zero or negative. " +
                             "Set it to the actual size of your kit pieces (typically 4).", this);
        }

        if (minSize < 5)
        {
            Debug.LogWarning("[DungeonGenerator] Min Size must be at least 5. Clamping to 5.", this);
            minSize = 5;
        }

        if (maxSize > 30)
        {
            Debug.LogWarning("[DungeonGenerator] Max Size must be at most 30. Clamping to 30.", this);
            maxSize = 30;
        }
    }

    // ──────────────────────────────────────────────
    //  Build
    // ──────────────────────────────────────────────

    /// <summary>
    /// Builds a room at the given origin under the given parent transform.
    /// Returns the four gate positions + outward directions for corridor
    /// attachment. Callers own tear-down and parent creation.
    /// </summary>
    public GateInfo[] BuildRoom(int n, int m, Vector3 origin, Transform parent)
    {
        float ox = origin.x;
        float oz = origin.z;
        float maxX = (n - 1) * tileSize;
        float maxZ = (m - 1) * tileSize;

        // --- Corners ---
        //   Model faces -Z and +X by default.
        //   SW: room inside +X,+Z → 270°    SE: room inside -X,+Z → 180°
        //   NW: room inside +X,-Z → 0°      NE: room inside -X,-Z → 90°
        PlacePiece(cornerPrefab, parent, ox, oz, 270f);  // SW
        PlacePiece(cornerPrefab, parent, ox + maxX, oz, 180f);  // SE
        PlacePiece(cornerPrefab, parent, ox, oz + maxZ, 0f);    // NW
        PlacePiece(cornerPrefab, parent, ox + maxX, oz + maxZ, 90f);   // NE

        int midX = n / 2;
        int midZ = m / 2;
        float wallOut = wallOutwardOffset;
        float gateOut = gateOutwardOffset;

        // --- Walls (pushed outward) ---
        for (int i = 1; i <= n - 2; i++)
        {
            float x = ox + i * tileSize;
            if (i != midX)
                PlacePiece(wallPrefab, parent, x, oz - wallOut, 180f);  // bottom
            if (i != midX)
                PlacePiece(wallPrefab, parent, x, oz + maxZ + wallOut, 0f);    // top
        }
        for (int j = 1; j <= m - 2; j++)
        {
            float z = oz + j * tileSize;
            if (j != midZ)
                PlacePiece(wallPrefab, parent, ox - wallOut, z, 270f);  // left
            if (j != midZ)
                PlacePiece(wallPrefab, parent, ox + maxX + wallOut, z, 90f);   // right
        }

        // --- Gates ---
        PlacePiece(gatePrefab, parent, ox + midX * tileSize, oz - gateOut, 0f);    // bottom
        PlacePiece(gatePrefab, parent, ox + midX * tileSize, oz + maxZ + gateOut, 180f);  // top
        PlacePiece(gatePrefab, parent, ox - gateOut, oz + midZ * tileSize, 270f);  // left
        PlacePiece(gatePrefab, parent, ox + maxX + gateOut, oz + midZ * tileSize, 90f);   // right

        // --- Floors (all grid cells except the 4 corners) ---
        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < m; j++)
            {
                bool isCorner = (i == 0 && j == 0)
                             || (i == n - 1 && j == 0)
                             || (i == 0 && j == m - 1)
                             || (i == n - 1 && j == m - 1);
                if (isCorner) continue;
                float floorRot = Random.Range(0, 4) * 90f; // 0, 90, 180, or 270
                PlacePiece(floorPrefab, parent, ox + i * tileSize, oz + j * tileSize, floorRot);
            }
        }

        // --- Record state ---
        lastWidth = n;
        lastHeight = m;

        Debug.Log($"[DungeonGenerator] Built {n}×{m} room at ({ox:F0}, {oz:F0}).", this);

        // --- Return gate data for corridor attachment ---
        return new GateInfo[]
        {
            new GateInfo { position = new Vector3(ox + midX * tileSize, 0, oz - gateOut),           direction = new Vector3(0, 0, -1) },
            new GateInfo { position = new Vector3(ox + midX * tileSize, 0, oz + maxZ + gateOut),     direction = new Vector3(0, 0,  1) },
            new GateInfo { position = new Vector3(ox - gateOut,          0, oz + midZ * tileSize),   direction = new Vector3(-1, 0, 0) },
            new GateInfo { position = new Vector3(ox + maxX + gateOut,   0, oz + midZ * tileSize),   direction = new Vector3( 1, 0, 0) },
        };
    }

    // ──────────────────────────────────────────────
    //  Corridor Builder
    // ──────────────────────────────────────────────

    /// <summary>
    /// Places a straight corridor of <paramref name="length"/> segments
    /// starting from <paramref name="startPos"/> and extending along
    /// <paramref name="direction"/>, capped with an end piece.
    /// </summary>
    public void BuildCorridor(Vector3 startPos, Vector3 direction, int length, Transform parent)
    {
        if (corridorPrefab == null || corridorEndPrefab == null) return;

        float rot = CorridorRotation(direction);

        for (int i = 0; i < length; i++)
        {
            Vector3 pos = startPos + direction * ((i + 1) * tileSize);
            PlacePiece(corridorPrefab, parent, pos.x, pos.z, rot);
        }

        Vector3 endPos = startPos + direction * ((length + 1) * tileSize);
        PlacePiece(corridorEndPrefab, parent, endPos.x, endPos.z, rot);
    }

    /// <summary>
    /// Converts an outward direction vector to the Y rotation needed
    /// for corridor pieces to run along that axis.
    /// </summary>
    private float CorridorRotation(Vector3 direction)
    {
        if (direction.z < 0) return 90f;     // south
        if (direction.z > 0) return 90f;   // north
        if (direction.x < 0) return 0f;   // west
        return 0f;                         // east
    }

    // ──────────────────────────────────────────────
    //  Data
    // ──────────────────────────────────────────────

    /// <summary>
    /// Position and outward direction of a gate, used to attach corridors.
    /// </summary>
    public struct GateInfo
    {
        public Vector3 position;
        public Vector3 direction;
    }

    // ──────────────────────────────────────────────
    //  Helpers
    // ──────────────────────────────────────────────

    /// <summary>
    /// Instantiates a piece at the given grid position with the given
    /// Y-axis rotation, parented under the room root.
    /// </summary>
    private void PlacePiece(GameObject prefab, Transform parent,
                            float x, float z, float yRotation)
    {
        if (prefab == null) return;

        Vector3 position = new Vector3(x, 0f, z);
        Quaternion rotation = Quaternion.Euler(0f, yRotation, 0f);

        GameObject piece = Instantiate(prefab, position, rotation, parent);
        piece.name = prefab.name; // Strip the "(Clone)" suffix for cleaner hierarchy.

        // --- Add MeshCollider if the prefab doesn't have a collider ---
        if (piece.GetComponent<Collider>() == null)
        {
            MeshCollider col = piece.AddComponent<MeshCollider>();
            // Shares the mesh from MeshFilter — this preserves openings
            // (e.g. gate doorways) that a BoxCollider would fill solid.
            MeshFilter mf = piece.GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null)
            {
                col.sharedMesh = mf.sharedMesh;
            }
        }
    }

    /// <summary>
    /// Returns a random odd integer in [min, max].
    /// </summary>
    private int RandomOddInRange(int min, int max)
    {
        if (min % 2 == 0) min++;
        if (max % 2 == 0) max--;
        int oddCount = (max - min) / 2 + 1;
        return min + Random.Range(0, oddCount) * 2;
    }
}
