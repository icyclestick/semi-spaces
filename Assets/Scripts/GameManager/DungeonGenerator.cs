using UnityEngine;

/// <summary>
/// Procedurally generates a rectangular dungeon room from the DungeonKit
/// modular pieces. Places corners at the four corners, walls along each
/// edge, and a gate at the midpoint of each side.
///
/// Setup:
///   1. Create an empty "DungeonGenerator" GameObject in the scene.
///   2. Attach this script.
///   3. Drag template-corner, template-wall, and a gate variant (gate, gate-door,
///      gate-door-window, or gate-metal-bars) from the DungeonKit folder into the
///      Prefab fields in the Inspector.
///   4. Set Tile Size to match the actual dimensions of the wall/corner pieces.
///   5. Click "Generate Dungeon" in the context menu (⋮ → Generate Dungeon),
///      or call Generate() from another script at runtime.
///
/// The generated room is parented under a "DungeonRoom" child GameObject so
/// it's easy to delete and regenerate.
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

    [Header("Grid Settings")]
    [SerializeField, Tooltip("Tile size in world units. Must match the actual size " +
        "of the wall/corner pieces in the modular kit (typically 4).")]
    private float tileSize = 4f;

    [SerializeField, Tooltip("Minimum grid width/height (inclusive). Must be ≥ 3.")]
    private int minSize = 3;

    [SerializeField, Tooltip("Maximum grid width/height (inclusive). Must be ≤ 10.")]
    private int maxSize = 10;

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

        // Set the random seed.
        Random.State previousState = Random.state;
        if (useRandomSeed)
        {
            Random.InitState(System.Environment.TickCount);
        }
        else
        {
            Random.InitState(seed);
        }

        // Pick random grid dimensions.
        int n = Random.Range(minSize, maxSize + 1); // width  (X-axis)
        int m = Random.Range(minSize, maxSize + 1); // height (Z-axis)

        // Restore state so repeated calls from code don't cascade.
        Random.state = previousState;

        BuildRoom(n, m);
    }

    /// <summary>
    /// Destroys any previously generated room and builds a new one
    /// with the given exact dimensions.
    /// </summary>
    /// <param name="width">Grid columns (n), must be ≥ 3.</param>
    /// <param name="height">Grid rows (m), must be ≥ 3.</param>
    public void Generate(int width, int height)
    {
        ValidateConfiguration();

        if (width < 3) width = 3;
        if (height < 3) height = 3;

        BuildRoom(width, height);
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

        if (minSize < 3)
        {
            Debug.LogWarning("[DungeonGenerator] Min Size must be at least 3. Clamping to 3.", this);
            minSize = 3;
        }

        if (maxSize > 10)
        {
            Debug.LogWarning("[DungeonGenerator] Max Size must be at most 10. Clamping to 10.", this);
            maxSize = 10;
        }
    }

    // ──────────────────────────────────────────────
    //  Build
    // ──────────────────────────────────────────────

    /// <summary>
    /// Core generation routine. Destroys the old room, creates a fresh
    /// parent, then places corners → walls → gates for an n×m grid.
    /// </summary>
    private void BuildRoom(int n, int m)
    {
        // --- Tear down any existing room ---
        Transform existing = transform.Find(RoomParentName);
        if (existing != null)
        {
            DestroyImmediate(existing.gameObject);
        }

        // --- Create fresh parent ---
        GameObject room = new GameObject(RoomParentName);
        room.transform.SetParent(transform, worldPositionStays: false);
        room.transform.localPosition = Vector3.zero;
        Transform roomT = room.transform;

        // --- Positions ---
        // Grid origin is at world (0, 0, 0) under the room parent.
        // X-axis = columns (n), Z-axis = rows (m).

        float maxX = (n - 1) * tileSize;
        float maxZ = (m - 1) * tileSize;

        // --- Corners ---
        //   SW: (0,    0   )  — assumes default model faces SW
        //   SE: (maxX, 0   )  — rotate Y +90°
        //   NW: (0,    maxZ)  — rotate Y +270° (-90°)
        //   NE: (maxX, maxZ)  — rotate Y +180°
        PlacePiece(cornerPrefab, roomT, 0,     0,     0f);
        PlacePiece(cornerPrefab, roomT, maxX,  0,     90f);
        PlacePiece(cornerPrefab, roomT, 0,     maxZ,  270f);
        PlacePiece(cornerPrefab, roomT, maxX,  maxZ,  180f);

        // --- Walls ---
        // Bottom edge (Z = 0,   X = 1..n-2): wall faces south (Y=0)
        // Top edge    (Z = maxZ, X = 1..n-2): wall faces north (Y=180)
        // Left edge   (X = 0,    Z = 1..m-2): wall faces west  (Y=270)
        // Right edge  (X = maxX, Z = 1..m-2): wall faces east  (Y=90)
        for (int i = 1; i <= n - 2; i++)
        {
            float x = i * tileSize;
            PlacePiece(wallPrefab, roomT, x, 0,     0f);    // bottom
            PlacePiece(wallPrefab, roomT, x, maxZ,  180f);  // top
        }
        for (int j = 1; j <= m - 2; j++)
        {
            float z = j * tileSize;
            PlacePiece(wallPrefab, roomT, 0,    z, 270f);  // left
            PlacePiece(wallPrefab, roomT, maxX, z, 90f);   // right
        }

        // --- Gates (replace wall at midpoint of each side) ---
        int midX = n / 2;  // integer division → center column
        int midZ = m / 2;  // integer division → center row

        PlacePiece(gatePrefab, roomT, midX * tileSize, 0,     0f);    // bottom gate
        PlacePiece(gatePrefab, roomT, midX * tileSize, maxZ,  180f);  // top gate
        PlacePiece(gatePrefab, roomT, 0,    midZ * tileSize, 270f);   // left gate
        PlacePiece(gatePrefab, roomT, maxX, midZ * tileSize, 90f);    // right gate

        // --- Record state ---
        lastWidth = n;
        lastHeight = m;

        Debug.Log($"[DungeonGenerator] Generated {n}×{m} dungeon room " +
                  $"(tile size: {tileSize}, world size: {maxX + tileSize}×{maxZ + tileSize}).", this);
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
    }
}
