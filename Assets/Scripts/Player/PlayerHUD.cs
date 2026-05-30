using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Drives the player HUD — crosshair in screen center, health bar,
/// and any additional UI elements. Attach to the Canvas GameObject.
///
/// Setup in Unity:
///   1. Right-click in Hierarchy → UI → Canvas. Name it "PlayerHUD".
///   2. On the Canvas, set Render Mode = Screen Space - Overlay.
///   3. Right-click the Canvas → UI → Image. Name it "Crosshair".
///      - Set its RectTransform anchor to center (Alt+Shift click center).
///      - Set Width/Height to your desired crosshair size (e.g. 32×32).
///      - Drag your crosshair SVG/PNG into the Source Image field.
///      - Set Color to white (or green if you want a tint).
///   4. Right-click the Canvas → UI → Slider. Name it "HealthBar".
///      - Set anchor to top-left or top-center.
///      - Remove the Handle Slide Area (delete the child).
///      - Set Fill Area's Fill Rect color to red/green.
///      - Set Min Value = 0, Max Value = 100 (or your max health).
///   5. Drag this script onto the Canvas.
///   6. Drag the Crosshair Image into the Crosshair field.
///   7. Drag the HealthBar Slider into the Health Bar field.
///   8. (Optional) Drag the player GameObject into the Player field,
///      or leave empty to auto-find via tag "Player".
/// </summary>
public class PlayerHUD : MonoBehaviour
{
    [Header("UI Elements")]
    [SerializeField, Tooltip("The crosshair Image in the center of the screen.")]
    private Image crosshair;

    [SerializeField, Tooltip("The health bar Slider.")]
    private Slider healthBar;

    [SerializeField, Tooltip("TextMeshProUGUI that shows 'HP: 100/100'.")]
    private TMP_Text healthText;

    [SerializeField, Tooltip("TextMeshProUGUI that shows 'Enemies Left: 42'.")]
    private TMP_Text enemyCountText;

    [Header("Target")]
    [SerializeField, Tooltip("The player GameObject. Leave empty to auto-find by tag 'Player'.")]
    private GameObject player;

    [SerializeField, Tooltip("The EnemySpawner for enemy count display.")]
    private EnemySpawner enemySpawner;

    [SerializeField, Tooltip("All WeaponMount components (one per weapon child).")]
    private WeaponMount[] weaponMounts;

    [Header("Reload UI")]
    [SerializeField, Tooltip("Slider showing reload progress (0–1). Hidden when not reloading.")]
    private Slider reloadSlider;

    [SerializeField, Tooltip("TMP text showing reload seconds remaining.")]
    private TMP_Text reloadText;

    // ──────────────────────────────────────────────
    //  State
    // ──────────────────────────────────────────────

    private Health playerHealth;

    // ──────────────────────────────────────────────
    //  Lifecycle
    // ──────────────────────────────────────────────

    private void Start()
    {
        if (player == null)
            player = GameObject.FindGameObjectWithTag("Player");

        if (player != null)
            playerHealth = player.GetComponent<Health>();

    }

    private void Update()
    {
        // --- Health bar + text ---
        if (playerHealth != null)
        {
            if (healthBar != null)
            {
                healthBar.maxValue = playerHealth.MaxHealth;
                healthBar.value = playerHealth.CurrentHealth;
            }
            if (healthText != null)
                healthText.text = $"{playerHealth.CurrentHealth}";
        }

        // --- Enemy count (reads childCount directly — no polling needed) ---
        if (enemySpawner != null && enemyCountText != null)
        {
            enemyCountText.text = $"{enemySpawner.AliveCount}";
        }
        else if (enemyCountText != null && enemySpawner == null)
        {
            enemyCountText.text = "0";
        }

        // --- Reload indicator (checks all weapon mounts) ---
        WeaponMount reloadingMount = null;
        if (weaponMounts != null)
        {
            foreach (var wm in weaponMounts)
            {
                if (wm != null && wm.isActiveAndEnabled && wm.IsReloading)
                {
                    reloadingMount = wm;
                    break;
                }
            }
        }

        if (reloadSlider != null)
        {
            reloadSlider.gameObject.SetActive(reloadingMount != null);
            if (reloadingMount != null)
                reloadSlider.value = reloadingMount.ReloadProgress;
        }
        if (reloadText != null)
        {
            reloadText.gameObject.SetActive(reloadingMount != null);
            if (reloadingMount != null)
                reloadText.text = reloadingMount.ReloadTimeRemaining.ToString("F1");
        }
    }

    // ──────────────────────────────────────────────
    //  Public API (for future extensions)
    // ──────────────────────────────────────────────

    /// <summary>Shows or hides the crosshair.</summary>
    public void SetCrosshairVisible(bool visible)
    {
        if (crosshair != null)
            crosshair.enabled = visible;
    }
}
