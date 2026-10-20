using UnityEngine;
using UnityEngine.Rendering.Universal;
#if UNITY_EDITOR
using System.IO;
using System.Linq;
using UnityEditor;
#endif

/// <summary>
/// Attach to the sun (a directional light). Shines a soft, slowly shifting caustic pattern through the light's cookie,
/// so gentle bands of light lie across the sea floor, rocks, coral and the robot.
/// Every rendered frame, the two caustic frames either side of the current moment are blended into a small texture
/// used as the cookie, so the pattern changes continuously at the display's full rate (180 Hz on a 180 Hz screen)
/// instead of stepping between frames. The defaults keep the motion very subtle: a long loop, an almost still drift,
/// and a faint swell in scale.
/// Adding the component fills Frames from Textures/Caustics Smooth; right-click the component header and choose
/// Load Smooth Caustic Frames to refill it.
/// </summary>
[RequireComponent(typeof(Light))]
public class CausticsAnimator : MonoBehaviour
{
    private static readonly int NextTexId = Shader.PropertyToID("_NextTex");
    private static readonly int BlendId = Shader.PropertyToID("_Blend");

    [Tooltip("Looping caustic frames, in order. Neighbouring frames should differ only slightly.")]
    [SerializeField] private Texture2D[] frames = new Texture2D[0];
    [Tooltip("Seconds to flow through every frame once. Higher moves less.")]
    [SerializeField, Min(1f)] private float cycleSeconds = 60f;
    [Tooltip("Size of one caustic tile on the ground, in metres. Larger makes broader, lazier ripples.")]
    [SerializeField] private Vector2 tileSize = new Vector2(16f, 16f);
    [Tooltip("How far the whole pattern slides each second, in metres. Zero holds it in place.")]
    [SerializeField] private Vector2 drift = new Vector2(0.015f, 0.01f);
    [Tooltip("How much the tile size swells and shrinks, as a fraction of its size. 0 turns it off.")]
    [SerializeField, Range(0f, 0.2f)] private float breathing = 0.03f;
    [Tooltip("Seconds for one slow swell and settle of the pattern's scale.")]
    [SerializeField, Min(1f)] private float breathingSeconds = 24f;
    [Tooltip("Blends neighbouring frames together. Filled in automatically from Shallows/Shaders/CausticBlend.")]
    [SerializeField] private Shader blendShader;

    private Light sun;
    private UniversalAdditionalLightData lightData;
    private Material blendMaterial;
    private RenderTexture blended;

    /// <summary>The smoothly blended caustic pattern this frame (pattern in every channel), or null when not blending.</summary>
    public Texture Pattern => blended;
    /// <summary>The ground size of one caustic tile right now, in metres, including the breathing swell.</summary>
    public Vector2 CurrentTileSize => lightData != null ? lightData.lightCookieSize : tileSize;
    /// <summary>How far the pattern has drifted so far, in metres.</summary>
    public Vector2 CurrentOffset => lightData != null ? lightData.lightCookieOffset : Vector2.zero;

    private void Awake()
    {
        sun = GetComponent<Light>();
        lightData = GetComponent<UniversalAdditionalLightData>();
        if (lightData == null)
            lightData = gameObject.AddComponent<UniversalAdditionalLightData>();
        lightData.lightCookieSize = tileSize;
        if (blendShader == null)
            blendShader = Shader.Find("Hidden/Low Signal/Caustic Blend");
        // Without a working blend shader, swap whole frames rather than risk a black cookie blocking the sun.
        if (blendShader != null && blendShader.isSupported)
            blendMaterial = new Material(blendShader) { hideFlags = HideFlags.HideAndDontSave };
        else
            Debug.LogWarning("CausticsAnimator has no usable blend shader, so the caustics will step between frames.", this);
    }

    private void LateUpdate()
    {
        float time = Time.time;
        // Two out-of-step waves, so the swell never feels like a mechanical pulse.
        float swell = Mathf.Sin(time * Mathf.PI * 2f / breathingSeconds) * 0.7f
                      + Mathf.Sin(time * Mathf.PI * 2f / (breathingSeconds * 1.618f) + 1.3f) * 0.3f;
        lightData.lightCookieSize = tileSize * (1f + breathing * swell);
        lightData.lightCookieOffset = drift * time;

        if (frames == null || frames.Length == 0)
            return;
        float position = Mathf.Repeat(time / cycleSeconds, 1f) * frames.Length;
        int index = Mathf.FloorToInt(position) % frames.Length;
        Texture2D from = frames[index];
        Texture2D to = frames[(index + 1) % frames.Length];
        if (from == null)
            return;
        if (blendMaterial == null || to == null)
        {
            sun.cookie = from;
            return;
        }

        // Mix the two frames either side of this moment, so the pattern flows instead of stepping.
        EnsureTarget(from);
        blendMaterial.SetTexture(NextTexId, to);
        blendMaterial.SetFloat(BlendId, position - Mathf.Floor(position));
        Graphics.Blit(from, blended, blendMaterial);
        if (sun.cookie != blended)
            sun.cookie = blended;
    }

    private void EnsureTarget(Texture2D like)
    {
        if (blended != null && blended.width == like.width && blended.height == like.height)
            return;
        ReleaseTarget();
        // Linear, like the frames: the cookie holds light strength, not a colour to gamma-correct.
        blended = new RenderTexture(like.width, like.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear)
        {
            name = "Blended Caustics",
            // The pattern tiles across the ground, and mipmaps keep it from shimmering in the distance.
            wrapMode = TextureWrapMode.Repeat,
            filterMode = FilterMode.Trilinear,
            useMipMap = true,
            autoGenerateMips = true,
            hideFlags = HideFlags.HideAndDontSave,
        };
        blended.Create();
    }

    private void ReleaseTarget()
    {
        if (blended == null)
            return;
        if (sun != null && sun.cookie == blended)
            sun.cookie = null;
        blended.Release();
        Destroy(blended);
        blended = null;
    }

    private void OnDisable()
    {
        ReleaseTarget();
        if (sun != null)
            sun.cookie = frames != null && frames.Length > 0 ? frames[0] : null;
    }

    private void OnDestroy()
    {
        if (blendMaterial != null)
            Destroy(blendMaterial);
    }

#if UNITY_EDITOR
    private const string FramesFolder = "Assets/_LowSignal/World/Shallows/Textures/Caustics Smooth";
    private const string BlendShaderPath = "Assets/_LowSignal/World/Shallows/Shaders/CausticBlend.shader";

    private void Reset()
    {
        LoadFrames();
    }

    private void OnValidate()
    {
        // Keep a reference to the blend shader, so it is always included in builds.
        if (blendShader == null)
            blendShader = AssetDatabase.LoadAssetAtPath<Shader>(BlendShaderPath);
        // Frames that were deleted or replaced leave holes in the list; refill it once the editor is ready.
        if (frames != null && frames.Length > 0 && frames.Any(frame => frame == null))
            EditorApplication.delayCall += () =>
            {
                if (this != null)
                    LoadFrames();
            };
    }

    [ContextMenu("Load Smooth Caustic Frames")]
    private void LoadFrames()
    {
        if (!AssetDatabase.IsValidFolder(FramesFolder))
        {
            Debug.LogWarning("CausticsAnimator: no frames folder at " + FramesFolder, this);
            return;
        }
        Undo.RecordObject(this, "Load Smooth Caustic Frames");
        frames = AssetDatabase.FindAssets("t:Texture2D", new[] { FramesFolder })
            .Select(AssetDatabase.GUIDToAssetPath)
            .OrderBy(Path.GetFileName)
            .Select(AssetDatabase.LoadAssetAtPath<Texture2D>)
            .ToArray();
        if (blendShader == null)
            blendShader = AssetDatabase.LoadAssetAtPath<Shader>(BlendShaderPath);
        // Show the first frame while editing too; the animation runs in Play mode.
        Light light = GetComponent<Light>();
        if (frames.Length > 0 && light.cookie == null)
        {
            Undo.RecordObject(light, "Load Smooth Caustic Frames");
            light.cookie = frames[0];
        }
        EditorUtility.SetDirty(this);
        Debug.Log($"CausticsAnimator: loaded {frames.Length} smooth caustic frames.", this);
    }
#endif
}
