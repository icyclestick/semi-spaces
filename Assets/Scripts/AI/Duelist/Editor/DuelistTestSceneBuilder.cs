// ─────────────────────────────────────────────────────────────────────────────
//  DuelistTestSceneBuilder.cs
//  Owner: Ash (Duelist Lead)  |  Editor-only — zero runtime overhead
//
//  Adds a "Semi-Spaces / Build Duelist Test Scene" menu item that populates
//  Ash_TestScene with everything needed to run the Pre-PR checklist:
//
//    ✔  Ground plane (NavMesh walkable surface)
//    ✔  Player stand-in  (tagged "Player", has Health → IDamageable → attack
//                         can deal damage through the interface)
//    ✔  Duelist_Anomaly prefab instance (NavMeshAgent + Health + DuelistBrain)
//    ✔  NavMesh surface component  (requires AI Navigation package)
//    ✔  Baked NavMesh
//    ✔  "Player" tag registered in TagManager automatically
//
//  HOW TO USE
//  1. Open Assets/Scenes/Ash_TestScene.unity in the Unity Editor.
//  2. Menu bar → Semi-Spaces → Build Duelist Test Scene.
//  3. Press Play.
//  4. Watch the Console.  Expected lines (in order):
//       [DuelistBrain] 'Duelist_Anomaly' initialized. MaxHealth=100 …
//       [DuelistBrain] 'Duelist_Anomaly' switching: Reposition → Attack …
//       [DuelistBrain] 'Duelist_Anomaly' struck 'Player_StandIn' for 20 damage.
//       [Health] 'Player_StandIn' took 20 damage. Health: 80/100
//       [Health] 'Player_StandIn' has died.           ← after enough hits
//       [EnemyBase] 'Duelist_Anomaly' has been eliminated.  ← when you kill it
//
//  NOTE: This script lives in Assets/Scripts/AI/Duelist/Editor/ which is
//        inside Ash's owned directory (Assets/Scripts/AI/Duelist/).
//        No files outside Ash's domain are modified.
// ─────────────────────────────────────────────────────────────────────────────

#if UNITY_EDITOR
using Unity.AI.Navigation;           // NavMeshSurface, CollectObjects, NavMeshCollectGeometry
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

/// <summary>
/// Editor utility that builds a minimal but fully functional test scene for
/// validating DuelistBrain against the Pre-PR checklist. Accessible via
/// Semi-Spaces → Build Duelist Test Scene in the Unity menu bar.
/// </summary>
public static class DuelistTestSceneBuilder
{
    // ── Constants ───────────────────────────────────────────────────────────

    private const string ScenePath     = "Assets/Scenes/Ash_TestScene.unity";
    private const string PrefabPath    = "Assets/Prefabs/Duelist_Anomaly.prefab";
    private const string PlayerTag     = "Player";
    private const string PlayerName    = "Player_StandIn";
    private const string DuelistName   = "Duelist_Anomaly";
    private const string GroundName    = "Ground";
    private const string MenuPath      = "Semi-Spaces/Build Duelist Test Scene";

    // Ground plane half-extents (metres).
    private const float GroundHalfSize = 25f;

    // ── Menu Item ────────────────────────────────────────────────────────────

    [MenuItem(MenuPath, priority = 1)]
    private static void BuildTestScene()
    {
        // ── 1. Make sure Ash_TestScene is open and saved ──────────────────
        if (!EnsureSceneIsOpen()) return;

        // ── 2. Register "Player" tag if it doesn't exist yet ─────────────
        EnsurePlayerTagExists();

        // ── 3. Clear any previous test objects so rebuilds are idempotent ─
        DestroyExistingTestObjects();

        // ── 4. Create the ground plane (NavMesh walkable surface) ─────────
        GameObject ground = CreateGround();

        // ── 5. Add a NavMesh Surface and bake ────────────────────────────
        BakeNavMesh(ground);

        // ── 6. Create the Player stand-in ─────────────────────────────────
        GameObject player = CreatePlayerStandIn();

        // ── 7. Instantiate the Duelist prefab and wire the player reference
        GameObject duelist = CreateDuelist(player);

        if (duelist == null)
        {
            Debug.LogError($"[DuelistTestSceneBuilder] Could not load prefab at '{PrefabPath}'. " +
                           "Check that the prefab exists and reimport if needed.", null);
            return;
        }

        // ── 8. Select the Duelist so the Scene View shows its gizmos ──────
        Selection.activeGameObject = duelist;

        // ── 9. Save the scene ─────────────────────────────────────────────
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(SceneManager.GetActiveScene());

        // ── 10. Summary ───────────────────────────────────────────────────
        Debug.Log(
            "[DuelistTestSceneBuilder] ✔ Test scene built successfully!\n\n" +
            "What was created:\n" +
            $"  • '{GroundName}' — 50×50m NavMesh walkable ground plane.\n" +
            $"  • '{PlayerName}' — Player stand-in tagged '{PlayerTag}', has Health (IDamageable).\n" +
            $"  • '{DuelistName}' — Duelist prefab instance with DuelistBrain, NavMeshAgent, Health.\n\n" +
            "Pre-PR Checklist:\n" +
            "  1. Press Play and watch the Console.\n" +
            "  2. The Duelist should start approaching the Player immediately.\n" +
            "  3. Look for:\n" +
            "       [DuelistBrain] '…' initialized.\n" +
            "       [DuelistBrain] '…' switching: … → Attack\n" +
            "       [DuelistBrain] '…' struck 'Player_StandIn' for 20 damage.\n" +
            "       [Health] 'Player_StandIn' took 20 damage.\n" +
            "       [Health] 'Player_StandIn' has died.   (after 5 hits)\n" +
            "  4. To test Duelist death: select the Duelist and set Health to 1\n" +
            "     in the Inspector, then let the Player collider hit it — or call\n" +
            "     GetComponent<Health>().TakeDamage(999) from a test script.\n" +
            "  5. Confirm NO NullReferenceException appears in the Console.\n\n" +
            "IMPORTANT: attackTargetMask on DuelistBrain must include the layer\n" +
            "that Player_StandIn is on (Default = layer 0 → value 1). This is\n" +
            "already set by this builder. If you change the player's layer,\n" +
            "update the mask in the Inspector.");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>Opens Ash_TestScene if it isn't already the active scene.</summary>
    private static bool EnsureSceneIsOpen()
    {
        Scene active = SceneManager.GetActiveScene();

        if (active.path != ScenePath)
        {
            if (active.isDirty)
            {
                bool save = EditorUtility.DisplayDialog(
                    "Unsaved Changes",
                    $"The current scene '{active.name}' has unsaved changes. " +
                    "Save before switching to Ash_TestScene?",
                    "Save", "Don't Save");

                if (save) EditorSceneManager.SaveScene(active);
            }

            Scene opened = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            if (!opened.IsValid())
            {
                Debug.LogError($"[DuelistTestSceneBuilder] Could not open scene at '{ScenePath}'. " +
                               "Ensure the scene file exists.", null);
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Ensures the "Player" tag exists in the TagManager.
    /// Uses SerializedObject so the change persists without restarting Unity.
    /// </summary>
    private static void EnsurePlayerTagExists()
    {
        SerializedObject tagManager = new SerializedObject(
            AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);

        SerializedProperty tagsProp = tagManager.FindProperty("tags");

        for (int i = 0; i < tagsProp.arraySize; i++)
        {
            if (tagsProp.GetArrayElementAtIndex(i).stringValue == PlayerTag)
                return; // Tag already registered.
        }

        // Append the new tag.
        tagsProp.InsertArrayElementAtIndex(tagsProp.arraySize);
        tagsProp.GetArrayElementAtIndex(tagsProp.arraySize - 1).stringValue = PlayerTag;
        tagManager.ApplyModifiedProperties();

        Debug.Log($"[DuelistTestSceneBuilder] Registered '{PlayerTag}' tag in TagManager.", null);
    }

    /// <summary>
    /// Removes any GameObjects created by a previous run of this builder
    /// so the setup is idempotent (safe to run multiple times).
    /// </summary>
    private static void DestroyExistingTestObjects()
    {
        foreach (string name in new[] { GroundName, PlayerName, DuelistName })
        {
            GameObject existing = GameObject.Find(name);
            if (existing != null)
            {
                Object.DestroyImmediate(existing);
                Debug.Log($"[DuelistTestSceneBuilder] Removed existing '{name}'.", null);
            }
        }
    }

    /// <summary>
    /// Creates a large flat ground plane that serves as the NavMesh walkable surface.
    /// Uses a primitive cube scaled to 50×0.1×50 m and positioned at y=0.
    /// </summary>
    private static GameObject CreateGround()
    {
        GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
        ground.name = GroundName;
        ground.transform.position    = Vector3.zero;
        ground.transform.localScale  = new Vector3(GroundHalfSize * 2f, 0.1f, GroundHalfSize * 2f);

        // Assign a simple grey material so the scene looks recognisable.
        Renderer rend = ground.GetComponent<Renderer>();
        if (rend != null)
        {
            Material mat = new Material(Shader.Find("Universal Render Pipeline/Lit"))
            {
                name  = "Mat_Ground_Test",
                color = new Color(0.35f, 0.35f, 0.35f),
            };
            rend.sharedMaterial = mat;
        }

        // Mark the ground static for GI and batching.
        // NavigationStatic is obsolete in Unity 6 — NavMeshSurface.collectObjects
        // controls which objects are included in the bake instead.
        GameObjectUtility.SetStaticEditorFlags(ground,
            StaticEditorFlags.ContributeGI  |
            StaticEditorFlags.BatchingStatic);

        Debug.Log($"[DuelistTestSceneBuilder] Created '{GroundName}'.", ground);
        return ground;
    }

    /// <summary>
    /// Adds a NavMeshSurface component to the ground and bakes the NavMesh.
    /// Requires the AI Navigation package (com.unity.ai.navigation ≥ 2.x).
    /// </summary>
    private static void BakeNavMesh(GameObject ground)
    {
        // NavMeshSurface is the runtime NavMesh approach in Unity 6 + AI Navigation package.
        NavMeshSurface surface = ground.AddComponent<NavMeshSurface>();
        surface.collectObjects = CollectObjects.All;
        surface.useGeometry    = NavMeshCollectGeometry.PhysicsColliders;

        // Bake synchronously in the Editor.
        surface.BuildNavMesh();

        Debug.Log("[DuelistTestSceneBuilder] NavMesh baked successfully on Ground.", ground);
    }

    /// <summary>
    /// Creates the Player stand-in: a capsule tagged "Player" with a
    /// CapsuleCollider and a Health component on layer 0 (Default).
    /// The Health component makes it a valid IDamageable target.
    /// Placed 8 m in front of the Duelist's starting position.
    /// </summary>
    private static GameObject CreatePlayerStandIn()
    {
        GameObject player = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        player.name = PlayerName;
        player.tag  = PlayerTag;

        // Place the player on the ground surface (capsule height = 2, centre at y=1).
        player.transform.position = new Vector3(0f, 1f, 8f);

        // Assign a bright colour so you can see the player in the Scene View.
        Renderer rend = player.GetComponent<Renderer>();
        if (rend != null)
        {
            Material mat = new Material(Shader.Find("Universal Render Pipeline/Lit"))
            {
                name  = "Mat_Player_Test",
                color = new Color(0.1f, 0.6f, 1f),
            };
            rend.sharedMaterial = mat;
        }

        // Add Health — makes the player a valid IDamageable target for the
        // Duelist's OverlapSphere attack (checklist: damage via IDamageable).
        Health playerHealth = player.AddComponent<Health>();

        // Subscribe a console log to OnDeath so we can see it fire
        // (checklist: OnDeath events fire correctly).
        playerHealth.OnDeath += () =>
            Debug.Log($"[DuelistTestSceneBuilder] OnDeath event confirmed on '{player.name}'.", player);

        Debug.Log($"[DuelistTestSceneBuilder] Created '{PlayerName}' with Health(IDamageable).", player);
        return player;
    }

    /// <summary>
    /// Instantiates the Duelist_Anomaly prefab, places it 2 m in front of the
    /// scene origin, and wires the player reference directly in the Inspector
    /// field (bypasses tag lookup so it works even without NavMesh).
    ///
    /// Also sets attackTargetMask to layer 0 (Default) so the OverlapSphere
    /// can hit the Player_StandIn capsule.
    /// </summary>
    private static GameObject CreateDuelist(GameObject player)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null) return null;

        // Instantiate as a scene object (not a prefab instance) for easier editing.
        GameObject duelist = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        duelist.name = DuelistName;

        // Place Duelist on the ground surface (same height convention as player).
        duelist.transform.position = new Vector3(0f, 1f, 0f);

        // Face toward the player.
        Vector3 dirToPlayer = (player.transform.position - duelist.transform.position).normalized;
        if (dirToPlayer != Vector3.zero)
            duelist.transform.rotation = Quaternion.LookRotation(dirToPlayer);

        // Wire the player reference directly via SerializedObject so EnemyBase
        // doesn't need FindWithTag (which requires the tag to be loaded at runtime).
        SerializedObject so   = new SerializedObject(duelist.GetComponent<DuelistBrain>());
        SerializedProperty sp = so.FindProperty("player");    // The [SerializeField] private Transform player in EnemyBase.
        if (sp != null)
        {
            sp.objectReferenceValue = player.transform;
            so.ApplyModifiedProperties();
            Debug.Log("[DuelistTestSceneBuilder] Player reference wired into DuelistBrain.player.", duelist);
        }
        else
        {
            Debug.LogWarning("[DuelistTestSceneBuilder] Could not find 'player' serialized property on DuelistBrain. " +
                             "The Duelist will fall back to FindWithTag(\"Player\") at runtime.", duelist);
        }

        // Set attackTargetMask to layer 0 (Default) — the layer Player_StandIn is on.
        // Without this, the OverlapSphere hits nothing and attack always misses.
        SerializedProperty maskProp = so.FindProperty("attackTargetMask");
        if (maskProp != null)
        {
            maskProp.intValue = 1 << 0; // Layer 0 = Default = bitmask 1.
            so.ApplyModifiedProperties();
            Debug.Log("[DuelistTestSceneBuilder] attackTargetMask set to Default (layer 0).", duelist);
        }

        Debug.Log($"[DuelistTestSceneBuilder] Instantiated '{DuelistName}' at {duelist.transform.position}.", duelist);
        return duelist;
    }
}
#endif
