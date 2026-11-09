using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A full-screen backdrop in the HUD's colours: deep ink at the edges brightening to a faint teal glow behind the
/// centre, crossed by fine scanlines like an old display. Drawn at runtime (and in the editor), so it needs no asset.
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(RawImage))]
public sealed class HudBackdrop : MonoBehaviour
{
    [SerializeField] private Color edge = new Color(0.005f, 0.025f, 0.04f);
    [SerializeField] private Color centre = new Color(0.03f, 0.17f, 0.23f);
    [Tooltip("How far each scanline darkens the image.")]
    [SerializeField, Range(0f, 1f)] private float scanlines = 0.18f;

    private Texture2D texture;

    private void OnEnable()
    {
        Draw();
    }

    private void OnValidate()
    {
        if (isActiveAndEnabled)
            Draw();
    }

    private void OnDisable()
    {
        if (texture != null)
            DestroyImmediate(texture);
    }

    private void Draw()
    {
        const int width = 480, height = 270;
        if (texture == null)
        {
            texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.DontSave
            };
        }
        var pixels = new Color32[width * height];
        for (int y = 0; y < height; y++)
        {
            // Every third row is a scanline.
            float line = y % 3 == 0 ? 1f - scanlines : 1f;
            for (int x = 0; x < width; x++)
            {
                // An ellipse matched to the screen's shape, lit a little above the middle where the logo sits.
                float dx = (x + 0.5f) / width * 2f - 1f;
                float dy = ((y + 0.5f) / height * 2f - 1f) - 0.15f;
                float glow = 1f - Mathf.Clamp01(Mathf.Sqrt(dx * dx + dy * dy * 1.6f) / 1.15f);
                Color colour = Color.Lerp(edge, centre, HudStyle.SmoothStep01(glow)) * line;
                colour.a = 1f;
                pixels[y * width + x] = colour;
            }
        }
        texture.SetPixels32(pixels);
        texture.Apply(false);
        GetComponent<RawImage>().texture = texture;
    }
}
