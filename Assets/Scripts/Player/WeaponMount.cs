using UnityEngine;

/// <summary>
/// Auto-positions a weapon holder at a standard first-person offset
/// relative to the player's camera every frame. No manual tweaking
/// needed — drop a weapon model under the holder and it appears in
/// the correct spot automatically.
///
/// Setup:
///   1. Add this script to the Weapon_Holder GameObject.
///   2. Assign the Camera to the Player Camera field.
///   3. Adjust Hold Offset in the Inspector to taste (default is
///      bottom-right of the screen — standard FPS weapon position).
///   4. All child weapons inherit this position automatically.
/// </summary>
public class WeaponMount : MonoBehaviour
{
    [Header("Anchor")]
    [SerializeField, Tooltip("The player's first-person camera.")]
    private Transform playerCamera;

    [SerializeField, Tooltip("Local offset from the camera. X = right, Y = down, Z = forward.")]
    private Vector3 holdOffset = new Vector3(0.5f, -0.4f, 1.2f);

    [Header("Sway")]
    [SerializeField, Tooltip("How much the weapon sways with mouse look. 0 = rigid, 1 = full.")]
    [Range(0f, 1f)]
    private float lookSway = 0.3f;

    [SerializeField, Tooltip("How much the weapon sways with movement.")]
    [Range(0f, 1f)]
    private float moveSway = 0.15f;

    private Vector3 targetOffset;
    private FirstPersonController fpsController;

    private void Start()
    {
        if (playerCamera == null)
            playerCamera = GetComponentInParent<Camera>()?.transform;

        fpsController = GetComponentInParent<FirstPersonController>();
        targetOffset = holdOffset;
    }

    private void LateUpdate()
    {
        if (playerCamera == null) return;

        // --- Movement sway ---
        if (fpsController != null)
        {
            // Small weapon bob from movement input (separate from headbob).
            float moveBob = Mathf.Sin(Time.time * 8f) * moveSway * 0.02f;
            targetOffset = holdOffset + Vector3.up * moveBob;
        }

        // --- Smooth follow ---
        // Lerp the weapon toward the target so it doesn't snap jarringly.
        Vector3 worldPos = playerCamera.TransformPoint(targetOffset);
        transform.position = Vector3.Lerp(transform.position, worldPos,
                                          Time.deltaTime * 20f);

        // --- Rotation ---
        // The weapon follows the camera rotation, but with optional
        // look-sway damping so it lags slightly behind (feels weighty).
        Quaternion targetRot = playerCamera.rotation;
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
}
