using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Attach to the Player. The SCANNER ability finds shiny things: using it (right click with SCANNER selected on the ability
/// wheel) sends a faint golden shell sweeping slowly out to the edge of its range, and every item that can still be
/// picked up glints as the sweep passes it: a warm light flares at the item a few times, so treasure hidden among rocks
/// and seagrass stands out. SONAR BLAST is the short, bright pulse.
/// </summary>
[DisallowMultipleComponent]
public class Scanner : MonoBehaviour, IRobotAbility
{
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int RimPowerId = Shader.PropertyToID("_RimPower");

    [Header("Sweep")]
    [Tooltip("Seconds before the scanner can sweep again.")]
    [SerializeField, Min(0f)] private float cooldown = 4f;
    [Tooltip("How far the sweep reaches, in metres. Items within it glint.")]
    [SerializeField, Min(1f)] private float range = 45f;
    [Tooltip("How fast the sweep travels outward, in metres per second.")]
    [SerializeField, Min(1f)] private float sweepSpeed = 30f;
    [SerializeField] private Color sweepColor = new Color(1f, 0.85f, 0.45f, 0.5f);
    [SerializeField, Range(0.5f, 8f)] private float rimSharpness = 4f;
    [Tooltip("Material using the Low Signal/Sonar Pulse shader. Assigned from Characters/Robot/Effects by the editor setup.")]
    [SerializeField] private Material pulseMaterial;

    [Header("Glint")]
    [Tooltip("Seconds each found item glints for.")]
    [SerializeField, Min(0.1f)] private float glintSeconds = 4f;
    [SerializeField] private Color glintColor = new Color(1f, 0.78f, 0.35f);
    [SerializeField, Min(0f)] private float glintIntensity = 4f;
    [SerializeField, Min(0.5f)] private float glintRange = 5f;

    private MaterialPropertyBlock properties;
    private float readyAt;

    public AbilityId Id => AbilityId.Scanner;
    public bool IsActive => Time.time < readyAt;
    public string Status => IsActive ? (readyAt - Time.time).ToString("0.0") + "s" : "READY";

    private void Awake()
    {
        properties = new MaterialPropertyBlock();
        if (pulseMaterial == null)
        {
            Shader shader = Shader.Find("Low Signal/Sonar Pulse");
            if (shader != null)
                pulseMaterial = new Material(shader);
        }
    }

    public bool Use()
    {
        if (Time.time < readyAt)
            return false;
        readyAt = Time.time + cooldown;
        StartCoroutine(Sweep(transform.position));
        return true;
    }

    private IEnumerator Sweep(Vector3 centre)
    {
        var waiting = new List<PickupItem>();
        foreach (PickupItem item in FindObjectsByType<PickupItem>(FindObjectsSortMode.None))
            if (item.CanPickUp && !item.IsHeld && item.isActiveAndEnabled && Vector3.Distance(item.transform.position, centre) <= range)
                waiting.Add(item);
        GameObject sphere = null;
        MeshRenderer shell = null;
        if (pulseMaterial != null)
        {
            sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = "Scanner Sweep";
            Destroy(sphere.GetComponent<Collider>());
            sphere.transform.position = centre;
            shell = sphere.GetComponent<MeshRenderer>();
            shell.sharedMaterial = pulseMaterial;
            shell.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            shell.receiveShadows = false;
        }
        float duration = range / sweepSpeed;
        for (float elapsed = 0f; elapsed < duration; elapsed += Time.deltaTime)
        {
            float radius = range * elapsed / duration;
            // Each item glints the moment the sweep reaches it.
            for (int i = waiting.Count - 1; i >= 0; i--)
            {
                if (waiting[i] == null) { waiting.RemoveAt(i); continue; }
                if (Vector3.Distance(waiting[i].transform.position, centre) > radius) continue;
                StartCoroutine(Glint(waiting[i]));
                waiting.RemoveAt(i);
            }
            if (sphere != null)
            {
                Color color = sweepColor;
                color.a *= 1f - elapsed / duration;
                sphere.transform.localScale = Vector3.one * (radius * 2f);
                properties.SetColor(ColorId, color);
                properties.SetFloat(RimPowerId, rimSharpness);
                shell.SetPropertyBlock(properties);
            }
            yield return null;
        }
        foreach (PickupItem item in waiting)
            if (item != null) StartCoroutine(Glint(item));
        if (sphere != null)
            Destroy(sphere);
    }

    // A warm light that flares three times at the item and fades.
    private IEnumerator Glint(PickupItem item)
    {
        var glint = new GameObject("Scanner Glint");
        glint.transform.SetParent(item.transform, false);
        glint.transform.localPosition = Vector3.up * 0.4f;
        Light light = glint.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = glintColor;
        light.range = glintRange;
        light.shadows = LightShadows.None;
        for (float elapsed = 0f; elapsed < glintSeconds && glint != null; elapsed += Time.deltaTime)
        {
            float t = elapsed / glintSeconds;
            float flare = Mathf.Pow(Mathf.Abs(Mathf.Sin(t * Mathf.PI * 3f)), 2f);
            light.intensity = glintIntensity * flare * (1f - t * 0.5f);
            yield return null;
        }
        if (glint != null)
            Destroy(glint);
    }
}
