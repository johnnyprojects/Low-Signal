using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Attach to the Player. Press F to toggle a spot light that points where the view looks.
/// Assign an existing Light, or one is created under the child camera at runtime.
/// The light drains energy while on and switches off at 0%. While off, energy recharges
/// after a short delay until full or the light is switched back on.
/// Other systems (swimming, sprinting) share this battery through UseEnergy.
/// </summary>
[DisallowMultipleComponent]
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
    [Tooltip("Seconds after switching off before recharging begins.")]
    [SerializeField, Min(0f)] private float rechargeDelay = 5f;
    [Tooltip("Seconds to recharge from empty to full.")]
    [SerializeField, Min(0.1f)] private float fullRechargeSeconds = 30f;

    [Header("State")]
    [SerializeField] private bool startOn;

    private float energy = 1f;
    private float offTimer;

    public bool IsOn => flashlight != null && flashlight.enabled;
    /// <summary>Remaining energy from 0 (empty) to 1 (full).</summary>
    public float Energy01 => energy;

    private void Awake()
    {
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

        UpdateEnergy(Time.deltaTime);
    }

    private void UpdateEnergy(float deltaTime)
    {
        if (IsOn)
        {
            energy = Mathf.Max(0f, energy - deltaTime / fullDrainSeconds);
            if (energy <= 0f)
                SetOn(false);
            return;
        }

        offTimer += deltaTime;
        if (offTimer >= rechargeDelay)
            energy = Mathf.Min(1f, energy + deltaTime / fullRechargeSeconds);
    }

    /// <summary>
    /// Spends energy from the shared battery. Returns false, spending nothing, when the battery is empty.
    /// Any use restarts the wait before recharging.
    /// </summary>
    public bool UseEnergy(float amount)
    {
        if (energy <= 0f)
            return false;
        energy = Mathf.Max(0f, energy - amount);
        offTimer = 0f;
        return true;
    }

    public void Toggle() => SetOn(!IsOn);

    public void SetOn(bool on)
    {
        if (flashlight == null)
            return;
        // An empty battery cannot power the light.
        if (on && energy <= 0f)
            on = false;
        if (on == flashlight.enabled)
            return;

        flashlight.enabled = on;
        // Each switch-off restarts the wait before recharging.
        if (!on)
            offTimer = 0f;
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
