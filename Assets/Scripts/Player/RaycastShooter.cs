using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// A hitscan raycast weapon that fires from the player's camera on left
/// mouse click. Resolves damage through the IDamageable interface so it
/// works with any damageable entity. Uses the New Input System (inline
/// InputAction workflow).
///
/// Setup:
///   1. Attach this script to the Player GameObject (alongside FirstPersonController).
///   2. Assign the child Camera to the 'shootOrigin' field in the Inspector.
///   3. Tune weapon range and damage to taste.
///   4. Ensure targets have a Collider and an IDamageable component (e.g. Health).
///   5. Input actions are self-contained — no InputActions asset required.
/// </summary>
public class RaycastShooter : MonoBehaviour
{
    // ──────────────────────────────────────────────
    //  Weapon Configuration
    // ──────────────────────────────────────────────

    [Header("Weapon")]
    [SerializeField, Tooltip("Maximum distance the weapon's raycast can travel (in units).")]
    private float weaponRange = 50f;

    [SerializeField, Tooltip("Damage dealt to an IDamageable target per shot.")]
    private int weaponDamage = 10;

    // ──────────────────────────────────────────────
    //  References
    // ──────────────────────────────────────────────

    [Header("References")]
    [SerializeField, Tooltip("The Camera transform from which the ray is cast (should be the player's FPS camera).")]
    private Transform shootOrigin;

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

    // ──────────────────────────────────────────────
    //  Lifecycle
    // ──────────────────────────────────────────────

    private void Awake()
    {
        // Build the shoot action inline — no .inputactions asset required.
        shootAction = new InputAction("Shoot", InputActionType.Button);
        shootAction.AddBinding("<Mouse>/leftButton");
        shootAction.AddBinding("<Gamepad>/rightTrigger");
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
    }

    private void OnEnable()
    {
        // Input actions must be explicitly enabled to receive input.
        shootAction.Enable();
    }

    private void OnDisable()
    {
        // Disable the action when the component is turned off to prevent
        // ghost input and allow proper cleanup.
        shootAction.Disable();
    }

    private void Update()
    {
        // Fire on the frame the shoot button is pressed.
        if (shootAction.WasPressedThisFrame())
        {
            Shoot();
        }
    }

    // ──────────────────────────────────────────────
    //  Shooting
    // ──────────────────────────────────────────────

    /// <summary>
    /// Casts a ray forward from the camera's center. If it hits a
    /// Collider with an IDamageable component, applies weapon damage.
    /// Draws a debug ray in the Scene view regardless of hit or miss.
    /// </summary>
    private void Shoot()
    {
        if (shootOrigin == null) return;

        Vector3 origin    = shootOrigin.position;
        Vector3 direction = shootOrigin.forward;

        // --- Perform the raycast ---
        if (Physics.Raycast(origin, direction, out RaycastHit hit, weaponRange))
        {
            // Draw a green ray to the impact point (hit).
            Debug.DrawRay(origin, direction * hit.distance, Color.green, debugRayDuration);

            Debug.Log($"[RaycastShooter] Hit '{hit.collider.gameObject.name}' " +
                      $"at distance {hit.distance:F2}.", this);

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
}
