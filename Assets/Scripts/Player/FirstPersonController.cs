using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// A clean, self-contained first-person controller using Unity's built-in
/// CharacterController. Handles WASD movement, gravity, jumping, and
/// mouse-look rotation via the New Input System (InputAction workflow).
///
/// Setup:
///   1. Attach this script to your Player GameObject.
///   2. Ensure a CharacterController component is also on the same GameObject.
///   3. Place a Camera as a child of the Player and assign it to the
///      'playerCamera' field in the Inspector.
///   4. Tune speed, jump height, gravity, and sensitivity to taste.
///   5. Input actions are self-contained — no InputActions asset required.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class FirstPersonController : MonoBehaviour
{
    // ──────────────────────────────────────────────
    //  Movement
    // ──────────────────────────────────────────────

    [Header("Movement")]
    [SerializeField, Tooltip("Horizontal movement speed in units per second.")]
    private float moveSpeed = 6f;

    [SerializeField, Tooltip("Instantaneous upward velocity applied on jump.")]
    private float jumpHeight = 1.4f;

    [SerializeField, Tooltip("Downward acceleration (positive value). ~9.81 for Earth gravity.")]
    private float gravity = 20f;

    // ──────────────────────────────────────────────
    //  Ground Check
    // ──────────────────────────────────────────────

    [Header("Ground Check")]
    [SerializeField, Tooltip("Radius of the sphere cast used for ground detection. Should roughly match the CharacterController radius.")]
    private float groundCheckRadius = 0.3f;

    [SerializeField, Tooltip("Downward offset from the transform for the ground-check sphere origin.")]
    private float groundCheckDistance = 0.15f;

    [SerializeField, Tooltip("Layers considered as walkable ground. Leave as 'Everything' if unsure.")]
    private LayerMask groundLayer = ~0; // Default: everything.

    // ──────────────────────────────────────────────
    //  Mouse Look
    // ──────────────────────────────────────────────

    [Header("Mouse Look")]
    [SerializeField, Tooltip("Mouse sensitivity multiplier.")]
    private float mouseSensitivity = 2f;

    [SerializeField, Tooltip("Reference to the child Camera transform used for vertical look.")]
    private Transform playerCamera;

    // ──────────────────────────────────────────────
    //  Input Actions
    // ──────────────────────────────────────────────

    /// <summary>WASD / Left Stick — returns a Vector2 for horizontal movement.</summary>
    private InputAction moveAction;

    /// <summary>Mouse Delta / Right Stick — returns a Vector2 for look rotation.</summary>
    private InputAction lookAction;

    /// <summary>Space / South Button — triggers a jump.</summary>
    private InputAction jumpAction;

    // ──────────────────────────────────────────────
    //  Internal State
    // ──────────────────────────────────────────────

    private CharacterController controller;

    /// <summary>Accumulated vertical velocity (gravity + jump impulse).</summary>
    private float verticalVelocity;

    /// <summary>Current pitch angle for camera look clamping.</summary>
    private float cameraPitch;

    /// <summary>Cached move input read each frame to avoid repeated ReadValue calls.</summary>
    private Vector2 moveInput;

    /// <summary>Cached look input read each frame.</summary>
    private Vector2 lookInput;

    /// <summary>True during the frame the jump button was pressed.</summary>
    private bool jumpPressed;

    // ──────────────────────────────────────────────
    //  Lifecycle
    // ──────────────────────────────────────────────

    private void Awake()
    {
        // Cache the CharacterController reference.
        controller = GetComponent<CharacterController>();

        // --- Build input actions inline ---
        // This approach is fully self-contained: no .inputactions asset or
        // generated C# class is needed. Each action defines its own bindings.

        moveAction = new InputAction("Move", InputActionType.Value);
        moveAction.AddCompositeBinding("2DVector")
            .With("Up",    "<Keyboard>/w")
            .With("Down",  "<Keyboard>/s")
            .With("Left",  "<Keyboard>/a")
            .With("Right", "<Keyboard>/d");
        moveAction.AddCompositeBinding("2DVector")
            .With("Up",    "<Keyboard>/upArrow")
            .With("Down",  "<Keyboard>/downArrow")
            .With("Left",  "<Keyboard>/leftArrow")
            .With("Right", "<Keyboard>/rightArrow");
        // Gamepad left stick support.
        moveAction.AddBinding("<Gamepad>/leftStick");

        lookAction = new InputAction("Look", InputActionType.Value);
        lookAction.AddBinding("<Mouse>/delta");
        // Gamepad right stick support.
        lookAction.AddBinding("<Gamepad>/rightStick")
            .WithProcessor("ScaleVector2(x=150,y=150)");

        jumpAction = new InputAction("Jump", InputActionType.Button);
        jumpAction.AddBinding("<Keyboard>/space");
        jumpAction.AddBinding("<Gamepad>/buttonSouth");
    }

    private void Start()
    {
        // Lock and hide the cursor for an immersive first-person experience.
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible   = false;

        // Auto-find the camera if none was assigned in the Inspector.
        if (playerCamera == null)
        {
            Camera cam = GetComponentInChildren<Camera>();
            if (cam != null)
            {
                playerCamera = cam.transform;
            }
            else
            {
                Debug.LogError("[FirstPersonController] No camera assigned and none found in children. " +
                               "Assign a Camera to the 'playerCamera' field.", this);
            }
        }
    }

    private void OnEnable()
    {
        // Input actions must be explicitly enabled to receive input.
        moveAction.Enable();
        lookAction.Enable();
        jumpAction.Enable();
    }

    private void OnDisable()
    {
        // Disable actions when the component is turned off to prevent
        // ghost input and allow proper cleanup.
        moveAction.Disable();
        lookAction.Disable();
        jumpAction.Disable();
    }

    private void Update()
    {
        // Read all input once per frame to keep handler methods clean.
        ReadInput();

        HandleMouseLook();
        HandleMovement();
    }

    // ──────────────────────────────────────────────
    //  Input Reading
    // ──────────────────────────────────────────────

    /// <summary>
    /// Reads and caches all input action values for the current frame.
    /// Centralised here so each handler doesn't call ReadValue independently.
    /// </summary>
    private void ReadInput()
    {
        moveInput   = moveAction.ReadValue<Vector2>();
        lookInput   = lookAction.ReadValue<Vector2>();
        jumpPressed = jumpAction.WasPressedThisFrame();
    }

    // ──────────────────────────────────────────────
    //  Mouse Look
    // ──────────────────────────────────────────────

    /// <summary>
    /// Applies yaw rotation to the player body and pitch rotation to the
    /// child camera using the look input delta, clamped to ±90°.
    /// </summary>
    private void HandleMouseLook()
    {
        if (playerCamera == null) return;

        // Scale the raw delta by sensitivity.
        float lookX = lookInput.x * mouseSensitivity * Time.deltaTime;
        float lookY = lookInput.y * mouseSensitivity * Time.deltaTime;

        // Horizontal rotation — rotate the entire player body around Y.
        transform.Rotate(Vector3.up, lookX);

        // Vertical rotation — tilt the camera up/down, clamped so we
        // can't flip upside-down.
        cameraPitch -= lookY;
        cameraPitch  = Mathf.Clamp(cameraPitch, -90f, 90f);
        playerCamera.localRotation = Quaternion.Euler(cameraPitch, 0f, 0f);
    }

    // ──────────────────────────────────────────────
    //  Movement & Gravity
    // ──────────────────────────────────────────────

    /// <summary>
    /// Uses cached move input, applies gravity, handles jumping,
    /// and moves the CharacterController in a single Move call.
    /// </summary>
    private void HandleMovement()
    {
        // --- Ground detection ---
        // Use Physics.CheckSphere for reliable ground detection instead
        // of CharacterController.isGrounded, which only reports true if
        // the LAST Move() call caused a ground collision. With a single
        // Move() per frame, we check BEFORE moving to avoid stale state.
        bool isGrounded = IsGrounded();

        // When grounded, pin the vertical velocity to a small negative value
        // so the controller stays snapped to the ground on slopes.
        if (isGrounded && verticalVelocity < 0f)
        {
            verticalVelocity = -2f;
        }

        // --- Jumping ---
        // Physics formula: v = sqrt(2 * jumpHeight * gravity)
        // This gives the exact initial velocity needed to reach 'jumpHeight'.
        if (jumpPressed && isGrounded)
        {
            verticalVelocity = Mathf.Sqrt(2f * jumpHeight * gravity);
        }

        // --- Gravity ---
        verticalVelocity -= gravity * Time.deltaTime;

        // --- Build final movement vector ---
        // Combine horizontal (WASD) and vertical (jump/gravity) into one
        // vector so we only call Move() once per frame. This prevents
        // isGrounded desync issues caused by multiple Move() calls.
        Vector3 horizontalMove = transform.right * moveInput.x + transform.forward * moveInput.y;
        Vector3 finalMove      = horizontalMove * moveSpeed + Vector3.up * verticalVelocity;

        controller.Move(finalMove * Time.deltaTime);
    }

    // ──────────────────────────────────────────────
    //  Ground Detection
    // ──────────────────────────────────────────────

    /// <summary>
    /// Performs a Physics.CheckSphere at the base of the controller to
    /// determine if the entity is standing on walkable ground. More
    /// reliable than CharacterController.isGrounded for single-Move setups.
    /// </summary>
    /// <returns>True if the sphere overlaps any collider on the ground layer.</returns>
    private bool IsGrounded()
    {
        // Cast a small sphere downward from the base of the controller.
        Vector3 sphereOrigin = transform.position + Vector3.down * groundCheckDistance;
        return Physics.CheckSphere(sphereOrigin, groundCheckRadius, groundLayer, QueryTriggerInteraction.Ignore);
    }
}
