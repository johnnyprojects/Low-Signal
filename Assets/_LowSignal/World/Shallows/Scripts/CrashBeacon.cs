using UnityEngine;

/// <summary>
/// A failing light on the crashed escape pod: either a distress beacon that blinks on a steady beat, or a sputtering
/// engine glow that flickers unevenly. Drives a light and, optionally, the glow of a renderer through a property
/// block, so the shared material asset is never changed.
/// </summary>
public class CrashBeacon : MonoBehaviour
{
    private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

    [Tooltip("The light to pulse. Its intensity when built is the brightest it gets.")]
    [SerializeField] private Light pulsedLight;
    [Tooltip("A renderer whose emission pulses with the light (optional).")]
    [SerializeField] private Renderer glow;
    [Tooltip("The renderer's emission colour at full brightness.")]
    [SerializeField, ColorUsage(false, true)] private Color emission = Color.red;
    [Tooltip("Blink on a steady beat (a beacon) instead of flickering (a dying engine).")]
    [SerializeField] private bool blink = true;
    [Tooltip("Seconds between blinks.")]
    [SerializeField] private float period = 1.6f;
    [Tooltip("Seconds each blink stays lit.")]
    [SerializeField] private float onTime = 0.2f;

    private float peak;
    private MaterialPropertyBlock properties;

    private void Awake()
    {
        if (pulsedLight != null) peak = pulsedLight.intensity;
        properties = new MaterialPropertyBlock();
    }

    private void Update()
    {
        float level;
        if (blink)
        {
            // A quick double flash, then dark for the rest of the beat.
            float t = Mathf.Repeat(Time.time, period);
            level = t < onTime || (t > onTime * 2f && t < onTime * 3f) ? 1f : 0.04f;
        }
        else
        {
            float noise = Mathf.PerlinNoise(Time.time * 7f, 0.37f);
            level = noise < 0.3f ? 0.1f : Mathf.Lerp(0.45f, 1f, noise);
        }
        if (pulsedLight != null) pulsedLight.intensity = peak * level;
        if (glow != null)
        {
            glow.GetPropertyBlock(properties);
            properties.SetColor(EmissionId, emission * level);
            glow.SetPropertyBlock(properties);
        }
    }
}
