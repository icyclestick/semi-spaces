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

    [SerializeField, Tooltip("The floor tile piece (template-floor.fbx).")]
    private GameObject floorPrefab;

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

        // Pick random odd grid dimensions so the gate lands exactly centered.
        int n = RandomOddInRange(minSize, maxSize);
        int m = RandomOddInRange(minSize, maxSize);

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
        //   Model faces -Z and +X by default.
        //   SW at (0,    0   ): room inside is +X,+Z  → rotate Y 270°
        //   SE at (maxX, 0   ): room inside is -X,+Z  → rotate Y 180°
        //   NW at (0,    maxZ): room inside is +X,-Z  → rotate Y 0°
        //   NE at (maxX, maxZ): room inside is -X,-Z  → rotate Y 90°
        PlacePiece(cornerPrefab, roomT, 0, 0, 270f);  // SW
        PlacePiece(cornerPrefab, roomT, maxX, 0, 180f);  // SE
        PlacePiece(cornerPrefab, roomT, 0, maxZ, 0f);    // NW
        PlacePiece(cornerPrefab, roomT, maxX, maxZ, 90f);   // NE

        // --- Gate positions (computed first so walls skip them) ---
        int midX = n / 2;  // integer division → center column
        int midZ = m / 2;  // integer division → center row

        float wallOut = wallOutwardOffset;
        float gateOut = gateOutwardOffset;

        // --- Walls (pushed outward so the wall face sits on the boundary) ---
        // Bottom edge (Z = -wallOut, X = 1..n-2)
        // Top edge    (Z = maxZ + wallOut, X = 1..n-2)
        // Left edge   (X = -wallOut, Z = 1..m-2)
        // Right edge  (X = maxX + wallOut, Z = 1..m-2)
        for (int i = 1; i <= n - 2; i++)
        {
            float x = i * tileSize;
            if (i != midX)
                PlacePiece(wallPrefab, roomT, x, -wallOut, 180f);           // bottom (skip gate)
            if (i != midX)
                PlacePiece(wallPrefab, roomT, x, maxZ + wallOut, 0f);       // top (skip gate)
        }
        for (int j = 1; j <= m - 2; j++)
        {
            float z = j * tileSize;
            if (j != midZ)
                PlacePiece(wallPrefab, roomT, -wallOut, z, 270f);           // left (skip gate)
            if (j != midZ)
                PlacePiece(wallPrefab, roomT, maxX + wallOut, z, 90f);      // right (skip gate)
        }

        // --- Gates (pushed outward by gateOutwardOffset — less than walls) ---
        PlacePiece(gatePrefab, roomT, midX * tileSize, -gateOut, 0f);            // bottom gate
        PlacePiece(gatePrefab, roomT, midX * tileSize, maxZ + gateOut, 180f);     // top gate
        PlacePiece(gatePrefab, roomT, -gateOut, midZ * tileSize, 270f);           // left gate
        PlacePiece(gatePrefab, roomT, maxX + gateOut, midZ * tileSize, 90f);      // right gate

        // --- Floors (all grid cells except the 4 corners) ---
        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < m; j++)
            {
                // Skip the four corners.
                bool isCorner = (i == 0 && j == 0)
                             || (i == n - 1 && j == 0)
                             || (i == 0 && j == m - 1)
                             || (i == n - 1 && j == m - 1);
                if (isCorner) continue;

                float x = i * tileSize;
                float z = j * tileSize;
                PlacePiece(floorPrefab, roomT, x, z, 0f);
            }
        }

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
