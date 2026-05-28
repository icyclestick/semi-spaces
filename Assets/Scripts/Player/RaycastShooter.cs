using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.Pool;

/// <summary>
/// A data-driven hitscan weapon system that supports both single-shot
/// (rifle) and multi-pellet (shotgun) configurations. All behaviour is
/// controlled by Inspector values — no code changes needed to switch
/// weapon types.
///
/// Resolves damage through the IDamageable interface so it works with
/// any damageable entity. Uses the New Input System (inline InputAction
/// workflow).
///
/// Setup:
///   1. Attach this script to the Player GameObject (alongside FirstPersonController).
///   2. Assign the child Camera to the 'shootOrigin' field in the Inspector.
///   3. Create a child GameObject on your weapon model for 'gunBarrel' (muzzle point).
///   4. (Optional) Assign a 'trailMaterial' for visual bullet trails.
///   5. Tune weapon stats in the Inspector to configure as rifle or shotgun.
///   6. Ensure targets have a Collider and an IDamageable component (e.g. Health).
///   7. (Optional) Wire the UnityEvents for HUD integration.
///   8. Input actions are self-contained — no InputActions asset required.
///
/// Quick Presets (set these in the Inspector):
///   RIFLE:    pelletsPerShot = 1,  spreadAngle = 0,   damage = 10, fireRate = 0.15
///   SHOTGUN:  pelletsPerShot = 8,  spreadAngle = 5,   damage = 5,  fireRate = 0.8
///   SMG:      pelletsPerShot = 1,  spreadAngle = 2,   damage = 6,  fireRate = 0.08
/// </summary>
public class RaycastShooter : MonoBehaviour
{
    // ──────────────────────────────────────────────
    //  Weapon Stats
    // ──────────────────────────────────────────────

    [Header("Weapon Stats")]
    [SerializeField, Tooltip("Damage dealt to an IDamageable target per pellet.")]
    private int weaponDamage = 10;

    [SerializeField, Tooltip("Maximum distance the weapon's raycast can travel (in units).")]
    private float weaponRange = 50f;

    [SerializeField, Tooltip("Minimum time between shots (in seconds). Lower = faster fire. 0.1 = 10 rounds/sec.")]
    private float fireRate = 0.15f;

    [SerializeField, Tooltip("Number of pellets fired per trigger pull. 1 = rifle, 8+ = shotgun.")]
    private int pelletsPerShot = 1;

    [SerializeField, Tooltip("Maximum random spread angle in degrees per pellet. 0 = perfect accuracy (rifle).")]
    private float spreadAngle = 0f;

    // ──────────────────────────────────────────────
    //  Ammo
    // ──────────────────────────────────────────────

    [Header("Ammo")]
    [SerializeField, Tooltip("Maximum rounds in a full magazine.")]
    private int maxAmmo = 30;

    [SerializeField, Tooltip("Time in seconds it takes to complete a reload.")]
    public float reloadTime = 1.5f;

    // ──────────────────────────────────────────────
    //  References
    // ──────────────────────────────────────────────

    [Header("References")]
    [SerializeField, Tooltip("The Camera transform from which the ray is cast (should be the player's FPS camera).")]
    private Transform shootOrigin;

    // ──────────────────────────────────────────────
    //  Visuals
    // ──────────────────────────────────────────────
    //
    //  HOW THE MULTI-PELLET TRAIL SYSTEM WORKS:
    //  Each pellet spawns its own temporary GameObject with a
    //  LineRenderer attached. The line is drawn from the gun barrel
    //  to the pellet's hit point (or max range). The GameObject
    //  self-destructs after 'trailFlashDuration' seconds via
    //  Destroy(obj, delay).
    //
    //  This approach works for ANY pellet count:
    //    - Rifle (1 pellet):  spawns 1 trail
    //    - Shotgun (8 pellets): spawns 8 trails simultaneously
    //
    //  Because each trail is its own object, there are no conflicts
    //  between overlapping shots or rapid fire — every trail is
    //  independent and self-cleaning.
    //

    [Header("Visuals")]
    [SerializeField, Tooltip("The muzzle point where trails visually originate. If unassigned, falls back to shootOrigin.")]
    private Transform gunBarrel;

    [SerializeField, Tooltip("Material applied to bullet trail LineRenderers. If null, trails use the default material.")]
    private Material trailMaterial;

    [SerializeField, Tooltip("Start width of the bullet trail line.")]
    private float trailStartWidth = 0.03f;

    [SerializeField, Tooltip("End width of the bullet trail line (tapers to this).")]
    private float trailEndWidth = 0.01f;

    [SerializeField, Tooltip("Color of the bullet trail.")]
    private Color trailColor = new Color(1f, 0.85f, 0.2f, 1f); // Warm yellow.

    [SerializeField, Tooltip("How long each trail stays visible (in seconds). Keep very short for a hitscan flash.")]
    private float trailFlashDuration = 0.05f;

    // ──────────────────────────────────────────────
    //  Audio
    // ──────────────────────────────────────────────

    [Header("Audio")]
    [SerializeField, Tooltip("Sound played when the weapon fires.")]
    public AudioClip fireClip;

    [SerializeField, Tooltip("How much the pitch randomly varies per shot. 0 = no variation, 0.1 = ±10%.")]
    public float pitchVariance = 0.05f;

    // ──────────────────────────────────────────────
    //  Hit Sparks (Object Pooled)
    // ──────────────────────────────────────────────
    //
    //  HOW THE HIT SPARK POOL WORKS:
    //  Instead of Instantiate/Destroy on every hit (which creates
    //  garbage for the GC to collect, causing frame spikes at high
    //  fire rates), we use Unity's built-in ObjectPool.
    //
    //  On first fire:
    //    Pool.Get() → no instances exist → pool calls CreateSpark()
    //    → Instantiate the prefab ONCE and cache it.
    //
    //  On subsequent fires:
    //    Pool.Get() → reuses a dormant instance → repositions it
    //    → plays the ParticleSystem.
    //
    //  When the particle finishes playing:
    //    ReturnToPool.cs calls Pool.Release() → the instance is
    //    deactivated and returned to the pool for reuse.
    //
    //  Result: after a brief warm-up, ZERO allocations during combat.
    //

    [Header("Hit Sparks")]
    [SerializeField, Tooltip("Particle prefab spawned at hit points. Must have ReturnToPool.cs attached and Stop Action = Callback.")]
    private ParticleSystem hitSparkPrefab;

    [SerializeField, Tooltip("Default pool size. Pre-warms this many instances on Awake.")]
    private int poolDefaultSize = 10;

    [SerializeField, Tooltip("Maximum pool size. Extra instances beyond this are destroyed instead of returned.")]
    private int poolMaxSize = 30;

    // ──────────────────────────────────────────────
    //  Events (UnityEvent — for Inspector / HUD wiring)
    // ──────────────────────────────────────────────
    //
    //  These events let the UI designer build a HUD without touching
    //  this script. Wire them in the Inspector to update ammo counters,
    //  show reload indicators, play animations, etc.
    //

    [Header("Events")]
    [Tooltip("Fired whenever the ammo count changes. Passes (currentAmmo, maxAmmo).")]
    public UnityEvent<int, int> onAmmoChanged;

    [Tooltip("Fired when a reload begins. Use to trigger HUD animations or sound.")]
    public UnityEvent onReloadStart;

    [Tooltip("Fired when a reload completes.")]
    public UnityEvent onReloadFinish;

    [Tooltip("Fired every time the weapon fires (before pellets). Use for recoil / muzzle flash.")]
    public UnityEvent onFire;

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

    /// <summary>
    /// The ObjectPool that manages hit spark ParticleSystem instances.
    /// Initialised in Awake if a hitSparkPrefab is assigned.
    /// </summary>
    private ObjectPool<ParticleSystem> sparkPool;

    /// <summary>
    /// Cached fallback material for bullet trails when no trailMaterial
    /// is assigned. Created once to avoid per-pellet Material allocations.
    /// </summary>
    private Material cachedTrailMaterial;

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

        // Ensure UnityEvents are initialized (they can be null from old serialization).
        onReloadStart ??= new UnityEngine.Events.UnityEvent();
        onReloadFinish ??= new UnityEngine.Events.UnityEvent();
        onFire ??= new UnityEngine.Events.UnityEvent();
        onAmmoChanged ??= new UnityEngine.Events.UnityEvent<int, int>();

        // Build input actions inline — no .inputactions asset required.
        shootAction = new InputAction("Shoot", InputActionType.Button);
        shootAction.AddBinding("<Mouse>/leftButton");
        shootAction.AddBinding("<Gamepad>/rightTrigger");

        reloadAction = new InputAction("Reload", InputActionType.Button);
        reloadAction.AddBinding("<Keyboard>/r");
        reloadAction.AddBinding("<Gamepad>/buttonWest");

        // --- Initialise the hit spark pool ---
        // Only create the pool if a prefab is assigned. The weapon
        // functions perfectly without sparks — they're visual-only.
        if (hitSparkPrefab != null)
        {
            sparkPool = new ObjectPool<ParticleSystem>(
                createFunc:      CreateSpark,
                actionOnGet:     OnGetSpark,
                actionOnRelease: OnReleaseSpark,
                actionOnDestroy: OnDestroySpark,
                collectionCheck: false,
                defaultCapacity: poolDefaultSize,
                maxSize:         poolMaxSize
            );

            // --- Pre-warm the pool ---
            // defaultCapacity only sets the backing collection size.
            // We must actually instantiate objects so they're ready
            // for the first shots — no allocation spike on first fire.
            ParticleSystem[] prewarm = new ParticleSystem[poolDefaultSize];
            for (int i = 0; i < poolDefaultSize; i++)
            {
                prewarm[i] = sparkPool.Get();
            }
            for (int i = 0; i < prewarm.Length; i++)
            {
                sparkPool.Release(prewarm[i]);
            }
        }
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

        // Fall back to shootOrigin if no gunBarrel was assigned.
        if (gunBarrel == null)
        {
            gunBarrel = shootOrigin;
        }

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

    /// <summary>
    /// Disposes InputActions and cleans up the spark pool on destruction.
    /// Without this, inline InputActions leak native memory.
    /// </summary>
    private void OnDestroy()
    {
        // --- Dispose input actions ---
        shootAction?.Dispose();
        reloadAction?.Dispose();

        // --- Clean up pooled sparks ---
        // ObjectPool.Clear() invokes actionOnDestroy for each pooled
        // instance, which calls Destroy(gameObject) on them.
        sparkPool?.Clear();

        // --- Clean up cached trail material ---
        if (cachedTrailMaterial != null)
        {
            Destroy(cachedTrailMaterial);
        }
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
    /// Consumes ONE round (regardless of pellet count), resets the
    /// fire-rate timer, then fires 'pelletsPerShot' individual rays
    /// with randomized spread. Each pellet independently resolves
    /// damage through IDamageable.
    /// </summary>
    private void Shoot()
    {
        if (shootOrigin == null) return;

        // --- Consume ammo and reset fire timer ---
        // One trigger pull = one round consumed, even for a shotgun
        // that sprays 8 pellets. This is standard FPS convention.
        currentAmmo--;
        fireTimer = fireRate;

        // Notify any subscribed HUD elements.
        onAmmoChanged?.Invoke(currentAmmo, maxAmmo);

        // Notify recoil / muzzle-flash subscribers.
        onFire?.Invoke();

        // --- Fire each pellet ---
        Vector3 origin      = shootOrigin.position;
        Vector3 baseForward = shootOrigin.forward;

        for (int i = 0; i < pelletsPerShot; i++)
        {
            // Calculate a randomised direction for this pellet.
            // For rifles (spreadAngle = 0), this returns baseForward exactly.
            Vector3 pelletDirection = GetSpreadDirection(baseForward);

            FirePellet(origin, pelletDirection);
        }
    }

    /// <summary>
    /// Fires a single pellet ray in the given direction, resolves
    /// damage through IDamageable, and spawns a visual trail.
    /// </summary>
    /// <param name="origin">World-space origin of the ray (camera position).</param>
    /// <param name="direction">Direction this pellet travels (includes spread).</param>
    private void FirePellet(Vector3 origin, Vector3 direction)
    {
        if (Physics.Raycast(origin, direction, out RaycastHit hit, weaponRange))
        {
            // Draw a green debug ray to the impact point (hit).
            Debug.DrawRay(origin, direction * hit.distance, Color.green, debugRayDuration);

            // --- Damage resolution ---
            // Resolve through the IDamageable interface so this weapon
            // works with Health, shields, or any future damageable type.
            if (hit.collider.TryGetComponent(out IDamageable target))
            {
                target.TakeDamage(weaponDamage);
            }

            // --- Visual trail (hit) ---
            SpawnTrail(hit.point);

            // --- Hit spark (pooled) ---
            SpawnHitSpark(hit.point, hit.normal);
        }
        else
        {
            // Draw a red debug ray to max range (miss).
            Debug.DrawRay(origin, direction * weaponRange, Color.red, debugRayDuration);

            // --- Visual trail (miss) ---
            SpawnTrail(origin + direction * weaponRange);
        }
    }

    // ──────────────────────────────────────────────
    //  Spread Calculation
    // ──────────────────────────────────────────────

    /// <summary>
    /// Generates a randomised direction within a cone defined by
    /// 'spreadAngle' around the base forward vector.
    ///
    /// How the math works:
    ///   1. Pick a random point inside a unit circle (Random.insideUnitCircle).
    ///   2. Scale it by tan(spreadAngle) — this maps the flat circle
    ///      onto the surface of a cone at distance 1.
    ///   3. Add this offset to the forward vector and normalize.
    ///
    /// The result is a direction that deviates from forward by at most
    /// 'spreadAngle' degrees, with uniform random distribution within
    /// the cone. For spreadAngle = 0, the direction is exactly forward.
    /// </summary>
    /// <param name="forward">The base aim direction (camera forward).</param>
    /// <returns>A direction vector randomised within the spread cone.</returns>
    private Vector3 GetSpreadDirection(Vector3 forward)
    {
        // No spread — return the exact aim direction (rifle mode).
        if (spreadAngle <= 0f) return forward;

        // Generate a random offset within a cone.
        // insideUnitCircle gives us a random 2D point, which we project
        // onto the camera's right and up axes to create a 3D offset.
        Vector2 randomCircle = Random.insideUnitCircle;
        float   spreadRad    = Mathf.Tan(spreadAngle * Mathf.Deg2Rad);

        Vector3 right = shootOrigin.right;
        Vector3 up    = shootOrigin.up;

        Vector3 spread = forward
                       + right * (randomCircle.x * spreadRad)
                       + up    * (randomCircle.y * spreadRad);

        return spread.normalized;
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

    // ──────────────────────────────────────────────
    //  Bullet Trail (Disposable Object per Pellet)
    // ──────────────────────────────────────────────

    /// <summary>
    /// Spawns a temporary GameObject with a LineRenderer that draws a
    /// trail from the gun barrel to the given endpoint. The object
    /// self-destructs after 'trailFlashDuration' seconds.
    ///
    /// This replaces the old single-LineRenderer approach. Because each
    /// pellet gets its own trail object, shotguns (multi-pellet) and
    /// rapid-fire weapons work without any trail conflicts.
    /// </summary>
    /// <param name="endPoint">World-space position where the trail terminates.</param>
    private void SpawnTrail(Vector3 endPoint)
    {
        if (gunBarrel == null) return;

        // --- Create a temporary trail object ---
        // We build this from scratch in code so the weapon script is
        // fully self-contained — no prefab required.
        GameObject trailObj = new GameObject("BulletTrail");

        LineRenderer line = trailObj.AddComponent<LineRenderer>();

        // Configure the line's appearance.
        line.positionCount = 2;
        line.SetPosition(0, gunBarrel.position);
        line.SetPosition(1, endPoint);

        line.startWidth = trailStartWidth;
        line.endWidth   = trailEndWidth;

        // Apply material. If no material is assigned, use a shared
        // cached fallback so we don't allocate a new Material per pellet.
        if (trailMaterial != null)
        {
            line.material = trailMaterial;
        }
        else
        {
            if (cachedTrailMaterial == null)
            {
                cachedTrailMaterial = new Material(Shader.Find("Sprites/Default"));
            }
            line.material = cachedTrailMaterial;
        }

        line.startColor = trailColor;
        line.endColor   = trailColor;

        // Disable shadow casting — trails are UI/effect elements.
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows    = false;

        // --- Self-destruct ---
        // The trail object automatically destroys itself after the
        // flash duration. No cleanup code or coroutine management needed.
        Destroy(trailObj, trailFlashDuration);
    }

    // ──────────────────────────────────────────────
    //  Hit Spark Pool
    // ──────────────────────────────────────────────

    /// <summary>
    /// Pulls a spark from the pool, positions it at the hit point,
    /// rotates it to face along the surface normal, and plays it.
    /// If no prefab is assigned, this method silently skips.
    /// </summary>
    /// <param name="point">World-space position of the impact.</param>
    /// <param name="normal">Surface normal at the impact point.</param>
    private void SpawnHitSpark(Vector3 point, Vector3 normal)
    {
        if (sparkPool == null) return;

        ParticleSystem spark = sparkPool.Get();

        // Position at the hit point, rotated so the particles
        // spray outward along the surface normal.
        spark.transform.position = point;
        spark.transform.rotation = Quaternion.LookRotation(normal);

        spark.Play();
    }

    // --- Pool callbacks ---
    // These four methods are passed to the ObjectPool constructor.
    // They define what happens when the pool creates, retrieves,
    // returns, or discards an instance.

    /// <summary>
    /// Called by the pool when it needs a NEW instance (pool is empty).
    /// Instantiates the prefab and wires up ReturnToPool.
    /// </summary>
    private ParticleSystem CreateSpark()
    {
        ParticleSystem instance = Instantiate(hitSparkPrefab);

        // Wire up the ReturnToPool helper so the particle returns
        // itself to the pool when it finishes playing.
        ReturnToPool returnScript = instance.GetComponent<ReturnToPool>();
        if (returnScript == null)
        {
            returnScript = instance.gameObject.AddComponent<ReturnToPool>();
        }
        returnScript.Pool = sparkPool;

        return instance;
    }

    /// <summary>
    /// Called by the pool when Get() retrieves an existing instance.
    /// Activates the GameObject so it's visible and can play.
    /// </summary>
    private void OnGetSpark(ParticleSystem spark)
    {
        spark.gameObject.SetActive(true);
    }

    /// <summary>
    /// Called by the pool when Release() returns an instance.
    /// Stops the particle and deactivates the GameObject.
    /// </summary>
    private void OnReleaseSpark(ParticleSystem spark)
    {
        spark.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        spark.gameObject.SetActive(false);
    }

    /// <summary>
    /// Called by the pool when an instance exceeds maxSize and must
    /// be permanently destroyed (overflow cleanup). Also called during
    /// sparkPool.Clear() in OnDestroy — at that point, Unity may have
    /// already destroyed some instances, so we null-check first.
    /// </summary>
    private void OnDestroySpark(ParticleSystem spark)
    {
        if (spark != null)
        {
            Destroy(spark.gameObject);
        }
    }
}
