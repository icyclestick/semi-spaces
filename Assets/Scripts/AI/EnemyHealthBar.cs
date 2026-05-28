using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Attach to a World-Space health bar above an enemy. Billboards toward
/// the player camera and updates the slider from the parent's Health.
///
/// Setup per enemy prefab:
///   1. Create a child GameObject named "HealthBar".
///   2. Add a Canvas (World Space) — set Width/Height to 200×20,
///      scale to 0.01, position above the enemy (y = 2.5 or higher).
///   3. Under that Canvas, add a Slider (remove Handle Slide Area).
///   4. Set the Slider's Fill Rect to red/green.
///   5. Attach this script to the Slider's GameObject (or the Canvas root).
///   6. Drag the Slider into the Health Slider field.
///   7. Drag the main Camera into Player Camera (or leave empty for auto-find).
///
/// The script finds the Health component on the root enemy (parent of parent).
/// Works regardless of how deep the health bar is nested.
/// </summary>
public class EnemyHealthBar : MonoBehaviour
{
    [Header("References")]
    [SerializeField, Tooltip("The Slider component for the health bar.")]
    private Slider healthSlider;

    [SerializeField, Tooltip("Optional TMP text for current health.")]
    private TMP_Text currentHealthText;

    [SerializeField, Tooltip("Optional TMP text for max health.")]
    private TMP_Text maxHealthText;

    [SerializeField, Tooltip("The player's camera. Auto-found if empty.")]
    private Transform playerCamera;

    [Header("Offset")]
    [SerializeField, Tooltip("World-space Y offset above the enemy's pivot.")]
    private float verticalOffset = 2.5f;

    // ──────────────────────────────────────────────
    //  Cached
    // ──────────────────────────────────────────────

    private Health enemyHealth;
    private Transform rootEnemy;

    // ──────────────────────────────────────────────
    //  Lifecycle
    // ──────────────────────────────────────────────

    private void Start()
    {
        if (playerCamera == null)
        {
            Camera cam = Camera.main;
            if (cam != null) playerCamera = cam.transform;
        }

        // Walk up to find the root enemy (the one with Health + EnemyBase).
        rootEnemy = transform;
        while (rootEnemy != null)
        {
            if (rootEnemy.GetComponent<Health>() != null) break;
            rootEnemy = rootEnemy.parent;
        }

        if (rootEnemy != null)
            enemyHealth = rootEnemy.GetComponent<Health>();

        if (healthSlider == null)
            healthSlider = GetComponentInChildren<Slider>();

        if (enemyHealth != null && healthSlider != null)
        {
            healthSlider.maxValue = enemyHealth.MaxHealth;
            healthSlider.value = enemyHealth.CurrentHealth;
        }
    }

    private void LateUpdate()
    {
        if (rootEnemy == null || enemyHealth == null)
        {
            // Enemy destroyed — destroy the health bar too.
            Destroy(gameObject);
            return;
        }

        // --- Position above the enemy ---
        Vector3 worldPos = rootEnemy.position + Vector3.up * verticalOffset;
        transform.position = worldPos;

        // --- Billboard toward camera ---
        if (playerCamera != null)
        {
            transform.rotation = playerCamera.rotation;
        }

        // --- Update slider ---
        if (healthSlider != null)
        {
            healthSlider.value = enemyHealth.CurrentHealth;
        }

        // --- Update optional texts ---
        if (currentHealthText != null)
            currentHealthText.text = enemyHealth.CurrentHealth.ToString();
        if (maxHealthText != null)
            maxHealthText.text = enemyHealth.MaxHealth.ToString();
    }
}
