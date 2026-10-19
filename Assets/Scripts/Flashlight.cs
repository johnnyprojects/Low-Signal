using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Attach to the Player. Press F to toggle a spot light that points where the view looks.
/// Assign an existing Light, or one is created under the child camera at runtime.
/// Consumes the player's shared energy while on and switches off when it is empty.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerEnergy))]
public class Flashlight : MonoBehaviour
{
    [Header("Light")]
    [Tooltip("Optional. Leave empty to create a spot light under the child camera.")]
    [SerializeField] private Light flashlight;
    [Tooltip("A child camera or child view pivot. Found automatically when left empty.")]
    [SerializeField] private Transform viewTransform;
    [SerializeField] private Color color = new Color(0.85f, 0.95f, 1f);
    [SerializeField, Min(0f)] private float intensity = 40f;
    [SerializeField, Min(0.1f)] private float range = 30f;
    [SerializeField, Range(1f, 179f)] private float spotAngle = 55f;
    [SerializeField, Range(0f, 179f)] private float innerSpotAngle = 25f;
    [Tooltip("Offset from the view, so the beam reads as hand-held rather than coming from the eyes.")]
    [SerializeField] private Vector3 localOffset = new Vector3(0.25f, -0.2f, 0.3f);
    [SerializeField] private LightShadows shadows = LightShadows.Soft;

    [Header("Energy")]
    [Tooltip("Seconds a full charge lasts with the light on.")]
    [SerializeField, Min(0.1f)] private float fullDrainSeconds = 60f;
    [Tooltip("Shared player energy. Found on this object when left empty.")]
    [SerializeField] private PlayerEnergy energySource;

    [Header("State")]
    [SerializeField] private bool startOn;

    public bool IsOn => flashlight != null && flashlight.enabled;

    private void Awake()
    {
        if (energySource == null)
            energySource = GetComponent<PlayerEnergy>();

        if (viewTransform == null)
        {
            Camera childCamera = GetComponentInChildren<Camera>();
            viewTransform = childCamera != null ? childCamera.transform : transform;
        }

        if (flashlight == null)
            flashlight = CreateLight();

        SetOn(startOn);
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        // Match the swim controller: ignore input while the cursor is released.
        if (keyboard != null && Cursor.lockState == CursorLockMode.Locked && Application.isFocused
            && keyboard.fKey.wasPressedThisFrame)
            Toggle();

        if (IsOn)
        {
            energySource.Consume(energySource.MaxEnergy * Time.deltaTime / Mathf.Max(0.1f, fullDrainSeconds));
            if (!energySource.HasEnergy)
                SetOn(false);
        }
    }

    private void LateUpdate()
    {
        // Other abilities can empty the shared pool after this light's Update.
        if (IsOn && !energySource.HasEnergy)
            SetOn(false);
    }

    public void Toggle() => SetOn(!IsOn);

    public void SetOn(bool on)
    {
        if (flashlight == null)
            return;
        // An empty battery cannot power the light.
        if (on && (energySource == null || !energySource.HasEnergy))
            on = false;
        if (on == flashlight.enabled)
            return;

        flashlight.enabled = on;
    }

    private Light CreateLight()
    {
        GameObject lightObject = new GameObject("Flashlight");
        lightObject.transform.SetParent(viewTransform, false);
        lightObject.transform.localPosition = localOffset;
        lightObject.transform.localRotation = Quaternion.identity;

        Light spot = lightObject.AddComponent<Light>();
        spot.type = LightType.Spot;
        spot.color = color;
        spot.intensity = intensity;
        spot.range = range;
        spot.spotAngle = spotAngle;
        spot.innerSpotAngle = Mathf.Min(innerSpotAngle, spotAngle);
        spot.shadows = shadows;
        return spot;
    }
}
