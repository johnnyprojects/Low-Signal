using UnityEngine;

/// <summary>
/// Attach to the Player. Builds the robot's particle effects at runtime and switches them with its movement mode:
///  - Hover: a soft stabilising wash puffs from each jet nozzle and a few bubbles trickle up, livelier while moving.
///  - Swim: a bright thrust plume and a stream of bubbles pour from each nozzle, and bubbles shed off the body with speed.
/// Each nozzle emits along its forward axis, which the robot setup points down the robot's back,
/// so the exhaust falls below the hovering robot and streams behind it once it leans into a swim.
/// Low Signal > Player > Set up RobotMesh model creates the nozzles and assigns the materials.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(UnderwaterSwimController))]
public class RobotThrusterEffects : MonoBehaviour
{
    [Tooltip("Where the exhaust leaves each jet; particles emit along each nozzle's forward axis.")]
    [SerializeField] private Transform[] nozzles = new Transform[0];
    [Tooltip("Body that sheds wake bubbles while swimming. Uses this object when empty.")]
    [SerializeField] private Transform body;
    [Tooltip("Additive material for glowing exhaust.")]
    [SerializeField] private Material glowMaterial;
    [Tooltip("Alpha-blended material for bubbles.")]
    [SerializeField] private Material softMaterial;

    [Header("Colours")]
    [SerializeField] private Color thrustColor = new Color(0.45f, 0.9f, 1f, 0.9f);
    [SerializeField] private Color washColor = new Color(0.35f, 0.8f, 1f, 0.22f);
    [SerializeField] private Color bubbleColor = new Color(0.85f, 0.97f, 1f, 0.55f);

    [Header("Hover")]
    [Tooltip("Stabiliser puffs per second from each nozzle while idle.")]
    [SerializeField, Min(0f)] private float hoverWashRate = 10f;
    [Tooltip("Bubbles per second from each nozzle while idle.")]
    [SerializeField, Min(0f)] private float hoverBubbleRate = 2.5f;
    [Tooltip("How much busier the hover effects get at full hover speed (1 = double).")]
    [SerializeField, Min(0f)] private float hoverMovementBoost = 1.5f;
    [Tooltip("Speed treated as full hover speed for the boost above.")]
    [SerializeField, Min(0.1f)] private float hoverReferenceSpeed = 4.5f;

    [Header("Swim")]
    [Tooltip("Thrust plume particles per second from each nozzle.")]
    [SerializeField, Min(0f)] private float thrustRate = 70f;
    [Tooltip("Length of the thrust plume, in metres.")]
    [SerializeField, Min(0.05f)] private float thrustLength = 1.1f;
    [Tooltip("Bubbles per second from each nozzle.")]
    [SerializeField, Min(0f)] private float jetBubbleRate = 40f;
    [Tooltip("Bubbles per second shed off the body at the reference speed.")]
    [SerializeField, Min(0f)] private float wakeBubbleRate = 30f;
    [Tooltip("Speed at which the wake reaches its full rate.")]
    [SerializeField, Min(0.1f)] private float swimReferenceSpeed = 12f;
    [Tooltip("Seconds for the jets to flare up when swimming starts.")]
    [SerializeField, Min(0.01f)] private float ignitionSeconds = 0.3f;

    private UnderwaterSwimController controller;
    private Rigidbody rigidBody;
    private ParticleSystem[] washes;
    private ParticleSystem[] idleBubbles;
    private ParticleSystem[] thrusts;
    private ParticleSystem[] jetBubbles;
    private ParticleSystem wake;
    private float ignition;

    private void Awake()
    {
        controller = GetComponent<UnderwaterSwimController>();
        rigidBody = GetComponent<Rigidbody>();
        if (body == null)
            body = transform;
        if (glowMaterial == null || softMaterial == null)
            Debug.LogWarning("RobotThrusterEffects has no materials; run Low Signal > Player > Set up RobotMesh model.", this);

        // Without nozzles, emit from just behind the body so the effects still show.
        if (nozzles == null || nozzles.Length == 0)
        {
            Transform fallback = new GameObject("Jet Nozzle").transform;
            fallback.SetParent(body, false);
            fallback.SetLocalPositionAndRotation(new Vector3(0f, -0.3f, -0.4f), Quaternion.LookRotation(Vector3.down, Vector3.back));
            nozzles = new[] { fallback };
        }

        washes = new ParticleSystem[nozzles.Length];
        idleBubbles = new ParticleSystem[nozzles.Length];
        thrusts = new ParticleSystem[nozzles.Length];
        jetBubbles = new ParticleSystem[nozzles.Length];
        for (int i = 0; i < nozzles.Length; i++)
        {
            washes[i] = CreateWash(nozzles[i]);
            idleBubbles[i] = CreateIdleBubbles(nozzles[i]);
            thrusts[i] = CreateThrust(nozzles[i]);
            jetBubbles[i] = CreateJetBubbles(nozzles[i]);
        }
        wake = CreateWake(body);
    }

    private void OnEnable()
    {
        controller.ModeChanged += OnModeChanged;
        OnModeChanged(controller.Mode);
    }

    private void OnDisable()
    {
        controller.ModeChanged -= OnModeChanged;
        SetPlaying(washes, false);
        SetPlaying(idleBubbles, false);
        SetPlaying(thrusts, false);
        SetPlaying(jetBubbles, false);
        SetPlaying(wake, false);
    }

    private void OnModeChanged(UnderwaterSwimController.MovementMode mode)
    {
        bool swimming = mode == UnderwaterSwimController.MovementMode.Swim;
        // Stopping only ends emission, so particles already in the water fade out instead of vanishing.
        SetPlaying(washes, !swimming);
        SetPlaying(idleBubbles, !swimming);
        SetPlaying(thrusts, swimming);
        SetPlaying(jetBubbles, swimming);
        SetPlaying(wake, swimming);
        if (swimming)
            ignition = 0f;
    }

    private void Update()
    {
        float speed = rigidBody.linearVelocity.magnitude;
        if (controller.IsSwimming)
        {
            ignition = Mathf.MoveTowards(ignition, 1f, Time.deltaTime / ignitionSeconds);
            // The plume flares up as the jets ignite, then holds; bubbles and wake follow the actual speed.
            float flare = Mathf.SmoothStep(0f, 1f, ignition);
            float pace = Mathf.Clamp01(speed / swimReferenceSpeed);
            SetRate(thrusts, thrustRate * flare);
            SetRate(jetBubbles, jetBubbleRate * Mathf.Lerp(0.4f, 1f, pace) * flare);
            SetRate(wake, wakeBubbleRate * pace);
        }
        else
        {
            float busy = 1f + hoverMovementBoost * Mathf.Clamp01(speed / hoverReferenceSpeed);
            SetRate(washes, hoverWashRate * busy);
            SetRate(idleBubbles, hoverBubbleRate * busy);
        }
    }

    // ---------- Hover ----------

    private ParticleSystem CreateWash(Transform nozzle)
    {
        ParticleSystem system = CreateSystem("Hover Wash", nozzle, glowMaterial, 120);
        ParticleSystem.MainModule main = system.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 0.8f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 0.9f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.1f, 0.16f);
        main.startColor = washColor;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        SetCone(system, 14f, 0.06f);
        SetSizeOverLife(system, 1f, 3f);
        SetFade(system, Color.white, Color.white, 0.25f);
        SetWaterDrag(system, 1.5f);
        return system;
    }

    private ParticleSystem CreateIdleBubbles(Transform nozzle)
    {
        ParticleSystem system = CreateSystem("Hover Bubbles", nozzle, softMaterial, 60);
        ParticleSystem.MainModule main = system.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(2.5f, 4f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 0.5f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.025f, 0.06f);
        main.startColor = bubbleColor;
        // Buoyancy: bubbles leave the nozzle, then drift upward.
        main.gravityModifier = -0.04f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        SetCone(system, 25f, 0.05f);
        SetWobble(system, 0.15f);
        SetFade(system, Color.white, Color.white, 0.15f);
        SetWaterDrag(system, 1f);
        return system;
    }

    // ---------- Swim ----------

    private ParticleSystem CreateThrust(Transform nozzle)
    {
        ParticleSystem system = CreateSystem("Swim Thrust", nozzle, glowMaterial, 200);
        ParticleSystem.MainModule main = system.main;
        const float lifetime = 0.16f;
        main.startLifetime = lifetime;
        main.startSpeed = thrustLength / lifetime;
        main.startSize = new ParticleSystem.MinMaxCurve(0.18f, 0.26f);
        main.startColor = Color.white;
        // Local space keeps the plume a steady length behind the jets at any speed.
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        SetCone(system, 5f, 0.05f);
        SetSizeOverLife(system, 1f, 0.25f);
        // White-hot at the nozzle, cooling to the thrust colour, then gone.
        SetFade(system, Color.white, thrustColor, 0.6f);
        return system;
    }

    private ParticleSystem CreateJetBubbles(Transform nozzle)
    {
        ParticleSystem system = CreateSystem("Swim Bubbles", nozzle, softMaterial, 300);
        ParticleSystem.MainModule main = system.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 2.2f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(2f, 4f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.09f);
        main.startColor = bubbleColor;
        main.gravityModifier = -0.05f;
        // World space leaves a bubble trail hanging in the water behind the robot.
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        SetCone(system, 14f, 0.08f);
        SetWobble(system, 0.35f);
        SetFade(system, Color.white, Color.white, 0.1f);
        SetWaterDrag(system, 2.5f);
        return system;
    }

    private ParticleSystem CreateWake(Transform source)
    {
        ParticleSystem system = CreateSystem("Swim Wake", source, softMaterial, 200);
        ParticleSystem.MainModule main = system.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.6f);
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.05f);
        main.startColor = bubbleColor;
        main.gravityModifier = -0.04f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        // Bubbles pulled off the robot's surface as it cuts through the water.
        ParticleSystem.ShapeModule shape = system.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.55f;
        shape.radiusThickness = 0.1f;
        SetWobble(system, 0.3f);
        SetFade(system, Color.white, Color.white, 0.1f);
        return system;
    }

    // ---------- Shared building blocks ----------

    private static ParticleSystem CreateSystem(string name, Transform parent, Material material, int maxParticles)
    {
        GameObject effect = new GameObject(name);
        effect.transform.SetParent(parent, false);
        ParticleSystem system = effect.AddComponent<ParticleSystem>();
        system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        ParticleSystem.MainModule main = system.main;
        main.playOnAwake = false;
        main.loop = true;
        main.maxParticles = maxParticles;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        ParticleSystem.EmissionModule emission = system.emission;
        emission.rateOverTime = 0f;

        ParticleSystemRenderer renderer = effect.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = material;
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        return system;
    }

    private static void SetCone(ParticleSystem system, float angle, float radius)
    {
        ParticleSystem.ShapeModule shape = system.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = angle;
        shape.radius = radius;
    }

    private static void SetSizeOverLife(ParticleSystem system, float start, float end)
    {
        ParticleSystem.SizeOverLifetimeModule size = system.sizeOverLifetime;
        size.enabled = true;
        float peak = Mathf.Max(start, end);
        size.size = new ParticleSystem.MinMaxCurve(peak, AnimationCurve.EaseInOut(0f, start / peak, 1f, end / peak));
    }

    /// <summary>Fades in quickly, blends from one colour to another, and fades out from fadeOutFrom of the lifetime.</summary>
    private static void SetFade(ParticleSystem system, Color from, Color to, float fadeOutFrom)
    {
        ParticleSystem.ColorOverLifetimeModule colour = system.colorOverLifetime;
        colour.enabled = true;
        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(from, 0f), new GradientColorKey(to, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.08f),
                new GradientAlphaKey(1f, Mathf.Max(0.09f, fadeOutFrom)), new GradientAlphaKey(0f, 1f) });
        colour.color = gradient;
    }

    /// <summary>Gentle turbulence so bubbles wander like they do in real water.</summary>
    private static void SetWobble(ParticleSystem system, float strength)
    {
        ParticleSystem.NoiseModule noise = system.noise;
        noise.enabled = true;
        noise.strength = strength;
        noise.frequency = 0.6f;
        noise.scrollSpeed = 0.3f;
    }

    /// <summary>Water slows particles quickly after they leave the nozzle.</summary>
    private static void SetWaterDrag(ParticleSystem system, float drag)
    {
        ParticleSystem.LimitVelocityOverLifetimeModule limit = system.limitVelocityOverLifetime;
        limit.enabled = true;
        limit.drag = drag;
        limit.multiplyDragByParticleSize = false;
        limit.multiplyDragByParticleVelocity = false;
    }

    private static void SetRate(ParticleSystem[] systems, float rate)
    {
        foreach (ParticleSystem system in systems)
            SetRate(system, rate);
    }

    private static void SetRate(ParticleSystem system, float rate)
    {
        ParticleSystem.EmissionModule emission = system.emission;
        emission.rateOverTime = rate;
    }

    private static void SetPlaying(ParticleSystem[] systems, bool playing)
    {
        if (systems == null)
            return;
        foreach (ParticleSystem system in systems)
            SetPlaying(system, playing);
    }

    private static void SetPlaying(ParticleSystem system, bool playing)
    {
        if (system == null)
            return;
        if (playing && !system.isEmitting)
            system.Play();
        else if (!playing)
            system.Stop(true, ParticleSystemStopBehavior.StopEmitting);
    }
}
