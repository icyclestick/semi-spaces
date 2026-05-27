using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

/// <summary>
/// Global game state controller for Semi-Spaces. Implements a thread-safe
/// Singleton pattern for easy access from any script via GameManager.Instance.
///
/// Responsibilities:
///   - Track the current game state (Playing, Paused, GameOver, GameWon).
///   - Listen to the Player's Health.OnDeath → trigger loss state.
///   - Listen to WaveManager's completion → trigger win state.
///   - Control Time.timeScale for pause/resume.
///   - Broadcast UnityEvents for HUD / UI integration.
///
/// This script contains ZERO UI code. It only broadcasts events that
/// Jyesh's HUD scripts can subscribe to in the Inspector.
///
/// Setup:
///   1. Create an empty "GameManager" GameObject in the scene.
///   2. Attach this script.
///   3. Tag the Player as "Player" (auto-found) or assign manually.
///   4. Wire WaveManager's onGameWon → GameManager.CompleteGame() in Inspector.
///   5. Wire UnityEvents to HUD scripts for win/loss/pause screens.
///
/// Integration:
///   - WaveManager: drag GameManager into WaveManager's onGameWon event,
///     select GameManager.CompleteGame().
///   - Player death: auto-detected via Health.OnDeath subscription.
///   - HUD: wire onGameOver, onGameWon, onGamePaused, onGameResumed.
///   - Any script: check GameManager.Instance.CurrentState or
///     GameManager.Instance.IsPlaying.
/// </summary>
public class GameManager : MonoBehaviour
{
    // ──────────────────────────────────────────────
    //  Game States
    // ──────────────────────────────────────────────

    /// <summary>
    /// All possible states the game can be in. Transitions are
    /// one-directional for terminal states (GameOver, GameWon).
    /// </summary>
    public enum GameState
    {
        /// <summary>Normal gameplay — time flows, input is active.</summary>
        Playing,

        /// <summary>Game is paused — Time.timeScale = 0.</summary>
        Paused,

        /// <summary>Player died — terminal state, game is over.</summary>
        GameOver,

        /// <summary>All waves cleared — terminal state, player wins.</summary>
        GameWon
    }

    // ──────────────────────────────────────────────
    //  Singleton
    // ──────────────────────────────────────────────

    /// <summary>
    /// Thread-safe Singleton instance. Accessible globally via
    /// GameManager.Instance. Null-safe — check before using if
    /// the GameManager might not exist in the scene.
    /// </summary>
    public static GameManager Instance { get; private set; }

    // ──────────────────────────────────────────────
    //  Configuration
    // ──────────────────────────────────────────────

    [Header("Player Reference")]
    [SerializeField, Tooltip("The Player's Health component. If left empty, " +
        "auto-found via the 'Player' tag on Awake.")]
    private Health playerHealth;

    [Header("Pause Settings")]
    [SerializeField, Tooltip("If true, Escape key toggles pause during gameplay.")]
    private bool allowPauseInput = true;

    // ──────────────────────────────────────────────
    //  Events (UnityEvent — for Inspector / HUD wiring)
    // ──────────────────────────────────────────────
    //
    //  Wire these to HUD scripts in the Inspector. The GameManager
    //  NEVER updates UI directly — it only fires these events.
    //

    [Header("Events")]
    [SerializeField, Tooltip("Fired when gameplay begins (after initial setup).")]
    private UnityEvent onGameStarted;

    [SerializeField, Tooltip("Fired when the game is paused.")]
    private UnityEvent onGamePaused;

    [SerializeField, Tooltip("Fired when the game is resumed from pause.")]
    private UnityEvent onGameResumed;

    [SerializeField, Tooltip("Fired when the player dies (loss condition).")]
    private UnityEvent onGameOver;

    [SerializeField, Tooltip("Fired when all waves are cleared (win condition).")]
    private UnityEvent onGameWon;

    // ──────────────────────────────────────────────
    //  Internal State
    // ──────────────────────────────────────────────

    /// <summary>The current game state.</summary>
    private GameState currentState = GameState.Playing;

    /// <summary>
    /// Cached timeScale before pausing so we restore the correct value
    /// on resume (supports slow-mo or other timeScale modifications).
    /// </summary>
    private float cachedTimeScale = 1f;

    /// <summary>
    /// Inline InputAction for the Escape key (pause toggle).
    /// Uses the New Input System — legacy Input is disabled in this project.
    /// </summary>
    private InputAction pauseAction;

    // ──────────────────────────────────────────────
    //  Public Accessors
    // ──────────────────────────────────────────────

    /// <summary>The current game state (read-only).</summary>
    public GameState CurrentState => currentState;

    /// <summary>True if the game is in the Playing state (not paused, not over).</summary>
    public bool IsPlaying => currentState == GameState.Playing;

    /// <summary>True if the game is paused.</summary>
    public bool IsPaused => currentState == GameState.Paused;

    /// <summary>True if the game has ended (win or loss).</summary>
    public bool IsGameEnded => currentState == GameState.GameOver ||
                               currentState == GameState.GameWon;

    // ──────────────────────────────────────────────
    //  Lifecycle
    // ──────────────────────────────────────────────

    private void Awake()
    {
        // --- Singleton enforcement ---
        // If another GameManager already exists, destroy this duplicate.
        // DontDestroyOnLoad keeps the manager alive across scene loads.
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[GameManager] Duplicate instance detected. Destroying this one.", this);
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        // --- Build pause input action (New Input System) ---
        pauseAction = new InputAction("Pause", InputActionType.Button);
        pauseAction.AddBinding("<Keyboard>/escape");
        pauseAction.AddBinding("<Gamepad>/start");

        // --- Auto-find player health ---
        if (playerHealth == null)
        {
            GameObject playerObj = GameObject.FindWithTag("Player");
            if (playerObj != null)
            {
                playerHealth = playerObj.GetComponent<Health>();
            }

            if (playerHealth == null)
            {
                Debug.LogWarning("[GameManager] Could not find Player Health component. " +
                                 "Assign it manually or tag the Player as 'Player'.", this);
            }
        }

        // --- Subscribe to player death ---
        if (playerHealth != null)
        {
            playerHealth.OnDeath += HandlePlayerDeath;
        }
    }

    private void Start()
    {
        // Ensure time is running at the start of the game.
        Time.timeScale = 1f;
        currentState = GameState.Playing;

        Debug.Log("[GameManager] Game started.", this);
        onGameStarted?.Invoke();
    }

    private void OnEnable()
    {
        pauseAction?.Enable();
    }

    private void OnDisable()
    {
        pauseAction?.Disable();
    }

    private void Update()
    {
        // --- Pause toggle ---
        // Only allow pause/unpause during active gameplay or while paused.
        // Terminal states (GameOver, GameWon) cannot be unpaused.
        if (allowPauseInput && pauseAction != null && pauseAction.WasPerformedThisFrame())
        {
            if (currentState == GameState.Playing)
            {
                PauseGame();
            }
            else if (currentState == GameState.Paused)
            {
                ResumeGame();
            }
        }
    }

    private void OnDestroy()
    {
        // --- Unsubscribe to prevent leaks ---
        if (playerHealth != null)
        {
            playerHealth.OnDeath -= HandlePlayerDeath;
        }

        // --- Dispose native input action memory ---
        if (pauseAction != null)
        {
            pauseAction.Disable();
            pauseAction.Dispose();
            pauseAction = null;
        }

        // Clear the singleton reference if this is the active instance.
        if (Instance == this)
        {
            Instance = null;
        }
    }

    // ──────────────────────────────────────────────
    //  State Transitions
    // ──────────────────────────────────────────────

    /// <summary>
    /// Pauses the game. Caches the current timeScale and sets it to 0.
    /// Only works from the Playing state.
    /// </summary>
    public void PauseGame()
    {
        if (currentState != GameState.Playing) return;

        cachedTimeScale = Time.timeScale;
        Time.timeScale = 0f;
        currentState = GameState.Paused;

        Debug.Log("[GameManager] Game paused.", this);
        onGamePaused?.Invoke();
    }

    /// <summary>
    /// Resumes the game from pause. Restores the cached timeScale.
    /// Only works from the Paused state.
    /// </summary>
    public void ResumeGame()
    {
        if (currentState != GameState.Paused) return;

        Time.timeScale = cachedTimeScale;
        currentState = GameState.Playing;

        Debug.Log("[GameManager] Game resumed.", this);
        onGameResumed?.Invoke();
    }

    /// <summary>
    /// Triggers the win state. Call this from WaveManager's onGameWon
    /// event in the Inspector, or from any script that determines
    /// the player has won.
    ///
    /// This is a terminal state — the game cannot be unpaused after this.
    /// </summary>
    public void CompleteGame()
    {
        if (IsGameEnded) return;

        currentState = GameState.GameWon;
        Time.timeScale = 0f;

        Debug.Log("[GameManager] All waves cleared — VICTORY!", this);
        onGameWon?.Invoke();
    }

    // ──────────────────────────────────────────────
    //  Internal Handlers
    // ──────────────────────────────────────────────

    /// <summary>
    /// Called when the Player's Health fires OnDeath. Triggers the
    /// game-over (loss) state. This is a terminal state.
    /// </summary>
    private void HandlePlayerDeath()
    {
        if (IsGameEnded) return;

        currentState = GameState.GameOver;
        Time.timeScale = 0f;

        Debug.Log("[GameManager] Player died — GAME OVER.", this);
        onGameOver?.Invoke();
    }

    // ──────────────────────────────────────────────
    //  Utility
    // ──────────────────────────────────────────────

    /// <summary>
    /// Restarts the current scene. Resets timeScale and reloads.
    /// Call from a "Restart" button on the Game Over / Win screen.
    /// </summary>
    public void RestartGame()
    {
        Time.timeScale = 1f;
        UnityEngine.SceneManagement.SceneManager.LoadScene(
            UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex
        );
    }

    /// <summary>
    /// Quits the application. Call from a "Quit" button.
    /// In the editor, stops Play Mode.
    /// </summary>
    public void QuitGame()
    {
        Debug.Log("[GameManager] Quitting game.", this);

        #if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
        #else
        Application.Quit();
        #endif
    }
}
