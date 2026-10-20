using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Attach to the parent of the god-ray quads. Each child quad is a soft shaft of sunlight slanting down from the
/// surface along the sun's direction. Every frame each shaft turns about its own length to face the camera, so it
/// never shows its thin edge, and it slowly brightens, fades and drifts, like light flickering through waves.
/// </summary>
public class LightShafts : MonoBehaviour
{
    private static readonly int ColorId = Shader.PropertyToID("_BaseColor");

    [Tooltip("The sun the shafts slant away from. Found in the scene when left empty.")]
    [SerializeField] private Light sun;
    [SerializeField] private Color shaftColor = new Color(0.8f, 0.96f, 1f, 0.14f);
    [Tooltip("How much each shaft's brightness wavers, as a fraction of its colour's alpha.")]
    [SerializeField, Range(0f, 1f)] private float flicker = 0.5f;
    [Tooltip("Flickers per second.")]
    [SerializeField, Min(0f)] private float flickerSpeed = 0.08f;
    [Tooltip("How far each shaft wanders sideways, in metres.")]
    [SerializeField, Min(0f)] private float wander = 1.2f;

    private readonly List<Transform> shafts = new List<Transform>();
    private readonly List<Renderer> renderers = new List<Renderer>();
    private readonly List<Vector3> anchors = new List<Vector3>();
    private MaterialPropertyBlock properties;
    private Camera viewer;

    private void Awake()
    {
        properties = new MaterialPropertyBlock();
        if (sun == null)
            sun = FindSun();
        foreach (Transform child in transform)
        {
            Renderer shaftRenderer = child.GetComponent<Renderer>();
            if (shaftRenderer == null)
                continue;
            shafts.Add(child);
            renderers.Add(shaftRenderer);
            anchors.Add(child.position);
        }
    }

    /// <summary>The scene's sun, or else its first directional light.</summary>
    public static Light FindSun()
    {
        if (RenderSettings.sun != null)
            return RenderSettings.sun;
        foreach (Light light in FindObjectsByType<Light>(FindObjectsSortMode.None))
            if (light.type == LightType.Directional)
                return light;
        return null;
    }

    private void LateUpdate()
    {
        if (viewer == null)
            viewer = Camera.main;
        if (viewer == null || sun == null)
            return;
        // The quads are one unit tall along their local Y; that axis points back up toward the sun.
        Vector3 axis = -sun.transform.forward;
        Vector3 side = Vector3.Cross(axis, Vector3.up).normalized;
        float time = Time.time * flickerSpeed * Mathf.PI * 2f;
        for (int i = 0; i < shafts.Count; i++)
        {
            float seed = i * 2.39f;
            shafts[i].position = anchors[i] + side * (Mathf.Sin(time * 0.37f + seed) * wander);
            // Spin about the shaft's length so its face points as directly at the camera as possible.
            Vector3 toCamera = Vector3.ProjectOnPlane(viewer.transform.position - shafts[i].position, axis);
            if (toCamera.sqrMagnitude > 0.0001f)
                shafts[i].rotation = Quaternion.LookRotation(-toCamera.normalized, axis);
            Color color = shaftColor;
            color.a *= 1f - flicker * (0.5f + 0.5f * Mathf.Sin(time + seed));
            properties.SetColor(ColorId, color);
            renderers[i].SetPropertyBlock(properties);
        }
    }
}
