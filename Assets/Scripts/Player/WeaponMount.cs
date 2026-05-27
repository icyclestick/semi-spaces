using UnityEngine;

/// <summary>
/// Auto-positions a weapon holder at a first-person offset relative to
/// the player's camera. Adds equip animation (ease-out rotation + scale)
/// on weapon switch, configurable recoil kickback on fire, and sway
/// multipliers for look + movement.
///
/// Setup:
///   1. Add this script to the Weapon_Holder GameObject.
///   2. Assign the Camera and WeaponManager.
///   3. Tweak values in the Inspector.
/// </summary>
public class WeaponMount : MonoBehaviour
{
    [Header("Anchor")]
    [SerializeField, Tooltip("The player's first-person camera.")]
    private Transform playerCamera;

    [SerializeField, Tooltip("Local offset from the camera.")]
    private Vector3 holdOffset = new Vector3(0.5f, -0.4f, 1.2f);

    [SerializeField, Tooltip("The WeaponManager on this GameObject.")]
    private WeaponManager weaponManager;

    [Header("Sway")]
    [SerializeField, Range(0f, 1f), Tooltip("How much the weapon lags behind mouse look.")]
    private float lookSway = 0.3f;

    [SerializeField, Range(0f, 2f), Tooltip("Multiplier on movement bob.")]
    private float moveSway = 1f;

    [Header("Equip Animation")]
    [SerializeField, Tooltip("How long the scale-in + rotation ease-out lasts (seconds).")]
    private float equipDuration = 0.4f;

    [SerializeField, Tooltip("Upward rotation arc eased out during equip (degrees).")]
    private float equipRotationArc = 20f;

    [Header("Recoil")]
    [SerializeField, Tooltip("How far back the weapon kicks on fire. 0.02 = gentle, 0.12 = heavy.")]
    private float recoilIntensity = 0.1f;

    [SerializeField, Tooltip("Upward rotation per shot (degrees).")]
    private float recoilKickAngle = 8f;

    [SerializeField, Tooltip("How fast recoil recovers. Higher = snappier reset. Lower = floatier.")]
    private float recoilRecoverySpeed = 12f;

    // ──────────────────────────────────────────────
    //  State
    // ──────────────────────────────────────────────

    private FirstPersonController fpsController;
    private RaycastShooter activeShooter;
    private int lastWeaponIndex = -1;
    private bool initialized;

    // Equip
    private float equipTimer;
    private bool isEquipping;

    // Recoil
    private Vector3 recoilPosOffset;
    private float recoilRotOffset;

    // ──────────────────────────────────────────────
    //  Lifecycle
    // ──────────────────────────────────────────────

    private void Start()
    {
        if (playerCamera == null)
            playerCamera = GetComponentInParent<Camera>()?.transform;

        fpsController = GetComponentInParent<FirstPersonController>();

        if (weaponManager == null)
        {
            weaponManager = GetComponent<WeaponManager>();
            if (weaponManager == null)
                weaponManager = GetComponentInParent<WeaponManager>();
            if (weaponManager == null)
                weaponManager = FindObjectOfType<WeaponManager>();
        }

        if (weaponManager != null)
        {
            // Subscribe to weapon-switch event so we don't depend on Update order.
            weaponManager.onWeaponSwitched.AddListener(OnWeaponSwitched);
        }
        else
        {
            Debug.LogWarning("[WeaponMount] WeaponManager not found — weapon switch / recoil disabled.", this);
        }

        // Initial equip + subscribe to starting weapon's shooter.
        TriggerEquip();
        SubscribeToActiveShooter();
        initialized = true;
    }

    private void Update()
    {
        // --- Tick equip animation ---
        if (isEquipping)
        {
            equipTimer += Time.deltaTime / equipDuration;
            if (equipTimer >= 1f)
            {
                equipTimer = 1f;
                isEquipping = false;
            }
        }

        // --- Recover recoil ---
        recoilPosOffset = Vector3.Lerp(recoilPosOffset, Vector3.zero,
                                       Time.deltaTime * recoilRecoverySpeed);
        recoilRotOffset = Mathf.Lerp(recoilRotOffset, 0f,
                                     Time.deltaTime * recoilRecoverySpeed);
    }

    // ──────────────────────────────────────────────
    //  Event Handlers
    // ──────────────────────────────────────────────

    /// <summary>Called by WeaponManager.onWeaponSwitched event.</summary>
    public void OnWeaponSwitched(int newIndex, int totalWeapons)
    {
        // Unsubscribe old shooter.
        if (activeShooter != null)
            activeShooter.onFire.RemoveListener(ApplyRecoil);

        // Subscribe new shooter.
        SubscribeToActiveShooter();

        // Trigger equip animation (skip the initial auto-switch to weapon 0).
        if (lastWeaponIndex >= 0)
            TriggerEquip();

        lastWeaponIndex = newIndex;

        Debug.Log($"[WeaponMount] Switched to weapon {newIndex} " +
                  $"(shooter: {activeShooter != null}).", this);
    }

    private void SubscribeToActiveShooter()
    {
        if (weaponManager == null) return;

        activeShooter = null;
        GameObject currentWeapon = weaponManager.ActiveWeapon;
        if (currentWeapon != null)
        {
            activeShooter = currentWeapon.GetComponentInChildren<RaycastShooter>();
            if (activeShooter != null)
                activeShooter.onFire.AddListener(ApplyRecoil);
        }
    }

    private void LateUpdate()
    {
        if (playerCamera == null) return;

        // --- Equip scale (ease-out from 0 to 1) ---
        float equipScale = 1f;
        float equipRot = 0f;
        if (isEquipping || equipTimer < 1f)
        {
            equipScale = Mathf.SmoothStep(0f, 1f, equipTimer);
            float t = 1f - equipTimer;
            equipRot = t * t * equipRotationArc;
        }
        transform.localScale = Vector3.one * equipScale;

        // --- Movement bob ---
        Vector3 bobOffset = Vector3.zero;
        if (fpsController != null && moveSway > 0f)
        {
            float bob = Mathf.Sin(Time.time * 8f) * 0.01f * moveSway;
            bobOffset = Vector3.up * bob;
        }

        // --- Position: camera offset + bob + recoil ---
        Vector3 totalOffset = holdOffset + bobOffset + recoilPosOffset;
        Vector3 worldPos = playerCamera.TransformPoint(totalOffset);
        transform.position = Vector3.Lerp(transform.position, worldPos,
                                          Time.deltaTime * 25f);

        // --- Rotation: camera + look sway + equip swing + recoil kick ---
        Quaternion cameraRot = playerCamera.rotation;
        Quaternion equipSwing = Quaternion.Euler(-equipRot, 0f, 0f);
        Quaternion recoilKick = Quaternion.Euler(-recoilRotOffset, 0f, 0f);

        Quaternion targetRot = cameraRot * equipSwing * recoilKick;

        if (lookSway > 0f)
        {
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRot,
                                                  Time.deltaTime * (30f / lookSway));
        }
        else
        {
            transform.rotation = targetRot;
        }
    }

    // ──────────────────────────────────────────────
    //  Actions
    // ──────────────────────────────────────────────

    private void TriggerEquip()
    {
        equipTimer = 0f;
        isEquipping = true;
        Debug.Log("[WeaponMount] Equip animation triggered.", this);
    }

    private void ApplyRecoil()
    {
        recoilPosOffset += Vector3.back * recoilIntensity;
        recoilRotOffset += recoilKickAngle;

        recoilPosOffset = Vector3.ClampMagnitude(recoilPosOffset, recoilIntensity * 3f);
        recoilRotOffset = Mathf.Min(recoilRotOffset, recoilKickAngle * 3f);

        Debug.Log($"[WeaponMount] Recoil applied — posOffset: {recoilPosOffset.z:F3}, rotOffset: {recoilRotOffset:F1}°");
    }
}
