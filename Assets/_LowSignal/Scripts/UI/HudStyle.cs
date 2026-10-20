using System;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>
/// The robot HUD's shared look: its colours, fonts, UI building blocks, keycaps and generated shapes. Shapes are
/// drawn into small textures at runtime (panels are 9-sliced), so no art assets are needed for panels, badges or
/// glows. The style is angular: chamfered panels with two opposite corners cut, rhombus gauges and slanted cells.
/// Positions follow Figma: rects are placed by their top-left corner in a 1280x720 reference canvas.
/// </summary>
public static class HudStyle
{
    public const float GlowSize = 8f;
    /// <summary>How far keycaps sink onto their shadow when pressed.</summary>
    public const float KeyTravel = 3f;

    public static readonly Color Glass = new Color(0.02f, 0.09f, 0.13f, 0.82f);
    public static readonly Color Cyan = new Color(0.15f, 0.88f, 1f);
    public static readonly Color Amber = new Color(1f, 0.72f, 0.2f);
    public static readonly Color Red = new Color(1f, 0.3f, 0.36f);
    public static readonly Color Dim = new Color(0.55f, 0.75f, 0.82f);
    public static readonly Color Ink = new Color(0.02f, 0.09f, 0.13f);
    public static readonly Color Ice = new Color(0.9f, 0.98f, 1f);
    public static readonly Color KeyShadow = new Color(0.05f, 0.45f, 0.6f);

    public static Color WithAlpha(Color color, float alpha)
    {
        color.a = alpha;
        return color;
    }

    public static float SmoothStep01(float t)
    {
        return t * t * (3f - 2f * t);
    }

    /// <summary>Loads a HUD font from Resources/RobotHUD, falling back to the built-in font.</summary>
    public static Font LoadFont(string fileName, Object context)
    {
        Font font = Resources.Load<Font>("RobotHUD/" + fileName);
        if (font != null)
            return font;
        Debug.LogWarning("Robot HUD could not load font Resources/RobotHUD/" + fileName + "; using the built-in font.", context);
        return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
    }

    /// <summary>Loads a white HUD icon from Resources/RobotHUD/icon_{name}.png, or null with a warning.</summary>
    public static Sprite LoadIcon(string iconName, Object context)
    {
        Texture2D texture = Resources.Load<Texture2D>("RobotHUD/icon_" + iconName);
        if (texture == null)
        {
            Debug.LogWarning("Robot HUD could not load icon Resources/RobotHUD/icon_" + iconName + ".", context);
            return null;
        }
        return Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f));
    }

    // ---------- UI building blocks ----------

    /// <summary>A screen-space overlay canvas that scales with the screen from the reference resolution.</summary>
    public static GameObject CreateCanvas(string name, Vector2 referenceResolution, int sortingOrder)
    {
        GameObject canvasObject = new GameObject(name, typeof(Canvas), typeof(CanvasScaler));
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortingOrder;
        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = referenceResolution;
        scaler.matchWidthOrHeight = 0.5f;
        return canvasObject;
    }

    public static RectTransform CreateRect(string name, Transform parent)
    {
        GameObject child = new GameObject(name, typeof(RectTransform));
        child.transform.SetParent(parent, false);
        return (RectTransform)child.transform;
    }

    /// <summary>Positions a rect by its top-left corner, matching Figma's coordinates.</summary>
    public static RectTransform Place(RectTransform rect, float x, float y, float width, float height)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(x, -y);
        rect.sizeDelta = new Vector2(width, height);
        return rect;
    }

    /// <summary>Like Place, but pivoted on the centre so the rect turns in place.</summary>
    public static RectTransform PlaceCentred(RectTransform rect, float x, float y, float width, float height)
    {
        Place(rect, x, y, width, height);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(x + width * 0.5f, -(y + height * 0.5f));
        return rect;
    }

    /// <summary>Fills the parent, grown outward by the margin; the pivot stays central so it turns in place.</summary>
    public static RectTransform Stretch(RectTransform rect, float margin = 0f)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = new Vector2(-margin, -margin);
        rect.offsetMax = new Vector2(margin, margin);
        return rect;
    }

    public static Image AddImage(RectTransform rect, Sprite sprite)
    {
        Image image = rect.gameObject.AddComponent<Image>();
        image.sprite = sprite;
        image.type = sprite != null && sprite.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
        image.raycastTarget = false;
        return image;
    }

    public static Text AddText(RectTransform rect, Font font, int size, string value, Color color, TextAnchor alignment)
    {
        Text text = rect.gameObject.AddComponent<Text>();
        text.font = font;
        text.fontSize = size;
        text.color = color;
        text.alignment = alignment;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;
        text.text = value;
        return text;
    }

    /// <summary>
    /// Adds the outer glow, dark glass fill and outline of an angular HUD panel: the top-left and bottom-right
    /// corners are cut by `cut` pixels, the other two only nicked.
    /// </summary>
    public static void BuildAngularPanel(RectTransform panel, int cut, out Image glow, out Image outline)
    {
        glow = AddImage(Stretch(CreateRect("Glow", panel), GlowSize), ChamferGlowSprite(cut, (int)GlowSize));
        AddImage(Stretch(CreateRect("Glass", panel)), ChamferSprite(cut, 0f)).color = Glass;
        outline = AddImage(Stretch(CreateRect("Outline", panel)), ChamferSprite(cut, 1.5f));
    }

    // ---------- Keycaps ----------

    /// <summary>A keyboard key hint: a pale face over a blue shadow, which sinks onto the shadow while held.</summary>
    public sealed class Keycap
    {
        public readonly RectTransform Shadow;
        public readonly RectTransform Face;
        public float Pressed { get; private set; }
        private readonly Image faceImage;
        private readonly Vector2 size;
        private readonly Func<bool> held;
        private Vector2 position;

        public Keycap(string name, RectTransform parent, Font font, string label, int fontSize, Vector2 size, Func<bool> held)
        {
            this.size = size;
            this.held = held;
            Shadow = CreateRect(name + " Key Shadow", parent);
            AddImage(Stretch(CreateRect("Shadow", Shadow)), RoundedSprite(6, 0f)).color = KeyShadow;
            Face = CreateRect(name + " Key", parent);
            faceImage = AddImage(Stretch(CreateRect("Face", Face)), RoundedSprite(6, 0f));
            AddText(Stretch(CreateRect("Label", Face)), font, fontSize, label, Ink, TextAnchor.MiddleCenter);
        }

        /// <summary>Places the key by its top-left corner; the shadow sits KeyTravel below it.</summary>
        public void PlaceAt(float x, float y)
        {
            position = new Vector2(x, y);
            Place(Shadow, x, y + KeyTravel, size.x, size.y);
            Place(Face, x, y, size.x, size.y);
        }

        public void Animate(float deltaTime, float pressSeconds, Color pressedTint)
        {
            Pressed = Mathf.MoveTowards(Pressed, held() ? 1f : 0f, deltaTime / Mathf.Max(0.01f, pressSeconds));
            Face.anchoredPosition = new Vector2(position.x, -(position.y + KeyTravel * Pressed));
            faceImage.color = Color.Lerp(Ice, pressedTint, Pressed);
        }
    }

    // ---------- Generated sprites ----------

    /// <summary>A rounded rectangle; a border above zero draws only an outline of that width.</summary>
    public static Sprite RoundedSprite(int radius, float border)
    {
        int size = radius * 2 + 4;
        float half = size * 0.5f;
        return BuildShapeSprite(size, size, radius + 2, p => RoundedBoxDistance(Abs(p), half, radius), d => ShapeAlpha(d, border));
    }

    /// <summary>An exact circle, drawn unsliced so it stays round at its intended size.</summary>
    public static Sprite CircleSprite(int diameter, float border)
    {
        float radius = diameter * 0.5f;
        return BuildShapeSprite(diameter, diameter, 0, p => p.magnitude - radius, d => ShapeAlpha(d, border));
    }

    /// <summary>A soft halo that fades out over the given distance outside a rounded rectangle.</summary>
    public static Sprite GlowSprite(int radius, int spread)
    {
        int size = (radius + spread) * 2 + 4;
        float half = size * 0.5f - spread;
        return BuildShapeSprite(size, size, radius + spread + 2, p => RoundedBoxDistance(Abs(p), half, radius), d => HaloAlpha(d, spread));
    }

    /// <summary>
    /// A 9-sliced panel with the top-left and bottom-right corners cut by `cut` pixels and the other two nicked
    /// by a quarter of that, for an aggressive, machined look. A border above zero draws only the outline.
    /// </summary>
    public static Sprite ChamferSprite(int cut, float border)
    {
        int size = cut * 2 + 4;
        float half = size * 0.5f;
        return BuildShapeSprite(size, size, cut + 2, p => ChamferDistance(p, half, cut), d => ShapeAlpha(d, border));
    }

    /// <summary>A soft halo around a ChamferSprite panel, fading out over `spread` pixels outside it.</summary>
    public static Sprite ChamferGlowSprite(int cut, int spread)
    {
        int size = (cut + spread) * 2 + 4;
        float half = size * 0.5f - spread;
        return BuildShapeSprite(size, size, cut + spread + 2, p => ChamferDistance(p, half, cut), d => HaloAlpha(d, spread));
    }

    /// <summary>
    /// A rhombus (a square turned 45 degrees) touching the middle of each texture edge, with slightly softened tips.
    /// A border above zero draws only an outline of that width. Unsliced, so draw it at its square size.
    /// </summary>
    public static Sprite DiamondSprite(int size, float border, float tipRadius = 3f)
    {
        return BuildShapeSprite(size, size, 0, p => DiamondDistance(Abs(p), size * 0.5f - 1f, tipRadius), d => ShapeAlpha(d, border));
    }

    /// <summary>A soft halo around a DiamondSprite of diamondSize, fading out over spread pixels; the texture is larger by the spread.</summary>
    public static Sprite DiamondGlowSprite(int diamondSize, int spread, float tipRadius = 3f)
    {
        int size = diamondSize + spread * 2;
        return BuildShapeSprite(size, size, 0, p => DiamondDistance(Abs(p), diamondSize * 0.5f - 1f, tipRadius), d => HaloAlpha(d, spread));
    }

    /// <summary>A parallelogram leaning right by `slant` (its top edge shifted right by that fraction of its width).</summary>
    public static Sprite SlantedCellSprite(int width, int height, float slant)
    {
        return CoverageSprite(width, height, p =>
        {
            // Map y from -1..1 to a horizontal shift so the top leans right and the bottom left.
            float shift = p.y * slant;
            return Mathf.Abs(p.x - shift) <= 1f - slant;
        });
    }

    /// <summary>
    /// A filled shape from an inside test on points in -1..1 on each axis (y up), antialiased by 4x4 supersampling.
    /// Handy for small icons such as arrows and dot rings.
    /// </summary>
    public static Sprite CoverageSprite(int size, Func<Vector2, bool> inside)
    {
        return CoverageSprite(size, size, inside);
    }

    public static Sprite CoverageSprite(int width, int height, Func<Vector2, bool> inside)
    {
        const int samples = 4;
        return BuildSprite(width, height, 0, (x, y) =>
        {
            int hits = 0;
            for (int sy = 0; sy < samples; sy++)
                for (int sx = 0; sx < samples; sx++)
                {
                    Vector2 p = new Vector2((x + (sx + 0.5f) / samples) / width * 2f - 1f, (y + (sy + 0.5f) / samples) / height * 2f - 1f);
                    if (inside(p))
                        hits++;
                }
            return hits / (float)(samples * samples);
        });
    }

    /// <summary>A chevron arrow pointing up (or down), for key hints.</summary>
    public static Sprite ChevronSprite(int size, bool up)
    {
        return CoverageSprite(size, p =>
        {
            float y = up ? p.y : -p.y;
            // A thick V: inside the outer triangle but outside the inner one shifted down.
            float outer = 0.95f - Mathf.Abs(p.x) * 1.15f;
            float inner = outer - 0.75f;
            return y < outer - 0.35f && y > inner - 0.35f && Mathf.Abs(p.x) < 0.9f;
        });
    }

    /// <summary>Six filled dots evenly spaced around a ring, like an ability wheel.</summary>
    public static Sprite DotRingSprite(int size, int dots = 6, float ringRadius = 0.66f, float dotRadius = 0.25f)
    {
        return CoverageSprite(size, p =>
        {
            for (int i = 0; i < dots; i++)
            {
                float angle = Mathf.PI * 0.5f + i * Mathf.PI * 2f / dots;
                Vector2 centre = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * ringRadius;
                if ((p - centre).sqrMagnitude <= dotRadius * dotRadius)
                    return true;
            }
            return false;
        });
    }

    private static Vector2 Abs(Vector2 p)
    {
        return new Vector2(Mathf.Abs(p.x), Mathf.Abs(p.y));
    }

    private static float ShapeAlpha(float distance, float border)
    {
        float fill = Mathf.Clamp01(0.5f - distance);
        return border > 0f ? fill * Mathf.Clamp01(distance + border + 0.5f) : fill;
    }

    private static float HaloAlpha(float distance, float spread)
    {
        // Nothing inside the shape, so translucent panels are not tinted by their own glow.
        if (distance < 0f)
            return 0f;
        float falloff = 1f - Mathf.Clamp01(distance / spread);
        return falloff * falloff;
    }

    /// <summary>Signed distance from a point (folded into the first quadrant) to a rounded square.</summary>
    private static float RoundedBoxDistance(Vector2 p, float half, float radius)
    {
        Vector2 q = p - new Vector2(half - radius, half - radius);
        return Vector2.Max(q, Vector2.zero).magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0f) - radius;
    }

    /// <summary>Signed distance to a square with its top-left and bottom-right corners cut, and the others nicked.</summary>
    private static float ChamferDistance(Vector2 p, float half, float cut)
    {
        float box = Mathf.Max(Mathf.Abs(p.x) - half, Mathf.Abs(p.y) - half);
        // y is up, so top-left is (-x, +y) and bottom-right is (+x, -y).
        bool majorCorner = (p.x < 0f) == (p.y > 0f);
        float corner = majorCorner ? cut : cut * 0.25f;
        float chamfer = (Mathf.Abs(p.x) + Mathf.Abs(p.y) - (2f * half - corner)) * 0.70710678f;
        return Mathf.Max(box, chamfer);
    }

    /// <summary>Signed distance from a point (folded into the first quadrant) to a rhombus with the given half-diagonal.</summary>
    private static float DiamondDistance(Vector2 p, float halfDiagonal, float tipRadius)
    {
        // Shrinking the rhombus and growing it back by the radius rounds its four tips.
        float shrunk = halfDiagonal - tipRadius * Mathf.Sqrt(2f);
        return (p.x + p.y - shrunk) * 0.70710678f - tipRadius;
    }

    /// <summary>Draws a white shape from a signed distance function of the pixel's offset from the centre (y up).</summary>
    private static Sprite BuildShapeSprite(int width, int height, int sliceBorder, Func<Vector2, float> distanceAt, Func<float, float> alphaForDistance)
    {
        return BuildSprite(width, height, sliceBorder,
            (x, y) => alphaForDistance(distanceAt(new Vector2(x + 0.5f - width * 0.5f, y + 0.5f - height * 0.5f))));
    }

    private static Sprite BuildSprite(int width, int height, int sliceBorder, Func<int, int, float> alphaAt)
    {
        Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.filterMode = FilterMode.Bilinear;
        Color32[] pixels = new Color32[width * height];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                pixels[y * width + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(alphaAt(x, y)) * 255f));
        texture.SetPixels32(pixels);
        texture.Apply(false, true);
        return Sprite.Create(texture, new Rect(0f, 0f, width, height), new Vector2(0.5f, 0.5f), 100f, 0,
            SpriteMeshType.FullRect, new Vector4(sliceBorder, sliceBorder, sliceBorder, sliceBorder));
    }
}
