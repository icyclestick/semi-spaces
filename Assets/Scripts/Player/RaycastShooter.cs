using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

/// <summary>
/// A hitscan weapon system with fire-rate limiting, ammo management,
/// and reload logic. Resolves damage through the IDamageable interface
/// so it works with any damageable entity. Uses the New Input System
/// (inline InputAction workflow).
///
/// Setup:
///   1. Attach this script to the Player GameObject (alongside FirstPersonController).
///   2. Assign the child Camera to the 'shootOrigin' field in the Inspector.
///   3. Tune all weapon stats in the Inspector.
///   4. Ensure targets have a Collider and an IDamageable component (e.g. Health).
///   5. (Optional) Wire the UnityEvents for HUD integration.
///   6. Input actions are self-contained — no InputActions asset required.
/// </summary>
public class RaycastShooter : MonoBehaviour
{
    // ──────────────────────────────────────────────
    //  Weapon Stats
    // ──────────────────────────────────────────────

    [Header("Weapon Stats")]
    [SerializeField, Tooltip("Damage dealt to an IDamageable target per shot.")]
    private int weaponDamage = 10;

    [SerializeField, Tooltip("Maximum distance the weapon's raycast can travel (in units).")]
    private float weaponRange = 50f;

    [SerializeField, Tooltip("Minimum time between shots (in seconds). Lower = faster fire. 0.1 = 10 rounds/sec.")]
    private float fireRate = 0.15f;

    // ──────────────────────────────────────────────
    //  Ammo
    // ──────────────────────────────────────────────

    [Header("Ammo")]
    [SerializeField, Tooltip("Maximum rounds in a full magazine.")]
    private int maxAmmo = 30;

    [SerializeField, Tooltip("Time in seconds it takes to complete a reload.")]
    private float reloadTime = 1.5f;

    // ──────────────────────────────────────────────
    //  References
    // ──────────────────────────────────────────────

    [Header("References")]
    [SerializeField, Tooltip("The Camera transform from which the ray is cast (should be the player's FPS camera).")]
    private Transform shootOrigin;

    // ──────────────────────────────────────────────
    //  Events (UnityEvent — for Inspector / HUD wiring)
    // ──────────────────────────────────────────────
    //
    //  These events let the UI designer build a HUD without touching
    //  this script. Wire them in the Inspector to update ammo counters,
    //  show reload indicators, play animations, etc.
    //

    [Header("Events")]
    [SerializeField, Tooltip("Fired whenever the ammo count changes. Passes (currentAmmo, maxAmmo).")]
    private UnityEvent<int, int> onAmmoChanged;

    [SerializeField, Tooltip("Fired when a reload begins. Use to trigger HUD animations or sound.")]
    private UnityEvent onReloadStart;

    [SerializeField, Tooltip("Fired when a reload completes.")]
    private UnityEvent onReloadFinish;

    // ──────────────────────────────────────────────
    //  Debug
    // ──────────────────────────────────────────────

    [Header("Debug")]
    [SerializeField, Tooltip("Duration (in seconds) that the debug ray is drawn in the Scene view.")]
    private float debugRayDuration = 0.5f;

    // ──────────────────────────────────────────────
    //  Input Actions
    // ──────────────────────────────────────────────

    /// <summary>Left Mouse Button / Right Trigger — fires the weapon.</summary>
    private InputAction shootAction;

    /// <summary>R / Gamepad West — triggers a manual reload.</summary>
    private InputAction reloadAction;

    // ──────────────────────────────────────────────
    //  Internal State
    // ──────────────────────────────────────────────

    /// <summary>Current rounds remaining in the magazine.</summary>
    private int currentAmmo;

    /// <summary>
    /// Countdown timer for fire rate. The weapon can only fire when
    /// this reaches zero. After each shot, it resets to 'fireRate'.
    /// </summary>
    private float fireTimer;

    /// <summary>True while the reload coroutine is running.</summary>
    private bool isReloading;

    /// <summary>Reference to the active reload coroutine (so we can cancel it).</summary>
    private Coroutine reloadCoroutine;

    // ──────────────────────────────────────────────
    //  Public Accessors
    // ──────────────────────────────────────────────

    /// <summary>Current ammo in the magazine (read-only).</summary>
    public int CurrentAmmo => currentAmmo;

    /// <summary>Maximum magazine capacity (read-only).</summary>
    public int MaxAmmo => maxAmmo;

    /// <summary>True if the weapon is currently reloading (read-only).</summary>
    public bool IsReloading => isReloading;

    // ──────────────────────────────────────────────
    //  Lifecycle
    // ──────────────────────────────────────────────

    private void Awake()
    {
        // Start with a full magazine.
        currentAmmo = maxAmmo;

        // Build input actions inline — no .inputactions asset required.
        shootAction = new InputAction("Shoot", InputActionType.Button);
        shootAction.AddBinding("<Mouse>/leftButton");
        shootAction.AddBinding("<Gamepad>/rightTrigger");

        reloadAction = new InputAction("Reload", InputActionType.Button);
        reloadAction.AddBinding("<Keyboard>/r");
        reloadAction.AddBinding("<Gamepad>/buttonWest");
    }

    private void Start()
    {
        // Auto-find the camera if none was assigned in the Inspector.
        if (shootOrigin == null)
        {
            Camera cam = GetComponentInChildren<Camera>();
            if (cam != null)
            {
                shootOrigin = cam.transform;
            }
            else
            {
                Debug.LogError("[RaycastShooter] No shoot origin assigned and no Camera found in children. " +
                               "Assign a Camera to the 'shootOrigin' field.", this);
            }
        }

        // Broadcast the initial ammo count so any connected HUD
        // shows the correct value from frame one.
        onAmmoChanged?.Invoke(currentAmmo, maxAmmo);
    }

    private void OnEnable()
    {
        shootAction.Enable();
        reloadAction.Enable();
    }

    private void OnDisable()
    {
        shootAction.Disable();
        reloadAction.Disable();

        // Cancel any in-progress reload when the weapon is disabled
        // (e.g., weapon swap, death). Prevents ghost reloads.
        CancelReload();
    }

    private void Update()
    {
        // --- Fire rate cooldown ---
        // The timer counts down every frame. The weapon can only fire
        // when it reaches zero. This enforces a minimum delay between shots.
        if (fireTimer > 0f)
        {
            fireTimer -= Time.deltaTime;
        }

        // --- Reload input ---
        // Manual reload: press R any time the magazine isn't full.
        if (reloadAction.WasPressedThisFrame() && !isReloading && currentAmmo < maxAmmo)
        {
            StartReload();
        }

        // --- Shoot input ---
        // IsPressed() (not WasPressedThisFrame) enables full-auto:
        // the weapon keeps firing as long as the trigger is held,
        // gated by the fireTimer.
        if (shootAction.IsPressed() && fireTimer <= 0f && !isReloading)
        {
            if (currentAmmo > 0)
            {
                Shoot();
            }
            else
            {
                // Empty magazine — auto-reload on trigger pull.
                // This is standard boomer-shooter UX: the player
                // never has to think about pressing R.
                StartReload();
            }
        }
    }

    // ──────────────────────────────────────────────
    //  Shooting
    // ──────────────────────────────────────────────

    /// <summary>
    /// Consumes one round, resets the fire-rate timer, performs a raycast
    /// from the camera center, and resolves damage through IDamageable.
    /// Draws a debug ray in the Scene view regardless of hit or miss.
    /// </summary>
    private void Shoot()
    {
        if (shootOrigin == null) return;

        // --- Consume ammo and reset fire timer ---
        currentAmmo--;
        fireTimer = fireRate;

        // Notify any subscribed HUD elements.
        onAmmoChanged?.Invoke(currentAmmo, maxAmmo);

        // --- Perform the raycast ---
        Vector3 origin    = shootOrigin.position;
        Vector3 direction = shootOrigin.forward;

        if (Physics.Raycast(origin, direction, out RaycastHit hit, weaponRange))
        {
            // Draw a green ray to the impact point (hit).
            Debug.DrawRay(origin, direction * hit.distance, Color.green, debugRayDuration);

            Debug.Log($"[RaycastShooter] Hit '{hit.collider.gameObject.name}' " +
                      $"at distance {hit.distance:F2}. Ammo: {currentAmmo}/{maxAmmo}", this);

            // --- Damage resolution ---
            // Resolve through the IDamageable interface so this weapon
            // works with Health, shields, or any future damageable type.
            if (hit.collider.TryGetComponent(out IDamageable target))
            {
                target.TakeDamage(weaponDamage);
            }
        }
        else
        {
            // Draw a red ray to max range (miss).
            Debug.DrawRay(origin, direction * weaponRange, Color.red, debugRayDuration);
        }
    }

    // ──────────────────────────────────────────────
    //  Reload
    // ──────────────────────────────────────────────

    /// <summary>
    /// Initiates a reload if one isn't already in progress and the
    /// magazine isn't full. Starts the reload coroutine.
    /// </summary>
    private void StartReload()
    {
        if (isReloading) return;
        if (currentAmmo >= maxAmmo) return;

        Debug.Log($"[RaycastShooter] Reloading... ({reloadTime}s)", this);

        reloadCoroutine = StartCoroutine(ReloadCoroutine());
    }

    /// <summary>
    /// Waits for 'reloadTime' seconds, then refills the magazine.
    /// While running, 'isReloading' is true and the weapon cannot fire.
    ///
    /// Uses a Coroutine instead of a timer in Update because:
    ///   - The logic is sequential (start → wait → finish)
    ///   - It's self-cleaning (no leftover state if interrupted)
    ///   - It can be cancelled cleanly via StopCoroutine
    /// </summary>
    private IEnumerator ReloadCoroutine()
    {
        isReloading = true;

        // Notify HUD / audio — "show the reload bar."
        onReloadStart?.Invoke();

        // Wait for the reload duration.
        yield return new WaitForSeconds(reloadTime);

        // Refill the magazine.
        currentAmmo = maxAmmo;
        isReloading = false;
        reloadCoroutine = null;

        // Notify HUD — "update the ammo counter."
        onAmmoChanged?.Invoke(currentAmmo, maxAmmo);
        onReloadFinish?.Invoke();

        Debug.Log($"[RaycastShooter] Reload complete. Ammo: {currentAmmo}/{maxAmmo}", this);
    }

    /// <summary>
    /// Cancels any in-progress reload. Called when the weapon is
    /// disabled (weapon swap, death) to prevent ghost reloads.
    /// </summary>
    private void CancelReload()
    {
        if (reloadCoroutine != null)
        {
            StopCoroutine(reloadCoroutine);
            reloadCoroutine = null;
        }

        isReloading = false;
    }
}
