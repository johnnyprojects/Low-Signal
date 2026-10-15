using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Attach to the Player with a Rigidbody and CapsuleCollider. Assign its child camera.
/// The player walks by default: WASD moves along the ground relative to the view's heading; gravity applies.
/// Holding Space swims: the player floats slowly upward and WASD steers horizontally.
/// Releasing Space lets gravity pull the player back down to walk.
/// Shift boosts in both modes. The capsule stays upright while the camera looks around.
/// Swimming and boosting draw from the Flashlight's battery; when it is empty, neither works.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody), typeof(CapsuleCollider))]
public class UnderwaterSwimController : MonoBehaviour
{
    [Header("Swimming")]
    [Tooltip("Hold to swim upward.")]
    [SerializeField] private Key swimKey = Key.Space;
    [Tooltip("Upward speed reached while holding the swim key.")]
    [SerializeField, Min(0f)] private float floatSpeed = 1.5f;
    [Tooltip("How quickly the float speed is reached. Lower values feel more sluggish.")]
    [SerializeField, Min(0.01f)] private float floatAcceleration = 3f;
    [Tooltip("Horizontal speed while swimming.")]
    [SerializeField, Min(0f)] private float swimMoveSpeed = 3f;

    [Header("Energy")]
    [Tooltip("The battery swimming and boosting draw from. Found on this object when left empty; with none, both are free.")]
    [SerializeField] private Flashlight energySource;
    [Tooltip("Fraction of a full charge spent per second while swimming (0.05 = 5%).")]
    [SerializeField, Range(0f, 1f)] private float swimEnergyPerSecond = 0.05f;
    [Tooltip("Fraction of a full charge spent per second while boosting and moving (0.05 = 5%).")]
    [SerializeField, Range(0f, 1f)] private float boostEnergyPerSecond = 0.05f;

    [Header("Movement")]
    [SerializeField, Min(1f)] private float boostMultiplier = 2f;
    [SerializeField, Min(0.01f)] private float acceleration = 24f;
    [SerializeField, Min(0.01f)] private float brakingAcceleration = 16f;
    [Tooltip("Small clearance maintained by the capsule sweep at solid surfaces.")]
    [SerializeField, Min(0.001f)] private float collisionSkin = 0.02f;

    [Header("Walking")]
    [SerializeField, Min(0f)] private float walkSpeed = 4f;
    [Tooltip("Downward acceleration while walking. Lower than 9.81 feels heavier-water floaty.")]
    [SerializeField, Min(0f)] private float walkGravity = 9.81f;
    [SerializeField, Min(0f)] private float maxFallSpeed = 20f;
    [Tooltip("Fraction of acceleration available while airborne.")]
    [SerializeField, Range(0f, 1f)] private float airControl = 0.3f;
    [SerializeField, Range(0f, 89f)] private float maxSlopeAngle = 45f;
    [Tooltip("How far below the capsule the ground is searched for.")]
    [SerializeField, Min(0.001f)] private float groundCheckDistance = 0.1f;

    [Header("Look")]
    [Tooltip("A child camera or child view pivot. Do not assign the Player itself.")]
    [SerializeField] private Transform viewTransform;
    [SerializeField, Min(0f)] private float lookSensitivity = 0.12f;
    [SerializeField, Range(0f, 89f)] private float pitchLimit = 85f;
    [SerializeField] private bool lockCursorOnStart = true;

    private Rigidbody body;
    private Vector3 moveInput;
    private bool boosting;
    private bool swimming;
    private float yaw;
    private float pitch;

    public bool IsSwimming => swimming;
    public bool IsGrounded { get; private set; }

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
        body.isKinematic = false;
        body.useGravity = false;
        body.constraints = RigidbodyConstraints.FreezeRotation;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        GetComponent<CapsuleCollider>().isTrigger = false;
        if (energySource == null)
            energySource = GetComponent<Flashlight>();
        SyncLookRotation();
    }

    private void Start()
    {
        if (lockCursorOnStart)
            LockCursor();
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        Mouse mouse = Mouse.current;
        if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            UnlockCursor();
        else if (mouse != null && mouse.leftButton.wasPressedThisFrame)
            LockCursor();

        moveInput = Vector3.zero;
        boosting = false;
        swimming = false;
        if (Cursor.lockState != CursorLockMode.Locked || !Application.isFocused)
            return;

        if (mouse != null)
        {
            Vector2 delta = mouse.delta.ReadValue() * lookSensitivity;
            yaw = Mathf.Repeat(yaw + delta.x, 360f);
            pitch = Mathf.Clamp(pitch - delta.y, -pitchLimit, pitchLimit);
            viewTransform.localRotation = Quaternion.Euler(pitch, yaw, 0f);
        }
        if (keyboard == null)
            return;

        swimming = keyboard[swimKey].isPressed;

        if (keyboard.wKey.isPressed) moveInput.z += 1f;
        if (keyboard.sKey.isPressed) moveInput.z -= 1f;
        if (keyboard.dKey.isPressed) moveInput.x += 1f;
        if (keyboard.aKey.isPressed) moveInput.x -= 1f;
        // Diagonal input must not exceed the speed of a single direction.
        moveInput = Vector3.ClampMagnitude(moveInput, 1f);
        // Boosting only counts while moving, so holding Shift in place costs nothing.
        boosting = (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed) && moveInput.sqrMagnitude > 0f;
        SpendEnergy();
    }

    private void SpendEnergy()
    {
        if (energySource == null)
            return;
        float rate = (swimming ? swimEnergyPerSecond : 0f) + (boosting ? boostEnergyPerSecond : 0f);
        // An empty battery cancels both, so the player sinks and walks at normal speed.
        if (rate > 0f && !energySource.UseEnergy(rate * Time.deltaTime))
        {
            swimming = false;
            boosting = false;
        }
    }

    private void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;
        float topSpeed = (swimming ? swimMoveSpeed : walkSpeed) * (boosting ? boostMultiplier : 1f);
        // Move along the view's heading only, so looking up or down does not change speed.
        Quaternion heading = Quaternion.Euler(0f, viewTransform.eulerAngles.y, 0f);
        Vector3 wishVelocity = heading * new Vector3(moveInput.x, 0f, moveInput.z) * topSpeed;
        float rate = wishVelocity.sqrMagnitude > 0f ? acceleration : brakingAcceleration;
        // Start from the velocity resolved by physics, including the last collision.
        Vector3 velocity = body.linearVelocity;

        if (swimming)
        {
            // Ease toward a gentle rise so swimming reads as floating, not jumping.
            IsGrounded = false;
            Vector3 horizontal = Vector3.MoveTowards(new Vector3(velocity.x, 0f, velocity.z), wishVelocity, rate * dt);
            float vertical = Mathf.MoveTowards(velocity.y, floatSpeed, floatAcceleration * dt);
            velocity = new Vector3(horizontal.x, vertical, horizontal.z);
        }
        else
        {
            IsGrounded = TryGetGround(out Vector3 groundNormal);
            if (IsGrounded)
            {
                // Follow the slope so walking neither launches off downhill nor slows uphill.
                Vector3 target = Vector3.ProjectOnPlane(wishVelocity, groundNormal).normalized * wishVelocity.magnitude;
                velocity = Vector3.MoveTowards(velocity, target, rate * dt);
            }
            else
            {
                Vector3 horizontal = Vector3.MoveTowards(new Vector3(velocity.x, 0f, velocity.z), wishVelocity, rate * airControl * dt);
                float vertical = Mathf.Max(velocity.y - walkGravity * dt, -maxFallSpeed);
                velocity = new Vector3(horizontal.x, vertical, horizontal.z);
            }
        }

        body.linearVelocity = LimitVelocityAtSurfaces(velocity);
    }

    private bool TryGetGround(out Vector3 groundNormal)
    {
        groundNormal = Vector3.up;
        if (!body.SweepTest(Vector3.down, out RaycastHit hit, groundCheckDistance + collisionSkin, QueryTriggerInteraction.Ignore))
            return false;
        if (hit.normal.y < Mathf.Cos(maxSlopeAngle * Mathf.Deg2Rad))
            return false;
        groundNormal = hit.normal;
        return true;
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
        Vector3 angles = viewTransform.localEulerAngles;
        yaw = angles.y;
        pitch = Mathf.Clamp(Mathf.DeltaAngle(0f, angles.x), -pitchLimit, pitchLimit);
        viewTransform.localRotation = Quaternion.Euler(pitch, yaw, 0f);
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        if (!hasFocus)
        {
            moveInput = Vector3.zero;
            boosting = false;
            swimming = false;
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
        moveInput = Vector3.zero;
        boosting = false;
        swimming = false;
        UnlockCursor();
    }
}
