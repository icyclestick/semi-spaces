using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

/// <summary>
/// Manages weapon switching for a parent Weapon_Holder object. Automatically
/// discovers child weapons on Start, equips the first one, and listens for
/// number keys (1-9) and scroll wheel to switch between them. Scales
/// dynamically — add more child weapons and the inputs scale with them.
///
/// Uses the New Input System (inline InputAction workflow), matching the
/// project's established architecture.
///
/// Setup:
///   1. Create an empty "Weapon_Holder" child under the Player.
///   2. Place each weapon (with its own RaycastShooter) as a child of Weapon_Holder.
///   3. Attach this script to Weapon_Holder.
///   4. Press Play — weapon 1 is equipped automatically.
///
/// Hierarchy example:
///   Player
///   ├── Camera
///   └── Weapon_Holder         ← this script
///       ├── Rifle             ← index 0 (key "1")
///       ├── Shotgun           ← index 1 (key "2")
///       └── SMG               ← index 2 (key "3")
/// </summary>
public class WeaponManager : MonoBehaviour
{
    // ──────────────────────────────────────────────
    //  Configuration
    // ──────────────────────────────────────────────

    [Header("Configuration")]
    [SerializeField, Tooltip("Index of the weapon to equip on Start. 0 = first child.")]
    private int defaultWeaponIndex = 0;

    // ──────────────────────────────────────────────
    //  Events
    // ──────────────────────────────────────────────
    //
    //  Wire these in the Inspector for HUD integration.
    //  Example: update a weapon icon, play an equip animation, etc.
    //

    [Header("Events")]
    [SerializeField, Tooltip("Fired when the active weapon changes. Passes (newIndex, totalWeapons).")]
    private UnityEvent<int, int> onWeaponSwitched;

    // ──────────────────────────────────────────────
    //  Input Actions
    // ──────────────────────────────────────────────

    /// <summary>Scroll wheel Y axis — cycles weapons up/down.</summary>
    private InputAction scrollAction;

    /// <summary>
    /// Number key actions (Alpha1–Alpha9). Built dynamically based
    /// on how many child weapons exist, up to a maximum of 9.
    /// </summary>
    private InputAction[] numberKeyActions;

    // ──────────────────────────────────────────────
    //  Internal State
    // ──────────────────────────────────────────────

    /// <summary>Array of child weapon Transforms, cached on Start.</summary>
    private Transform[] weapons;

    /// <summary>
    /// Index of the currently active weapon. Initialised to -1 so the
    /// first SwitchWeapon call on Start always runs the full loop
    /// (deactivating all non-selected weapons).
    /// </summary>
    private int currentIndex = -1;

    // ──────────────────────────────────────────────
    //  Public Accessors
    // ──────────────────────────────────────────────

    /// <summary>The currently active weapon's index (read-only).</summary>
    public int CurrentWeaponIndex => currentIndex;

    /// <summary>Total number of weapons in the holder (read-only).</summary>
    public int WeaponCount => weapons != null ? weapons.Length : 0;

    /// <summary>The currently active weapon's GameObject (read-only).</summary>
    public GameObject ActiveWeapon =>
        weapons != null && currentIndex >= 0 && currentIndex < weapons.Length
            ? weapons[currentIndex].gameObject
            : null;

    // ──────────────────────────────────────────────
    //  Lifecycle
    // ──────────────────────────────────────────────

    private void Awake()
    {
        // --- Discover all child weapons ---
        // Every direct child of this object is treated as a weapon.
        // We cache them in an array for fast indexed access.
        int childCount = transform.childCount;

        if (childCount == 0)
        {
            Debug.LogWarning("[WeaponManager] No child weapons found. " +
                             "Add weapon GameObjects as children of this object.", this);
            weapons = new Transform[0];
            return;
        }

        weapons = new Transform[childCount];
        for (int i = 0; i < childCount; i++)
        {
            weapons[i] = transform.GetChild(i);
        }

        Debug.Log($"[WeaponManager] Found {weapons.Length} weapon(s): " +
                  string.Join(", ", System.Array.ConvertAll(weapons, w => w.name)), this);

        // --- Build input actions ---
        BuildInputActions();
    }

    private void Start()
    {
        // Equip the default weapon, disabling all others.
        SwitchWeapon(defaultWeaponIndex);
    }

    private void OnEnable()
    {
        scrollAction?.Enable();

        if (numberKeyActions != null)
        {
            for (int i = 0; i < numberKeyActions.Length; i++)
            {
                numberKeyActions[i].Enable();
            }
        }
    }

    private void OnDisable()
    {
        scrollAction?.Disable();

        if (numberKeyActions != null)
        {
            for (int i = 0; i < numberKeyActions.Length; i++)
            {
                numberKeyActions[i].Disable();
            }
        }
    }

    /// <summary>
    /// Disposes all inline InputActions on destruction to prevent
    /// native memory leaks. Matches the pattern in RaycastShooter.
    /// </summary>
    private void OnDestroy()
    {
        scrollAction?.Dispose();
        scrollAction = null;

        if (numberKeyActions != null)
        {
            for (int i = 0; i < numberKeyActions.Length; i++)
            {
                numberKeyActions[i]?.Dispose();
            }
            numberKeyActions = null;
        }
    }

    private void Update()
    {
        if (weapons == null || weapons.Length == 0) return;

        HandleNumberKeys();
        HandleScrollWheel();
    }

    // ──────────────────────────────────────────────
    //  Input Building
    // ──────────────────────────────────────────────

    /// <summary>
    /// Creates inline InputActions for number keys (1–9, scaled to weapon
    /// count) and the scroll wheel. No .inputactions asset required.
    /// </summary>
    private void BuildInputActions()
    {
        // --- Number keys ---
        // We only create as many key bindings as there are weapons,
        // capped at 9 (Alpha1 through Alpha9). If you have 3 weapons,
        // only keys 1, 2, 3 are bound — no wasted listeners.
        int keyCount = Mathf.Min(weapons.Length, 9);
        numberKeyActions = new InputAction[keyCount];

        // Alpha key codes: Alpha1 = <Keyboard>/1, Alpha2 = <Keyboard>/2, etc.
        for (int i = 0; i < keyCount; i++)
        {
            string keyName = $"Weapon{i + 1}";
            string binding = $"<Keyboard>/{i + 1}";

            numberKeyActions[i] = new InputAction(keyName, InputActionType.Button);
            numberKeyActions[i].AddBinding(binding);

            // Also bind numpad keys for desktop convenience.
            string numpadBinding = $"<Keyboard>/numpad{i + 1}";
            numberKeyActions[i].AddBinding(numpadBinding);
        }

        // D-pad up/down as an alternative for gamepads.
        scrollAction = new InputAction("WeaponScroll", InputActionType.Value);
        scrollAction.AddBinding("<Mouse>/scroll/y");
        scrollAction.AddBinding("<Gamepad>/dpad/y");
    }

    // ──────────────────────────────────────────────
    //  Input Handling
    // ──────────────────────────────────────────────

    /// <summary>
    /// Checks if any number key (1–9) was pressed this frame and
    /// switches to the corresponding weapon.
    /// </summary>
    private void HandleNumberKeys()
    {
        for (int i = 0; i < numberKeyActions.Length; i++)
        {
            if (numberKeyActions[i].WasPressedThisFrame())
            {
                SwitchWeapon(i);
                return; // Only one key can register per frame.
            }
        }
    }

    /// <summary>
    /// Reads the scroll wheel delta and cycles weapons up or down.
    /// Scroll up = next weapon, scroll down = previous weapon.
    /// Wraps around at both ends.
    /// </summary>
    private void HandleScrollWheel()
    {
        if (scrollAction == null) return;

        float scrollValue = scrollAction.ReadValue<float>();

        // Dead zone — ignore tiny scroll values.
        if (Mathf.Abs(scrollValue) < 0.1f) return;

        if (scrollValue > 0f)
        {
            // Scroll up → next weapon (wraps to 0 at the end).
            int nextIndex = (currentIndex + 1) % weapons.Length;
            SwitchWeapon(nextIndex);
        }
        else
        {
            // Scroll down → previous weapon (wraps to last at the start).
            int prevIndex = (currentIndex - 1 + weapons.Length) % weapons.Length;
            SwitchWeapon(prevIndex);
        }
    }

    // ──────────────────────────────────────────────
    //  Weapon Switching
    // ──────────────────────────────────────────────

    /// <summary>
    /// Activates the weapon at the given index and deactivates all
    /// others. Out-of-range indices are clamped and logged.
    ///
    /// This is the single entry point for all weapon switches — number
    /// keys, scroll wheel, and external scripts all call this method.
    /// </summary>
    /// <param name="index">Zero-based index of the weapon to equip.</param>
    public void SwitchWeapon(int index)
    {
        if (weapons == null || weapons.Length == 0) return;

        // Clamp the index to valid range.
        if (index < 0 || index >= weapons.Length)
        {
            Debug.LogWarning($"[WeaponManager] Invalid weapon index {index}. " +
                             $"Valid range: 0–{weapons.Length - 1}.", this);
            index = Mathf.Clamp(index, 0, weapons.Length - 1);
        }

        // Skip if already on this weapon (no redundant switches).
        if (index == currentIndex && weapons[index].gameObject.activeSelf)
        {
            return;
        }

        // --- Deactivate all, activate the selected ---
        // SetActive(false) on a weapon disables its Update loop,
        // renderers, and input actions (via OnDisable in RaycastShooter).
        for (int i = 0; i < weapons.Length; i++)
        {
            weapons[i].gameObject.SetActive(i == index);
        }

        currentIndex = index;

        Debug.Log($"[WeaponManager] Switched to weapon {index}: '{weapons[index].name}'", this);

        // Notify HUD — update weapon icons, names, etc.
        onWeaponSwitched?.Invoke(currentIndex, weapons.Length);
    }
}
