using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Attach to the Player with a Rigidbody and CapsuleCollider. Assign its child camera.
/// WASD swims relative to the view; Space/E and Ctrl/Q swim up/down relative to the view.
/// Shift boosts. The capsule stays upright while the camera looks around.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody), typeof(CapsuleCollider))]
public class UnderwaterSwimController : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField, Min(0f)] private float swimSpeed = 8f;
    [SerializeField, Min(1f)] private float boostMultiplier = 2f;
    [SerializeField, Min(0.01f)] private float acceleration = 24f;
    [SerializeField, Min(0.01f)] private float brakingAcceleration = 16f;
    [Tooltip("Small clearance maintained by the capsule sweep at solid surfaces.")]
    [SerializeField, Min(0.001f)] private float collisionSkin = 0.02f;

    [Header("Look")]
    [Tooltip("A child camera or child view pivot. Do not assign the Player itself.")]
    [SerializeField] private Transform viewTransform;
    [SerializeField, Min(0f)] private float lookSensitivity = 0.12f;
    [SerializeField, Range(0f, 89f)] private float pitchLimit = 85f;
    [SerializeField] private bool lockCursorOnStart = true;

    private Rigidbody body;
    private Vector3 moveInput;
    private bool boosting;
    private float yaw;
    private float pitch;

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

        if (keyboard.wKey.isPressed) moveInput.z += 1f;
        if (keyboard.sKey.isPressed) moveInput.z -= 1f;
        if (keyboard.dKey.isPressed) moveInput.x += 1f;
        if (keyboard.aKey.isPressed) moveInput.x -= 1f;
        if (keyboard.spaceKey.isPressed || keyboard.eKey.isPressed) moveInput.y += 1f;
        if (keyboard.leftCtrlKey.isPressed || keyboard.rightCtrlKey.isPressed || keyboard.qKey.isPressed) moveInput.y -= 1f;
        // Diagonal input must not exceed the speed of a single direction.
        moveInput = Vector3.ClampMagnitude(moveInput, 1f);
        boosting = keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed;
    }

    private void FixedUpdate()
    {
        float topSpeed = swimSpeed * (boosting ? boostMultiplier : 1f);
        Vector3 targetVelocity = viewTransform.rotation * moveInput * topSpeed;
        float rate = moveInput.sqrMagnitude > 0f ? acceleration : brakingAcceleration;
        // Start from the velocity resolved by physics, including the last collision.
        Vector3 nextVelocity = Vector3.MoveTowards(body.linearVelocity, targetVelocity, rate * Time.fixedDeltaTime);
        body.linearVelocity = LimitVelocityAtSurfaces(nextVelocity);
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
        UnlockCursor();
    }
}
