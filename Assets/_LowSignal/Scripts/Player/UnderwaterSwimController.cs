using System;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Attach to the Player with a Rigidbody and CapsuleCollider. Assign its child camera.
/// The robot is always in the water, either hovering or swimming, and always turns smoothly to face where the camera looks:
///  - Hover (default) keeps the robot upright, turning only to the camera's heading, and buoyant. WASD moves level
///    with that heading, E rises and Q sinks. Movement builds and fades with momentum and water drag, and the body
///    gently bobs and sways. Hovering costs no energy.
///  - Swim (hold Space) leans the robot forward until it lies horizontal and follows the camera's full aim, so its
///    back jets push it where the camera points, up and down included, much faster than hovering. WASD and E/Q do
///    nothing. Swimming draws from the shared energy; when it is empty, the robot returns to hover until Space is
///    pressed again.
/// Releasing Space lets the robot glide on and slow from water drag while it straightens up.
/// The camera orbits behind the player in third person; the mouse wheel zooms, and walls pull it in.
/// </summary>
[DisallowMultipleComponent]
// Places the camera before other scripts' LateUpdate, so the flashlight aims along this frame's view.
[DefaultExecutionOrder(-50)]
[RequireComponent(typeof(Rigidbody), typeof(CapsuleCollider), typeof(PlayerEnergy))]
public class UnderwaterSwimController : MonoBehaviour
{
    public enum MovementMode { Hover, Swim }

    [Header("Keys")]
    [Tooltip("Hold to swim; release to hover.")]
    [SerializeField] private Key swimModeKey = Key.Space;
    [Tooltip("Rises while hovering.")]
    [SerializeField] private Key ascendKey = Key.E;
    [Tooltip("Sinks while hovering.")]
    [SerializeField] private Key descendKey = Key.Q;

    [Header("Facing")]
    [Tooltip("Most degrees per second the robot turns to face the camera's direction.")]
    [SerializeField, Min(1f)] private float turnSpeed = 240f;
    [Tooltip("Seconds the robot takes to ease round to the camera's direction. Higher feels heavier in the water.")]
    [SerializeField, Min(0.01f)] private float turnSmoothTime = 0.25f;

    [Header("Hover")]
    [Tooltip("Top speed while hovering. Should stay well below the swim speed.")]
    [SerializeField, Min(0f)] private float hoverMoveSpeed = 4.5f;
    [Tooltip("How quickly hover movement builds up and changes direction. Low values give momentum, so starts and turns lag.")]
    [SerializeField, Min(0.01f)] private float hoverMoveAcceleration = 4.5f;
    [Tooltip("Fraction of velocity the water removes per second with no input, so the robot glides to a stop. " +
             "Also sheds the extra speed after a swim while the robot straightens up.")]
    [SerializeField, Min(0f)] private float waterDrag = 1.2f;

    [Header("Idle Float")]
    [Tooltip("Metres the body bobs up and down while hovering.")]
    [SerializeField, Min(0f)] private float bobHeight = 0.06f;
    [Tooltip("Bobs per second.")]
    [SerializeField, Min(0f)] private float bobFrequency = 0.3f;
    [Tooltip("Degrees the body sways while hovering.")]
    [SerializeField, Min(0f)] private float swayAngle = 2.5f;
    [Tooltip("Sways per second.")]
    [SerializeField, Min(0f)] private float swayFrequency = 0.2f;

    [Header("Hover Tilt")]
    [Tooltip("Degrees the model leans into its movement at full hover speed: forward/back with W/S, rolling into A/D.")]
    [SerializeField, Range(0f, 45f)] private float maxTiltAngle = 12f;
    [Tooltip("Seconds the lean takes to ease in, and to drift back upright once the robot slows.")]
    [SerializeField, Min(0.01f)] private float tiltSmoothTime = 0.35f;

    [Header("Swim")]
    [Tooltip("Cruising speed the jets push toward. Should be well above the hover speed.")]
    [SerializeField, Min(0f)] private float jetSwimSpeed = 12f;
    [Tooltip("Metres per second gained each second while the jets build speed or follow a turn.")]
    [SerializeField, Min(0.01f)] private float jetAcceleration = 8f;
    [Tooltip("Seconds for the jets to spin up to full power, eased, so the robot surges rather than snapping to speed.")]
    [SerializeField, Min(0f)] private float jetSpinUpTime = 0.5f;
    [Tooltip("Hard cap on the robot's speed in any mode.")]
    [SerializeField, Min(0f)] private float maxSpeed = 16f;
    [Tooltip("Steepest the robot will swim up or down, in degrees, however far the camera tilts.")]
    [SerializeField, Range(0f, 89f)] private float swimPitchLimit = 75f;
    [Tooltip("Roughly how many seconds the body takes to lean between upright and horizontal. Eased in and out.")]
    [SerializeField, Min(0.01f)] private float leanTransitionTime = 0.6f;

    [Header("Jets")]
    [Tooltip("Optional. Played while swimming and stopped while hovering.")]
    [SerializeField] private ParticleSystem[] jetEffects = Array.Empty<ParticleSystem>();
    [Tooltip("Optional. Played while swimming and stopped while hovering.")]
    [SerializeField] private AudioSource jetAudio;

    [Header("Energy")]
    [Tooltip("The battery swimming draws from. Found on this object when left empty; shared with all powered abilities.")]
    [SerializeField] private PlayerEnergy energySource;
    [Tooltip("Fraction of a full charge spent per second while swimming (0.05 = 5%). Hovering is free.")]
    [SerializeField, Range(0f, 1f)] private float swimEnergyPerSecond = 0.05f;

    [Header("Collision")]
    [Tooltip("Small clearance maintained by the capsule sweep at solid surfaces.")]
    [SerializeField, Min(0.001f)] private float collisionSkin = 0.02f;

    [Header("Look")]
    [Tooltip("A child camera or child view pivot. Do not assign the Player itself.")]
    [SerializeField] private Transform viewTransform;
    [SerializeField, Min(0f)] private float lookSensitivity = 0.12f;
    [SerializeField, Range(0f, 89f)] private float pitchLimit = 85f;
    [SerializeField] private bool lockCursorOnStart = true;

    [Header("Third-Person Camera")]
    [Tooltip("Distance the camera sits behind the view pivot (the camera's starting local position).")]
    [SerializeField, Min(0f)] private float followDistance = 3.2f;
    [SerializeField, Min(0f)] private float minCameraDistance = 1.5f;
    [SerializeField, Min(0f)] private float maxCameraDistance = 8f;
    [Tooltip("Metres of zoom per mouse-wheel notch.")]
    [SerializeField, Min(0f)] private float zoomStep = 0.5f;
    [Tooltip("Sideways and upward offset from the pivot, so the player sits off-centre over the shoulder.")]
    [SerializeField] private Vector2 overShoulderOffset = new Vector2(0.8f, 0.15f);
    [Tooltip("Radius kept clear between the camera and walls.")]
    [SerializeField, Min(0.01f)] private float cameraCollisionRadius = 0.25f;
    [Tooltip("Time for the camera to move back out after an obstruction clears.")]
    [SerializeField, Min(0.01f)] private float cameraReturnSeconds = 0.3f;
    [Tooltip("Metres the camera's pivot is raised above the robot, for a better view over it. Lowered under low ceilings.")]
    [SerializeField, Min(0f)] private float cameraLift = 0.45f;

    [Header("Body")]
    [Tooltip("The robot model's root, a child of the Player facing +Z with its jets on its back. Bob and sway move it. " +
             "Set up by Low Signal > Player > Set up RobotMesh model; a placeholder capsule is created when empty.")]
    [SerializeField] private Transform bodyVisual;

    private Rigidbody body;
    private CapsuleCollider capsule;
    private Vector3 moveInput;
    // Camera look angles, in world space.
    private float yaw;
    private float pitch;
    // The direction the robot's body faces, easing toward the camera's.
    private float bodyYaw;
    private float bodyYawVelocity;
    private float bodyPitch;
    private float bodyPitchVelocity;
    // Set when the battery runs dry mid-swim, so holding Space does not restart the jets until it is pressed again.
    private bool swimNeedsRepress;
    // 0 is upright (hover), 1 is horizontal (swim).
    private float lean;
    private float leanVelocity;
    private float jetPower;
    private float floatPhase;
    private float tiltPitch;
    private float tiltPitchVelocity;
    private float tiltRoll;
    private float tiltRollVelocity;
    private Vector3 visualRestPosition;
    private Quaternion visualRestRotation;
    private Vector3 viewOffset;
    private Vector3 viewRestPosition;
    private float currentCameraDistance;
    private readonly RaycastHit[] cameraHits = new RaycastHit[16];

    public MovementMode Mode { get; private set; } = MovementMode.Hover;
    public bool IsSwimming => Mode == MovementMode.Swim;
    public bool IsHovering => Mode == MovementMode.Hover;
    /// <summary>True while the swim key is held, for UI feedback.</summary>
    public bool IsModeKeyHeld => Keyboard.current != null && Keyboard.current[swimModeKey].isPressed;
    /// <summary>True while the rise key is held, for UI feedback.</summary>
    public bool IsAscendKeyHeld => Keyboard.current != null && Keyboard.current[ascendKey].isPressed;
    /// <summary>True while the sink key is held, for UI feedback.</summary>
    public bool IsDescendKeyHeld => Keyboard.current != null && Keyboard.current[descendKey].isPressed;
    /// <summary>Key labels for UI hints, so the HUD follows any rebinding.</summary>
    public string AscendKeyName => ascendKey.ToString().ToUpperInvariant();
    public string DescendKeyName => descendKey.ToString().ToUpperInvariant();
    /// <summary>Raised after the robot switches between hover and swim.</summary>
    public event Action<MovementMode> ModeChanged;
    /// <summary>The robot model's root (or the placeholder body), for attaching things that should move with the model.</summary>
    public Transform BodyVisual => bodyVisual;
    /// <summary>0 while upright (hover), 1 once leaned horizontal (swim), easing in between.</summary>
    public float Lean01 => lean;
    /// <summary>While true, mouse movement no longer turns the camera, so a menu (the ability wheel) can use it.</summary>
    public bool LookBlocked { get; set; }

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        if (viewTransform == null)
        {
            Camera childCamera = GetComponentInChildren<Camera>();
            if (childCamera != null)
                viewTransform = childCamera.transform;
        }

        if (viewTransform == null || viewTransform == transform || !viewTransform.IsChildOf(transform))
        {
            Debug.LogError("UnderwaterSwimController needs a child camera/view pivot. Attach the controller only to the Player.", this);
            enabled = false;
            return;
        }

        // A dynamic, solid capsule lets the physics solver stop and slide the swimmer.
        // Rotation stays frozen for physics; the controller turns the body itself so the capsule leans with the robot.
        body.isKinematic = false;
        body.useGravity = false;
        body.constraints = RigidbodyConstraints.FreezeRotation;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        capsule = GetComponent<CapsuleCollider>();
        capsule.isTrigger = false;
        if (energySource == null)
            energySource = GetComponent<PlayerEnergy>();
        viewRestPosition = viewTransform.localPosition;
        // The pivot is kept as an unrotated offset, so leaning the body does not swing the camera around.
        viewOffset = transform.TransformVector(viewRestPosition);
        followDistance = Mathf.Clamp(followDistance, minCameraDistance, maxCameraDistance);
        currentCameraDistance = followDistance;
        bodyYaw = transform.eulerAngles.y;
        if (bodyVisual == null)
            bodyVisual = CreatePlaceholderBody();
        visualRestPosition = bodyVisual.localPosition;
        visualRestRotation = bodyVisual.localRotation;
        // Offset the float cycle so the robot does not always start at the same point of its bob.
        floatPhase = UnityEngine.Random.value * 10f;
        SyncLookRotation();
        SetJets(false);
    }

    private Transform CreatePlaceholderBody()
    {
        // Without a model, a capsule matching the collider keeps the robot visible in third person.
        GameObject placeholder = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        placeholder.name = "Body (Placeholder)";
        Destroy(placeholder.GetComponent<Collider>());
        placeholder.transform.SetParent(transform, false);
        placeholder.transform.localPosition = capsule.center;
        placeholder.transform.localScale = new Vector3(capsule.radius * 2f, capsule.height * 0.5f, capsule.radius * 2f);

        // A small visor shows which way the body faces, and a jet pack marks its back.
        GameObject visor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        visor.name = "Visor";
        Destroy(visor.GetComponent<Collider>());
        visor.transform.SetParent(placeholder.transform, false);
        visor.transform.localPosition = new Vector3(0f, 0.5f, 0.42f);
        visor.transform.localScale = new Vector3(0.7f, 0.15f, 0.2f);
        visor.GetComponent<Renderer>().material.color = new Color(0.3f, 0.9f, 1f);

        GameObject jets = GameObject.CreatePrimitive(PrimitiveType.Cube);
        jets.name = "Jets";
        Destroy(jets.GetComponent<Collider>());
        jets.transform.SetParent(placeholder.transform, false);
        jets.transform.localPosition = new Vector3(0f, 0.1f, -0.5f);
        jets.transform.localScale = new Vector3(0.6f, 0.5f, 0.25f);
        return placeholder.transform;
    }

    private void Start()
    {
        if (lockCursorOnStart)
            LockCursor();
    }

    private void Update()
    {
        if (SaveGameService.InputBlockedThisFrame) return;
        Keyboard keyboard = Keyboard.current;
        Mouse mouse = Mouse.current;
        if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            UnlockCursor();
        else if (mouse != null && mouse.leftButton.wasPressedThisFrame)
            LockCursor();

        moveInput = Vector3.zero;
        bool swimHeld = false;
        if (Cursor.lockState == CursorLockMode.Locked && Application.isFocused)
            swimHeld = ReadInput(keyboard, mouse);
        if (!swimHeld)
            swimNeedsRepress = false;
        SetMode(swimHeld && !swimNeedsRepress ? MovementMode.Swim : MovementMode.Hover);
        SpendEnergy();
    }

    public PlayerSaveData CaptureSaveState()
    {
        return new PlayerSaveData
        {
            position = transform.position,
            heading = bodyYaw,
            look = new Vector2(pitch, yaw),
            cameraDistance = followDistance,
            energy = energySource != null ? energySource.CurrentEnergy : 0f,
            flashlightOn = GetComponent<Flashlight>() != null && GetComponent<Flashlight>().IsOn,
        };
    }

    public void RestoreSaveState(PlayerSaveData state)
    {
        moveInput = Vector3.zero;
        SetMode(MovementMode.Hover);
        swimNeedsRepress = true;
        lean = leanVelocity = jetPower = bodyPitch = bodyPitchVelocity = bodyYawVelocity = 0f;
        tiltPitch = tiltRoll = tiltPitchVelocity = tiltRollVelocity = 0f;
        bodyYaw = state.heading;
        body.linearVelocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
        body.position = state.position;
        body.rotation = Quaternion.Euler(0f, bodyYaw, 0f);
        transform.SetPositionAndRotation(body.position, body.rotation);
        pitch = Mathf.Clamp(state.look.x, -pitchLimit, pitchLimit);
        yaw = state.look.y;
        followDistance = Mathf.Clamp(state.cameraDistance, minCameraDistance, maxCameraDistance);
        currentCameraDistance = followDistance;
        if (energySource != null) energySource.RestoreSavedEnergy(state.energy);
        GetComponent<Flashlight>()?.SetOn(state.flashlightOn);
        GetComponent<AbilityWheel>()?.RestoreSaveState(state);
        SetJets(false);
    }

    /// <summary>Reads look and movement input; returns whether the swim key is held.</summary>
    private bool ReadInput(Keyboard keyboard, Mouse mouse)
    {
        // Menus such as the ability wheel borrow the mouse; the camera holds still until they let go.
        if (mouse != null && !LookBlocked)
        {
            // The camera always looks freely; the robot turns to follow it in both modes.
            Vector2 look = mouse.delta.ReadValue() * lookSensitivity;
            yaw = Mathf.Repeat(yaw + look.x, 360f);
            pitch = Mathf.Clamp(pitch - look.y, -pitchLimit, pitchLimit);
            float scroll = mouse.scroll.ReadValue().y;
            if (scroll != 0f)
                followDistance = Mathf.Clamp(followDistance - Mathf.Sign(scroll) * zoomStep,
                    minCameraDistance, maxCameraDistance);
        }
        if (keyboard == null)
            return false;

        if (keyboard.wKey.isPressed) moveInput.z += 1f;
        if (keyboard.sKey.isPressed) moveInput.z -= 1f;
        if (keyboard.dKey.isPressed) moveInput.x += 1f;
        if (keyboard.aKey.isPressed) moveInput.x -= 1f;
        if (keyboard[ascendKey].isPressed) moveInput.y += 1f;
        if (keyboard[descendKey].isPressed) moveInput.y -= 1f;
        // Diagonal input must not exceed the speed of a single direction.
        moveInput = Vector3.ClampMagnitude(moveInput, 1f);
        return keyboard[swimModeKey].isPressed;
    }

    private void SetMode(MovementMode next)
    {
        if (next == Mode)
            return;
        if (next == MovementMode.Swim)
        {
            // Swimming needs some charge, so an empty battery cannot flicker between modes.
            if (energySource != null && !energySource.HasEnergy)
            {
                swimNeedsRepress = true;
                return;
            }
            jetPower = 0f;
        }

        Mode = next;
        SetJets(next == MovementMode.Swim);
        ModeChanged?.Invoke(next);
    }

    private void SetJets(bool on)
    {
        foreach (ParticleSystem jet in jetEffects)
        {
            if (jet == null)
                continue;
            if (on)
                jet.Play();
            else
                jet.Stop();
        }
        if (jetAudio == null)
            return;
        if (on && !jetAudio.isPlaying)
            jetAudio.Play();
        else if (!on)
            jetAudio.Stop();
    }

    private void SpendEnergy()
    {
        if (energySource == null || Mode != MovementMode.Swim || swimEnergyPerSecond <= 0f)
            return;
        // An empty battery cuts the jets, so the robot glides back into hover until Space is pressed again.
        if (!energySource.Consume(swimEnergyPerSecond * energySource.MaxEnergy * Time.deltaTime))
        {
            swimNeedsRepress = true;
            SetMode(MovementMode.Hover);
        }
    }

    private void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;
        bool swimming = Mode == MovementMode.Swim;
        // Start from the velocity resolved by physics, including the last collision.
        Vector3 velocity = body.linearVelocity;
        // SmoothDamp eases both into and out of the lean, so the robot tips over and rights itself gradually.
        lean = Mathf.SmoothDamp(lean, swimming ? 1f : 0f, ref leanVelocity, leanTransitionTime, Mathf.Infinity, dt);
        // Ease round to the camera's direction at a capped turn speed, like turning through water.
        bodyYaw = Mathf.SmoothDampAngle(bodyYaw, yaw, ref bodyYawVelocity, turnSmoothTime, turnSpeed, dt);
        // Pitch is tracked in both modes but only shows through the lean, so hovering stays upright.
        bodyPitch = Mathf.SmoothDamp(bodyPitch, Mathf.Clamp(pitch, -swimPitchLimit, swimPitchLimit),
            ref bodyPitchVelocity, turnSmoothTime, turnSpeed, dt);

        if (swimming)
        {
            jetPower = jetSpinUpTime > 0f ? Mathf.MoveTowards(jetPower, 1f, dt / jetSpinUpTime) : 1f;
            Vector3 forward = Quaternion.Euler(bodyPitch, bodyYaw, 0f) * Vector3.forward;
            // The jets on the back only push forward once the body has leaned over into the travel direction.
            float alignment = Mathf.Clamp01(Vector3.Dot(body.rotation * Vector3.up, forward));
            float thrust = Mathf.SmoothStep(0f, 1f, jetPower) * alignment;
            velocity = Vector3.MoveTowards(velocity, forward * (jetSwimSpeed * thrust), jetAcceleration * dt);
        }
        else
        {
            jetPower = 0f;
            // Move along the camera's heading only, so looking up or down does not change speed.
            Vector3 wishVelocity = Quaternion.Euler(0f, yaw, 0f) * moveInput * hoverMoveSpeed;
            velocity = HoverVelocity(velocity, wishVelocity, dt);
        }

        // Leaning a quarter turn forward points the head along the travel direction and the back jets behind it.
        body.MoveRotation(Quaternion.Euler(bodyPitch * lean, bodyYaw, 0f) * Quaternion.Euler(90f * lean, 0f, 0f));
        body.linearVelocity = LimitVelocityAtSurfaces(Vector3.ClampMagnitude(velocity, maxSpeed));
    }

    private Vector3 HoverVelocity(Vector3 velocity, Vector3 wishVelocity, float dt)
    {
        // Water drag decays the velocity, so the robot glides to a stop instead of halting.
        Vector3 dragged = velocity * Mathf.Exp(-waterDrag * dt);
        if (wishVelocity.sqrMagnitude < 0.0001f)
            return dragged;
        // Input builds and turns the velocity gradually, so starts and direction changes carry momentum.
        Vector3 steered = Vector3.MoveTowards(velocity, wishVelocity, hoverMoveAcceleration * dt);
        // Above hover speed, such as just after a swim, drag sheds the extra speed at least as fast as gliding.
        return Vector3.ClampMagnitude(steered, Mathf.Max(wishVelocity.magnitude, dragged.magnitude));
    }

    private void LateUpdate()
    {
        Quaternion look = Quaternion.Euler(pitch, yaw, 0f);
        viewTransform.rotation = look;
        // Raise the pivot above the robot for a view over it, stopping short of any ceiling.
        Vector3 raise = viewOffset + Vector3.up * cameraLift;
        Vector3 pivot = transform.position + raise.normalized * CameraClearance(transform.position, raise, raise.magnitude);
        Vector3 shoulder = look * new Vector3(overShoulderOffset.x, overShoulderOffset.y, 0f);
        pivot += shoulder.normalized * CameraClearance(pivot, shoulder, shoulder.magnitude);

        Vector3 back = look * Vector3.back;
        float clearDistance = CameraClearance(pivot, back, followDistance);
        // Snap in so walls never block the view, then ease back out once clear.
        currentCameraDistance = clearDistance < currentCameraDistance
            ? clearDistance
            : Mathf.MoveTowards(currentCameraDistance, clearDistance,
                maxCameraDistance * Time.unscaledDeltaTime / Mathf.Max(0.01f, cameraReturnSeconds));
        viewTransform.position = pivot + back * currentCameraDistance;

        ApplyIdleFloat();
    }

    private void ApplyIdleFloat()
    {
        if (bodyVisual == null)
            return;
        // Only the model floats, so buoyancy never fights the physics capsule. It fades out as the robot leans into a swim.
        float calm = 1f - lean;
        float time = Time.time + floatPhase;
        float bob = Mathf.Sin(time * bobFrequency * 2f * Mathf.PI) * bobHeight * calm;
        float swayPhase = time * swayFrequency * 2f * Mathf.PI;
        // Pitch and roll drift at different rates, so the sway never repeats as an obvious loop.
        float swayPitch = Mathf.Sin(swayPhase) * swayAngle * calm;
        float swayRoll = Mathf.Sin(swayPhase * 0.77f + 1.3f) * swayAngle * calm;
        UpdateHoverTilt(calm);
        bodyVisual.localPosition = visualRestPosition + transform.InverseTransformDirection(Vector3.up) * bob;
        bodyVisual.localRotation = visualRestRotation
            * Quaternion.Euler(swayPitch + tiltPitch, 0f, swayRoll + tiltRoll);
    }

    /// <summary>Leans the model into its hover movement: forward and back with W/S, and rolling into A/D strafes.</summary>
    private void UpdateHoverTilt(float calm)
    {
        // Velocity in the body's own frame; the root only yaws while hovering, so this is forward and sideways speed.
        Vector3 local = transform.InverseTransformDirection(body.linearVelocity);
        float scale = maxTiltAngle * calm / Mathf.Max(0.01f, hoverMoveSpeed);
        // Positive pitch tips the head forward; negative roll tips the top toward the right.
        float targetPitch = Mathf.Clamp(local.z * scale, -maxTiltAngle, maxTiltAngle);
        float targetRoll = Mathf.Clamp(-local.x * scale, -maxTiltAngle, maxTiltAngle);
        tiltPitch = Mathf.SmoothDamp(tiltPitch, targetPitch, ref tiltPitchVelocity, tiltSmoothTime);
        tiltRoll = Mathf.SmoothDamp(tiltRoll, targetRoll, ref tiltRollVelocity, tiltSmoothTime);
    }

    /// <summary>How far the camera can travel from the pivot along a direction before touching a wall.</summary>
    private float CameraClearance(Vector3 origin, Vector3 direction, float distance)
    {
        if (distance <= 0.0001f || direction.sqrMagnitude < 0.000001f)
            return distance;
        int count = Physics.SphereCastNonAlloc(origin, cameraCollisionRadius, direction.normalized, cameraHits,
            distance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        float nearest = distance;
        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = cameraHits[i];
            // Ignore the player's own colliders and casts that start overlapping something.
            if (hit.collider.transform.IsChildOf(transform) || hit.distance <= 0f)
                continue;
            nearest = Mathf.Min(nearest, hit.distance);
        }
        return nearest;
    }

    private Vector3 LimitVelocityAtSurfaces(Vector3 nextVelocity)
    {
        // Re-check after each projection so corners can constrain multiple directions.
        // Remove only motion into a surface; tangential motion can still slide along it.
        for (int i = 0; i < 3; i++)
        {
            float speed = nextVelocity.magnitude;
            if (speed < 0.0001f)
                break;

            Vector3 direction = nextVelocity / speed;
            float distance = speed * Time.fixedDeltaTime + collisionSkin;
            if (!body.SweepTest(direction, out RaycastHit hit, distance, QueryTriggerInteraction.Ignore))
                break;

            float intoSurface = -Vector3.Dot(nextVelocity, hit.normal);
            if (intoSurface <= 0.0001f)
                break;

            float allowedSpeed = Mathf.Max(0f, hit.distance - collisionSkin) / Time.fixedDeltaTime;
            float allowedIntoSurface = allowedSpeed * -Vector3.Dot(direction, hit.normal);
            nextVelocity += hit.normal * Mathf.Max(0f, intoSurface - allowedIntoSurface);
        }
        return nextVelocity;
    }

    /// <summary>Call after another script changes the view's rotation.</summary>
    public void SyncLookRotation()
    {
        if (viewTransform == null)
            return;
        Vector3 angles = viewTransform.eulerAngles;
        yaw = angles.y;
        pitch = Mathf.Clamp(Mathf.DeltaAngle(0f, angles.x), -pitchLimit, pitchLimit);
        viewTransform.rotation = Quaternion.Euler(pitch, yaw, 0f);
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        if (!hasFocus)
        {
            moveInput = Vector3.zero;
            UnlockCursor();
        }
    }

    private static void LockCursor()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private static void UnlockCursor()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private void OnDisable()
    {
        if (viewTransform != null)
            viewTransform.localPosition = viewRestPosition;
        // The rest pose is only captured once Awake finished its setup.
        if (bodyVisual != null && capsule != null)
            bodyVisual.SetLocalPositionAndRotation(visualRestPosition, visualRestRotation);
        moveInput = Vector3.zero;
        if (Mode != MovementMode.Hover)
        {
            Mode = MovementMode.Hover;
            SetJets(false);
            ModeChanged?.Invoke(Mode);
        }
        UnlockCursor();
    }
}
