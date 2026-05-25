using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// A full-featured first-person controller using Unity's built-in
/// CharacterController. Handles WASD movement, gravity, jumping,
/// sprinting, crouching (with ceiling detection), and dynamic
/// viewbobbing via the New Input System (InputAction workflow).
///
/// Setup:
///   1. Attach this script to your Player GameObject.
///   2. Ensure a CharacterController component is also on the same GameObject.
///   3. Place a Camera as a child of the Player and assign it to the
///      'playerCamera' field in the Inspector.
///   4. Tune all values in the Inspector to taste.
///   5. Input actions are self-contained — no InputActions asset required.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class FirstPersonController : MonoBehaviour
{
    // ──────────────────────────────────────────────
    //  Movement
    // ──────────────────────────────────────────────

    [Header("Movement")]
    [SerializeField, Tooltip("Base walking speed in units per second.")]
    private float walkSpeed = 6f;

    [SerializeField, Tooltip("Instantaneous upward velocity applied on jump.")]
    private float jumpHeight = 1.4f;

    [SerializeField, Tooltip("Downward acceleration (positive value). ~9.81 for Earth gravity.")]
    private float gravity = 20f;

    // ──────────────────────────────────────────────
    //  Sprint
    // ──────────────────────────────────────────────

    [Header("Sprint")]
    [SerializeField, Tooltip("Speed multiplier applied while sprinting. 1.0 = no boost.")]
    private float sprintMultiplier = 1.6f;

    // ──────────────────────────────────────────────
    //  Crouch
    // ──────────────────────────────────────────────
    //
    //  HOW CROUCHING WORKS:
    //  When the player presses the crouch key, we smoothly lerp the
    //  CharacterController's height from its standing value down to
    //  'crouchHeight'. At the same time, we lower the camera's local
    //  Y position so the viewpoint drops. Movement speed is reduced
    //  by 'crouchSpeedMultiplier'.
    //
    //  STANDING BACK UP:
    //  Before un-crouching, we cast a ray upward to check for a
    //  ceiling. If there's something above the player's head within
    //  the standing height, we BLOCK the stand-up to prevent the
    //  CharacterController from clipping through geometry.
    //

    [Header("Crouch")]
    [SerializeField, Tooltip("CharacterController height when fully crouched.")]
    private float crouchHeight = 1.0f;

    [SerializeField, Tooltip("Speed multiplier while crouching. 0.5 = half walk speed.")]
    private float crouchSpeedMultiplier = 0.4f;

    [SerializeField, Tooltip("How fast the crouch/stand transition animates (higher = snappier).")]
    private float crouchTransitionSpeed = 10f;

    // ──────────────────────────────────────────────
    //  Slide
    // ──────────────────────────────────────────────
    //
    //  HOW SLIDING WORKS:
    //  When the player presses Crouch while already sprinting, a
    //  "slide" is triggered instead of a normal crouch. The player
    //  drops to crouch height but gets a brief burst of forward
    //  speed (slideForce) that lasts for slideDuration seconds.
    //
    //  During the slide:
    //    - The player moves at slideForce speed (faster than sprint)
    //    - Jumping is locked out
    //    - The slide timer counts down each frame
    //    - When the timer expires, the player settles into a normal
    //      crouch at crouchSpeedMultiplier speed
    //
    //  Think of it like a baseball slide: you sprint, you drop,
    //  you glide forward, then you're crouching.
    //

    [Header("Slide")]
    [SerializeField, Tooltip("Forward speed during a slide. Should be higher than sprint speed for a satisfying lunge.")]
    private float slideForce = 12f;

    [SerializeField, Tooltip("How long the slide speed boost lasts (in seconds).")]
    private float slideDuration = 0.5f;

    // ──────────────────────────────────────────────
    //  Viewbob
    // ──────────────────────────────────────────────
    //
    //  HOW VIEWBOBBING WORKS:
    //  We run a sine wave over time and apply it as a vertical offset
    //  to the camera's local Y position. The sine wave only advances
    //  ("ticks") when the player is grounded AND actively moving.
    //
    //  The FREQUENCY (how fast the bob cycles) and AMPLITUDE (how
    //  far the camera moves up/down) change based on movement state:
    //    - Walking:   moderate frequency, subtle amplitude
    //    - Sprinting: faster frequency, stronger amplitude
    //    - Crouching: bobbing is disabled (amplitude = 0)
    //    - Standing still or airborne: bob timer resets to zero
    //
    //  The formula each frame is:
    //    bobOffset = sin(bobTimer * frequency) * amplitude
    //
    //  bobTimer accumulates Time.deltaTime only while conditions are
    //  met, giving us a smooth oscillation that pauses in mid-air.
    //

    [Header("Viewbob — Walking")]
    [SerializeField, Tooltip("Oscillation speed of the headbob while walking.")]
    private float walkBobFrequency = 12f;

    [SerializeField, Tooltip("Vertical displacement of the headbob while walking.")]
    private float walkBobAmplitude = 0.03f;

    [Header("Viewbob — Sprinting")]
    [SerializeField, Tooltip("Oscillation speed of the headbob while sprinting.")]
    private float sprintBobFrequency = 16f;

    [SerializeField, Tooltip("Vertical displacement of the headbob while sprinting.")]
    private float sprintBobAmplitude = 0.06f;

    // ──────────────────────────────────────────────
    //  Ground Check
    // ──────────────────────────────────────────────

    [Header("Ground Check")]
    [SerializeField, Tooltip("Radius of the sphere cast used for ground detection.")]
    private float groundCheckRadius = 0.3f;

    [SerializeField, Tooltip("Downward offset from the transform for the ground-check sphere origin.")]
    private float groundCheckDistance = 0.15f;

    [SerializeField, Tooltip("Layers considered as walkable ground.")]
    private LayerMask groundLayer = ~0;

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

    /// <summary>Left Shift / Left Shoulder — held to sprint.</summary>
    private InputAction sprintAction;

    /// <summary>Left Ctrl or C / Right Shoulder — toggles crouch.</summary>
    private InputAction crouchAction;

    // ──────────────────────────────────────────────
    //  Internal State
    // ──────────────────────────────────────────────

    private CharacterController controller;

    /// <summary>Accumulated vertical velocity (gravity + jump impulse).</summary>
    private float verticalVelocity;

    /// <summary>Current pitch angle for camera look clamping.</summary>
    private float cameraPitch;

    /// <summary>Cached move input read each frame.</summary>
    private Vector2 moveInput;

    /// <summary>Cached look input read each frame.</summary>
    private Vector2 lookInput;

    /// <summary>True during the frame the jump button was pressed.</summary>
    private bool jumpPressed;

    /// <summary>True while the sprint button is held down.</summary>
    private bool sprintHeld;

    /// <summary>True when the player is in the crouched state.</summary>
    private bool isCrouching;

    /// <summary>The CharacterController's original height, cached on Awake.</summary>
    private float standingHeight;

    /// <summary>The CharacterController's original center, cached on Awake.</summary>
    private Vector3 standingCenter;

    /// <summary>The camera's local Y position when standing, cached on Start.</summary>
    private float standingCameraY;

    /// <summary>
    /// The camera's target local Y when crouching.
    /// Calculated proportionally: standingCameraY * (crouchHeight / standingHeight)
    /// so the camera scales down correctly regardless of initial setup.
    /// </summary>
    private float crouchingCameraY;

    /// <summary>The controller's current target height (smoothly interpolated).</summary>
    private float targetHeight;

    /// <summary>
    /// Accumulator for the sine wave that drives viewbob.
    /// Only ticks upward while the player is grounded and moving.
    /// Reset to zero when the player stops or leaves the ground.
    /// </summary>
    private float bobTimer;

    /// <summary>True while the player is in the slide speed-boost window.</summary>
    private bool isSliding;

    /// <summary>Counts down from slideDuration to zero during a slide.</summary>
    private float slideTimer;

    /// <summary>
    /// The world-space forward direction captured at the moment the slide
    /// begins. Movement is locked to this direction for the slide's
    /// duration so the player can't steer mid-slide.
    /// </summary>
    private Vector3 slideDirection;

    // ──────────────────────────────────────────────
    //  Lifecycle
    // ──────────────────────────────────────────────

    private void Awake()
    {
        // Cache the CharacterController and its default height and center.
        // We store these so we can adjust RELATIVELY during crouch/slide,
        // respecting whatever the user has set in the Inspector.
        controller     = GetComponent<CharacterController>();
        standingHeight = controller.height;
        standingCenter = controller.center;
        targetHeight   = standingHeight;

        // --- Build input actions inline ---
        // Self-contained: no .inputactions asset or generated C# class needed.

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
        moveAction.AddBinding("<Gamepad>/leftStick");

        lookAction = new InputAction("Look", InputActionType.Value);
        lookAction.AddBinding("<Mouse>/delta");
        lookAction.AddBinding("<Gamepad>/rightStick")
            .WithProcessor("ScaleVector2(x=150,y=150)");

        jumpAction = new InputAction("Jump", InputActionType.Button);
        jumpAction.AddBinding("<Keyboard>/space");
        jumpAction.AddBinding("<Gamepad>/buttonSouth");

        sprintAction = new InputAction("Sprint", InputActionType.Button);
        sprintAction.AddBinding("<Keyboard>/leftShift");
        sprintAction.AddBinding("<Gamepad>/leftShoulder");

        crouchAction = new InputAction("Crouch", InputActionType.Button);
        crouchAction.AddBinding("<Keyboard>/leftCtrl");
        crouchAction.AddBinding("<Keyboard>/c");
        crouchAction.AddBinding("<Gamepad>/rightShoulder");
    }

    private void Start()
    {
        // Lock and hide the cursor.
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

        // Cache the camera's default local Y so we can offset it for
        // crouching and viewbob without losing the original position.
        if (playerCamera != null)
        {
            standingCameraY  = playerCamera.localPosition.y;

            // Calculate crouching camera Y proportionally.
            // This ensures the camera stays at the same relative
            // position in the capsule (e.g., 80% of height = eye level)
            // regardless of the initial camera setup.
            crouchingCameraY = standingCameraY * (crouchHeight / standingHeight);
        }
    }

    private void OnEnable()
    {
        moveAction.Enable();
        lookAction.Enable();
        jumpAction.Enable();
        sprintAction.Enable();
        crouchAction.Enable();
    }

    private void OnDisable()
    {
        moveAction.Disable();
        lookAction.Disable();
        jumpAction.Disable();
        sprintAction.Disable();
        crouchAction.Disable();
    }

    private void Update()
    {
        ReadInput();

        HandleMouseLook();
        HandleCrouch();
        HandleMovement();
        HandleViewbob();
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

        // Sprint is a "held" action — true as long as the button is down.
        sprintHeld = sprintAction.IsPressed();
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

        float lookX = lookInput.x * mouseSensitivity * Time.deltaTime;
        float lookY = lookInput.y * mouseSensitivity * Time.deltaTime;

        // Horizontal rotation — rotate the entire player body around Y.
        transform.Rotate(Vector3.up, lookX);

        // Vertical rotation — tilt the camera up/down, clamped so we
        // can't flip upside-down.
        cameraPitch -= lookY;
        cameraPitch  = Mathf.Clamp(cameraPitch, -90f, 90f);

        // NOTE: We only set the X rotation here. The Y offset (for viewbob
        // and crouch) is handled separately in HandleViewbob / HandleCrouch
        // by modifying playerCamera.localPosition.y.
        playerCamera.localRotation = Quaternion.Euler(cameraPitch, 0f, 0f);
    }

    // ──────────────────────────────────────────────
    //  Crouch
    // ──────────────────────────────────────────────

    /// <summary>
    /// Handles crouch toggling, smooth height transitions, and ceiling
    /// detection to prevent standing up into geometry.
    /// </summary>
    private void HandleCrouch()
    {
        // --- Toggle crouch on button press ---
        // We use WasPressedThisFrame so one tap toggles the state.
        if (crouchAction.WasPressedThisFrame())
        {
            if (isCrouching)
            {
                // Before standing up, check if there's room above.
                // If a ceiling is blocking, stay crouched.
                if (!CanStandUp())
                {
                    Debug.Log("[FirstPersonController] Cannot stand — ceiling detected.", this);
                    return;
                }

                isCrouching = false;
                isSliding   = false;  // Cancel any active slide.
                targetHeight = standingHeight;
            }
            else if (sprintHeld)
            {
                // --- SLIDE TRIGGER ---
                // The player pressed Crouch while sprinting.
                // Instead of a normal crouch, initiate a slide:
                //   1. Drop to crouch height (handled by the lerp below)
                //   2. Start the slide timer (speed boost in HandleMovement)
                isCrouching  = true;
                isSliding    = true;
                slideTimer   = slideDuration;
                targetHeight = crouchHeight;

                // Capture the player's current facing direction so the
                // slide travels in a straight line — no WASD steering.
                slideDirection = transform.forward;

                // SNAP the height and center immediately instead of
                // lerping. The gradual height transition during high-speed
                // movement causes the capsule to briefly lose ground
                // contact, which lets the player clip off the surface.
                controller.height = crouchHeight;

                // Adjust center RELATIVELY: shift down by half the height
                // difference so the FEET stay planted. This works with
                // ANY Inspector center value.
                float heightDelta = standingHeight - crouchHeight;
                controller.center = standingCenter + Vector3.down * (heightDelta * 0.5f);

                Debug.Log("[FirstPersonController] Slide initiated.", this);
            }
            else
            {
                // Normal crouch — no sprint, no slide.
                isCrouching  = true;
                isSliding    = false;
                targetHeight = crouchHeight;
            }
        }

        // --- Tick down the slide timer ---
        // The slide is a temporary speed boost. Once the timer runs out,
        // the player is just crouching normally.
        if (isSliding)
        {
            slideTimer -= Time.deltaTime;

            if (slideTimer <= 0f)
            {
                isSliding = false;

                // Auto-stand after the slide ends (if there's room above).
                // Without this, isCrouching stays true and the player is
                // stuck in slow crouch mode until they manually toggle.
                if (CanStandUp())
                {
                    isCrouching  = false;
                    targetHeight = standingHeight;
                }

                Debug.Log("[FirstPersonController] Slide ended.", this);
            }
        }

        // --- Smoothly interpolate the controller's height ---
        // Lerp provides a smooth, ease-out transition between heights.
        // crouchTransitionSpeed controls how snappy the transition feels.
        controller.height = Mathf.Lerp(controller.height, targetHeight,
                                       Time.deltaTime * crouchTransitionSpeed);

        // --- Adjust the controller's center to keep the feet planted ---
        // Instead of setting an absolute center, we shift RELATIVE to the
        // original Inspector center. When height shrinks by X, the center
        // moves down by X/2. This keeps the capsule bottom in place
        // regardless of what center value was set in the Inspector.
        float currentHeightDelta = standingHeight - controller.height;
        controller.center = standingCenter + Vector3.down * (currentHeightDelta * 0.5f);
    }

    /// <summary>
    /// Casts a ray upward from the player's position to detect if
    /// there is enough vertical clearance to stand at full height.
    /// </summary>
    /// <returns>True if the player can safely stand up.</returns>
    private bool CanStandUp()
    {
        // The distance to check is the difference between standing and
        // current (crouched) height, with a small buffer so we don't
        // clip right at the edge.
        float clearanceNeeded = standingHeight - controller.height + 0.1f;

        // The current capsule top is at:
        // transform.y + center.y + height/2
        float capsuleTop = controller.center.y + controller.height * 0.5f;

        // Cast upward from the top of the current crouched capsule.
        // If anything is hit within that clearance, we can't stand.
        return !Physics.Raycast(
            transform.position + Vector3.up * capsuleTop,
            Vector3.up,
            clearanceNeeded,
            groundLayer,
            QueryTriggerInteraction.Ignore
        );
    }

    // ──────────────────────────────────────────────
    //  Movement & Gravity
    // ──────────────────────────────────────────────

    /// <summary>
    /// Determines the effective movement speed based on state (crouch →
    /// sprint → walk), applies gravity and jumping, then issues a single
    /// Move call to the CharacterController.
    /// </summary>
    private void HandleMovement()
    {
        bool isGrounded = IsGrounded();

        // When grounded, pin vertical velocity to a small negative so
        // the controller stays snapped to slopes. During a slide we use
        // a stronger pin to prevent high-speed separation from the ground.
        if (isGrounded && verticalVelocity < 0f)
        {
            verticalVelocity = isSliding ? -6f : -2f;
        }

        // --- Jumping ---
        // Cannot jump while crouching or sliding — must stand first.
        if (jumpPressed && isGrounded && !isCrouching && !isSliding)
        {
            verticalVelocity = Mathf.Sqrt(2f * jumpHeight * gravity);
        }

        // --- Gravity ---
        verticalVelocity -= gravity * Time.deltaTime;

        // --- Determine current speed ---
        //  Priority chain: slide → crouch → sprint → walk.
        //  Only ONE of these applies per frame.
        float currentSpeed = walkSpeed;

        if (isSliding)
        {
            // Slide is active — override with the slide force.
            // This creates the forward lunge while the timer ticks.
            currentSpeed = slideForce;
        }
        else if (isCrouching)
        {
            // Crouching (post-slide or normal) — slow crawl.
            currentSpeed = walkSpeed * crouchSpeedMultiplier;
        }
        else if (sprintHeld)
        {
            // Sprint only applies when standing and holding the key.
            currentSpeed = walkSpeed * sprintMultiplier;
        }

        // --- Build final movement vector ---
        Vector3 horizontalMove;

        if (isSliding)
        {
            // During a slide, lock movement to the direction the player
            // was facing when the slide started. WASD input is ignored
            // so the player can't steer mid-slide — just like a real slide.
            horizontalMove = slideDirection;
        }
        else
        {
            // Normal movement — WASD relative to current facing.
            horizontalMove = transform.right * moveInput.x + transform.forward * moveInput.y;
        }

        Vector3 finalMove = horizontalMove * currentSpeed + Vector3.up * verticalVelocity;

        controller.Move(finalMove * Time.deltaTime);
    }

    // ──────────────────────────────────────────────
    //  Viewbob
    // ──────────────────────────────────────────────

    /// <summary>
    /// Applies a sine-wave vertical offset to the camera to simulate
    /// natural head movement while walking or sprinting. The bob is
    /// disabled while crouching, airborne, or standing still.
    ///
    /// How the math works:
    ///   bobTimer += Time.deltaTime     (accumulate time while moving)
    ///   offset = sin(bobTimer * freq) * amplitude
    ///
    /// The sine function oscillates between -1 and +1, so the camera
    /// moves amplitude units above and below the base position. Higher
    /// frequency = faster oscillation. Higher amplitude = more dramatic.
    /// </summary>
    private void HandleViewbob()
    {
        if (playerCamera == null) return;

        // Determine the camera's base Y for the current stance.
        // This is the "rest position" that the bob oscillates around.
        float baseCameraY = isCrouching ? crouchingCameraY : standingCameraY;

        // --- Determine if we should bob ---
        // Conditions: grounded + actually moving + not crouching.
        bool isMoving   = moveInput.sqrMagnitude > 0.01f;
        bool isGrounded = IsGrounded();
        bool shouldBob  = isGrounded && isMoving && !isCrouching;

        float bobOffset = 0f;

        if (shouldBob)
        {
            // Pick frequency and amplitude based on sprint state.
            float frequency = sprintHeld ? sprintBobFrequency : walkBobFrequency;
            float amplitude = sprintHeld ? sprintBobAmplitude : walkBobAmplitude;

            // Advance the sine wave timer.
            bobTimer += Time.deltaTime;

            // Calculate the vertical offset.
            // sin() returns -1..+1, so the camera moves ±amplitude.
            bobOffset = Mathf.Sin(bobTimer * frequency) * amplitude;
        }
        else
        {
            // When not bobbing, smoothly decay the timer back to zero
            // so the camera eases back to center instead of snapping.
            bobTimer = Mathf.Lerp(bobTimer, 0f, Time.deltaTime * 8f);

            // Still apply a diminishing offset during the decay so
            // the camera glides to rest, not teleports.
            if (bobTimer > 0.001f)
            {
                float frequency = sprintHeld ? sprintBobFrequency : walkBobFrequency;
                float amplitude = sprintHeld ? sprintBobAmplitude : walkBobAmplitude;
                bobOffset = Mathf.Sin(bobTimer * frequency) * amplitude;
            }
        }

        // --- Apply the final camera Y position ---
        // baseCameraY handles crouch offset; bobOffset adds the sine wave.
        // We lerp the position for butter-smooth transitions between
        // crouching, standing, and bobbing.
        Vector3 camPos = playerCamera.localPosition;
        float   targetY = baseCameraY + bobOffset;

        camPos.y = Mathf.Lerp(camPos.y, targetY, Time.deltaTime * crouchTransitionSpeed);
        playerCamera.localPosition = camPos;
    }

    // ──────────────────────────────────────────────
    //  Ground Detection
    // ──────────────────────────────────────────────

    /// <summary>
    /// Performs a Physics.CheckSphere at the base of the controller to
    /// determine if the entity is standing on walkable ground.
    /// </summary>
    /// <returns>True if the sphere overlaps any collider on the ground layer.</returns>
    private bool IsGrounded()
    {
        // Compute the world-space position of the capsule's bottom.
        // The capsule bottom is at: transform.y + center.y - height/2.
        // We then offset slightly downward by groundCheckDistance.
        float capsuleBottomY = controller.center.y - controller.height * 0.5f;
        Vector3 sphereOrigin = transform.position
                             + Vector3.up * capsuleBottomY
                             + Vector3.down * groundCheckDistance;

        return Physics.CheckSphere(sphereOrigin, groundCheckRadius, groundLayer, QueryTriggerInteraction.Ignore);
    }
}
