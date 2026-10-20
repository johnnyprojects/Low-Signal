using UnityEngine;

/// <summary>
/// Attach to the Player. The LIGHT ability: a spot light shining from the robot's eye, toggled on and off by using
/// the ability (right click with LIGHT selected on the ability wheel). The beam is part of the robot model, so it turns
/// with it, bobs and sways with it, and points along the robot's head once it leans horizontal to swim.
/// A small glow lights the robot itself while the beam is on. Drains the shared energy while on and switches off
/// when it runs out.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerEnergy))]
public class Flashlight : MonoBehaviour, IRobotAbility
{
    [Header("Beam")]
    [Tooltip("A cool white with a hint of cyan cuts through murky water without looking warm and out of place.")]
    [SerializeField] private Color beamColor = new Color(0.78f, 0.94f, 1f);
    [SerializeField, Min(0f)] private float beamIntensity = 30f;
    [Tooltip("How far the beam reaches, in metres.")]
    [SerializeField, Min(0.1f)] private float beamRange = 24f;
    [Tooltip("Width of the beam's cone, in degrees.")]
    [SerializeField, Range(1f, 179f)] private float beamAngle = 60f;
    [Tooltip("Width of the beam's bright core, in degrees.")]
    [SerializeField, Range(0f, 179f)] private float innerBeamAngle = 28f;
    [Tooltip("Where the beam starts, relative to the robot model (Robot Visual): just in front of its eye.")]
    [SerializeField] private Vector3 eyeOffset = new Vector3(0f, 0.42f, 0.5f);
    [SerializeField] private LightShadows shadows = LightShadows.Soft;

    [Header("Robot Glow")]
    [Tooltip("A soft point light near the eye, so the robot itself glows while the beam is on.")]
    [SerializeField] private bool robotGlow = true;
    [SerializeField, Min(0f)] private float glowIntensity = 1.5f;
    [SerializeField, Min(0.1f)] private float glowRange = 1.8f;

    [Header("Energy")]
    [Tooltip("Seconds a full charge lasts with the light on.")]
    [SerializeField, Min(0.1f)] private float fullDrainSeconds = 60f;
    [Tooltip("Shared player energy. Found on this object when left empty.")]
    [SerializeField] private PlayerEnergy energySource;

    [Header("State")]
    [SerializeField] private bool startOn;

    private UnderwaterSwimController controller;
    private Transform pivot;
    private Light beam;
    private Light glow;

    public AbilityId Id => AbilityId.Light;
    public bool IsOn => beam != null && beam.enabled;
    public bool IsActive => IsOn;
    public string Status => IsOn ? "ON" : "OFF";

    private void Awake()
    {
        if (energySource == null)
            energySource = GetComponent<PlayerEnergy>();
        controller = GetComponent<UnderwaterSwimController>();
    }

    private void Start()
    {
        // The swim controller sets up the robot model in its own Awake, so the beam is attached here.
        Transform body = controller != null && controller.BodyVisual != null ? controller.BodyVisual : transform;
        pivot = new GameObject("Light Ability").transform;
        pivot.SetParent(body, false);
        pivot.localPosition = eyeOffset;

        beam = pivot.gameObject.AddComponent<Light>();
        beam.type = LightType.Spot;
        beam.color = beamColor;
        beam.intensity = beamIntensity;
        beam.range = beamRange;
        beam.spotAngle = beamAngle;
        beam.innerSpotAngle = Mathf.Min(innerBeamAngle, beamAngle);
        beam.shadows = shadows;

        if (robotGlow)
        {
            glow = new GameObject("Robot Glow").AddComponent<Light>();
            glow.transform.SetParent(pivot, false);
            // Just behind the beam, so it washes over the head and visor.
            glow.transform.localPosition = new Vector3(0f, 0f, -0.25f);
            glow.type = LightType.Point;
            glow.color = beamColor;
            glow.intensity = glowIntensity;
            glow.range = glowRange;
            glow.shadows = LightShadows.None;
        }

        beam.enabled = false;
        if (glow != null)
            glow.enabled = false;
        SetOn(startOn);
        AimBeam();
    }

    private void Update()
    {
        if (!IsOn)
            return;
        energySource.Consume(energySource.MaxEnergy * Time.deltaTime / Mathf.Max(0.1f, fullDrainSeconds));
        if (!energySource.HasEnergy)
            SetOn(false);
    }

    private void LateUpdate()
    {
        // Other abilities can empty the shared pool after this light's Update.
        if (IsOn && !energySource.HasEnergy)
            SetOn(false);
        AimBeam();
    }

    /// <summary>
    /// Upright, the robot faces along its forward axis. Leaned over to swim, its head leads, so the beam tips up the
    /// body by the same amount the body leans forward.
    /// </summary>
    private void AimBeam()
    {
        if (pivot == null)
            return;
        float lean = controller != null ? controller.Lean01 : 0f;
        pivot.localRotation = Quaternion.Euler(-90f * lean, 0f, 0f);
    }

    public bool Use()
    {
        bool wasOn = IsOn;
        SetOn(!wasOn);
        // Turning on with an empty battery does nothing.
        return IsOn != wasOn;
    }

    public void Toggle() => Use();

    public void SetOn(bool on)
    {
        if (beam == null)
            return;
        // An empty battery cannot power the light.
        if (on && (energySource == null || !energySource.HasEnergy))
            on = false;
        beam.enabled = on;
        if (glow != null)
            glow.enabled = on;
    }
}
