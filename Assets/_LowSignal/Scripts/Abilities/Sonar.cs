using System.Collections;
using UnityEngine;

/// <summary>
/// Attach to the Player. The SONAR ability: using it (right click with SONAR selected on the ability wheel) sends out a
/// pulse. For now it has no gameplay effect; it is the readout only:
/// a glowing sphere bursts out from the robot in every direction and fades as it grows, and a ping plays.
/// The sphere is drawn as a shell (Low Signal/Sonar Pulse shader), bright only at its edges, so it reads as a bubble of
/// sound sweeping outward, and as a ring of light around the screen once it passes the camera.
/// A short cooldown stops it being spammed.
/// </summary>
[DisallowMultipleComponent]
public class Sonar : MonoBehaviour, IRobotAbility
{
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int RimPowerId = Shader.PropertyToID("_RimPower");

    [Header("Pulse")]
    [Tooltip("Seconds before the sonar can ping again.")]
    [SerializeField, Min(0f)] private float cooldown = 1.5f;
    [Tooltip("How far the sphere travels before it has faded out, in metres.")]
    [SerializeField, Min(0.5f)] private float maxRadius = 14f;
    [Tooltip("How fast the sphere expands, in metres per second. With the default radius it lasts about half a second.")]
    [SerializeField, Min(0.5f)] private float expansionSpeed = 28f;
    [SerializeField] private Color pulseColor = new Color(0.3f, 0.92f, 1f, 1f);
    [Tooltip("How tightly the glow hugs the sphere's edge. Higher makes a thinner, crisper shell.")]
    [SerializeField, Range(0.5f, 8f)] private float rimSharpness = 3f;
    [Tooltip("Material using the Low Signal/Sonar Pulse shader. Assigned from Characters/Robot/Effects by the editor setup.")]
    [SerializeField] private Material pulseMaterial;

    [Header("Sound")]
    [Tooltip("Played once per ping. Leave empty for a silent pulse until a sound is ready.")]
    [SerializeField] private AudioClip pingClip;
    [SerializeField, Range(0f, 1f)] private float pingVolume = 0.8f;

    private AudioSource audioSource;
    private MaterialPropertyBlock properties;
    private float readyAt;

    public AbilityId Id => AbilityId.Sonar;
    public bool IsActive => Time.time < readyAt;
    public string Status => IsActive ? (readyAt - Time.time).ToString("0.0") + "s" : "READY";

    private void Awake()
    {
        properties = new MaterialPropertyBlock();
        if (pulseMaterial == null)
        {
            Shader shader = Shader.Find("Low Signal/Sonar Pulse");
            Debug.LogWarning("Sonar has no pulse material; re-run the editor setup to assign SonarPulse.", this);
            if (shader != null)
                pulseMaterial = new Material(shader);
        }
    }

    public bool Use()
    {
        if (Time.time < readyAt)
            return false;
        readyAt = Time.time + cooldown;
        if (pulseMaterial != null)
            StartCoroutine(Pulse(transform.position));
        if (pingClip != null)
        {
            if (audioSource == null)
            {
                // A 2D source of the sonar's own, so the ping is always heard clearly whatever else plays.
                audioSource = gameObject.AddComponent<AudioSource>();
                audioSource.playOnAwake = false;
                audioSource.spatialBlend = 0f;
            }
            audioSource.PlayOneShot(pingClip, pingVolume);
        }
        return true;
    }

    private IEnumerator Pulse(Vector3 centre)
    {
        GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sphere.name = "Sonar Pulse";
        Destroy(sphere.GetComponent<Collider>());
        sphere.transform.position = centre;
        MeshRenderer shell = sphere.GetComponent<MeshRenderer>();
        shell.sharedMaterial = pulseMaterial;
        shell.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        shell.receiveShadows = false;
        float duration = maxRadius / expansionSpeed;

        for (float elapsed = 0f; elapsed < duration; elapsed += Time.deltaTime)
        {
            float t = elapsed / duration;
            // Burst out fast and ease off, fading as it goes.
            float radius = maxRadius * (1f - (1f - t) * (1f - t));
            Color color = pulseColor;
            color.a *= Mathf.Pow(1f - t, 1.5f);
            // The primitive sphere is one unit across, so its scale is the diameter.
            sphere.transform.localScale = Vector3.one * (radius * 2f);
            properties.SetColor(ColorId, color);
            properties.SetFloat(RimPowerId, rimSharpness);
            shell.SetPropertyBlock(properties);
            yield return null;
        }
        Destroy(sphere);
    }
}
