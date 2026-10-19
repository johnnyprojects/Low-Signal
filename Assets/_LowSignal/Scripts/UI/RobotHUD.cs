using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Attach to the Player. Builds the robot HUD at runtime from the Figma design
/// ("Underwater Robot · HUD"): an energy meter for the player's shared energy (top-left)
/// and a Walk/Swim mode indicator with a hold-Space hint (bottom-center).
/// Fonts and icons are loaded from Assets/Resources/RobotHUD; panel shapes are generated.
/// </summary>
[DisallowMultipleComponent]
public class RobotHUD : MonoBehaviour
{
    [Tooltip("Found on this object, or anywhere in the scene, when left empty.")]
    [SerializeField] private PlayerEnergy energySource;
    [Tooltip("Found on this object, or anywhere in the scene, when left empty.")]
    [SerializeField] private UnderwaterSwimController swimController;

    [Header("Energy")]
    [Tooltip("At or below this fraction the meter turns amber.")]
    [SerializeField, Range(0f, 1f)] private float lowThreshold = 0.3f;
    [Tooltip("At or below this fraction the meter turns red and shows the warning.")]
    [SerializeField, Range(0f, 1f)] private float criticalThreshold = 0.1f;

    [Header("Layout")]
    [Tooltip("Canvas reference size. 1280x720 matches the Figma frame, so its pixel values carry over 1:1.")]
    [SerializeField] private Vector2 referenceResolution = new Vector2(1280f, 720f);
    [SerializeField, Min(0f)] private float screenMargin = 32f;
    [SerializeField, Min(1f)] private float modeSwitchSpeed = 12f;
    [SerializeField, Min(0.01f)] private float energyFadeSeconds = 0.3f;

    private static readonly Color Glass = new Color(0.02f, 0.09f, 0.13f, 0.82f);
    private static readonly Color Cyan = new Color(0.15f, 0.88f, 1f);
    private static readonly Color Amber = new Color(1f, 0.72f, 0.2f);
    private static readonly Color Red = new Color(1f, 0.3f, 0.36f);
    private static readonly Color Dim = new Color(0.55f, 0.75f, 0.82f);
    private static readonly Color Ink = new Color(0.02f, 0.09f, 0.13f);
    private static readonly Color Ice = new Color(0.9f, 0.98f, 1f);
    private static readonly Color KeyShadow = new Color(0.05f, 0.45f, 0.6f);

    private const float GlowSize = 8f;
    private const float EnergyWidth = 320f;
    private const float EnergyHeight = 24f;

    private GameObject canvasObject;
    private Font headingFont;
    private Font boldFont;
    private Font semiBoldFont;

    private RectTransform energyPanel;
    private CanvasGroup energyCanvasGroup;
    private Image energyOutline;
    private Image energyFill;
    private Text percentText;
    private Text warningText;
    private int shownPercent = -1;
    private Color shownColor;

    private RectTransform modePanel;
    private RectTransform highlight;
    private Image walkIcon;
    private Image swimIcon;
    private Text walkLabel;
    private Text swimLabel;
    private RectTransform keycap;
    private float modeBlend;

    private void Awake()
    {
        if (energySource == null)
            energySource = GetComponent<PlayerEnergy>();
        if (energySource == null)
            energySource = FindAnyObjectByType<PlayerEnergy>();
        if (swimController == null)
            swimController = GetComponent<UnderwaterSwimController>();
        if (swimController == null)
            swimController = FindAnyObjectByType<UnderwaterSwimController>();

        headingFont = LoadFont("Michroma-Regular");
        boldFont = LoadFont("TitilliumWeb-Bold");
        semiBoldFont = LoadFont("TitilliumWeb-SemiBold");

        BuildCanvas();
        // Each widget is skipped when the script it reports on is missing.
        if (energySource != null)
            BuildEnergyMeter();
        if (swimController != null)
            BuildModeToggle();
    }

    private void LateUpdate()
    {
        if (energyPanel != null)
            UpdateEnergyMeter();
        if (modePanel != null)
            UpdateModeToggle(Time.unscaledDeltaTime);
    }

    private void OnDestroy()
    {
        if (canvasObject != null)
            Destroy(canvasObject);
    }

    // ---------- Energy meter ----------

    private void UpdateEnergyMeter()
    {
        float energy = energySource.Energy01;
        bool shouldShow = energy < 1f;
        if (shouldShow && !energyPanel.gameObject.activeSelf)
            energyPanel.gameObject.SetActive(true);

        float targetAlpha = shouldShow ? 0.8f : 0f;
        energyCanvasGroup.alpha = Mathf.MoveTowards(energyCanvasGroup.alpha, targetAlpha,
            0.8f * Time.unscaledDeltaTime / Mathf.Max(0.01f, energyFadeSeconds));
        if (!shouldShow && energyCanvasGroup.alpha <= 0f)
            energyPanel.gameObject.SetActive(false);
        if (!energyPanel.gameObject.activeSelf)
            return;

        bool critical = energy <= criticalThreshold;
        Color accent = critical ? Red : energy <= lowThreshold ? Amber : Cyan;
        energyFill.rectTransform.anchorMax = new Vector2(energy, 1f);

        if (accent != shownColor)
        {
            shownColor = accent;
            energyOutline.color = WithAlpha(accent, 0.55f);
            percentText.color = warningText.color = accent;
            energyFill.color = accent;

            warningText.enabled = critical;
        }

        if (critical)
        {
            // Blink the warning so it reads as urgent without hiding it.
            float pulse = 0.55f + 0.45f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 3f));
            warningText.color = WithAlpha(accent, pulse);
        }

        int percent = Mathf.CeilToInt(energy * 100f);
        if (percent != shownPercent)
        {
            shownPercent = percent;
            percentText.text = percent + "%";
        }
    }

    private void BuildEnergyMeter()
    {
        energyPanel = CreateRect("Energy Meter", canvasObject.transform);
        energyPanel.anchorMin = energyPanel.anchorMax = energyPanel.pivot = new Vector2(0.5f, 1f);
        energyPanel.anchoredPosition = new Vector2(0f, -screenMargin);
        energyPanel.sizeDelta = new Vector2(EnergyWidth, EnergyHeight);
        energyCanvasGroup = energyPanel.gameObject.AddComponent<CanvasGroup>();
        energyCanvasGroup.alpha = 0f;
        // Center the track itself, with equal insets and no generated glow.
        AddImage(Stretch(CreateRect("Glass", energyPanel)), RoundedSprite(2, 0f)).color = WithAlpha(Glass, 0.55f);
        energyOutline = AddImage(Stretch(CreateRect("Outline", energyPanel)), RoundedSprite(2, 1f));
        AddText(Place(CreateRect("Label", energyPanel), 5f, 27f, 62f, 16f), headingFont, 8, "ENERGY", Dim, TextAnchor.MiddleLeft);
        percentText = AddText(Place(CreateRect("Percent", energyPanel), EnergyWidth - 51f, 27f, 46f, 16f), boldFont, 10, "100%", Cyan, TextAnchor.MiddleRight);

        RectTransform barTrack = Place(CreateRect("Bar Track", energyPanel), 5f, 5f, EnergyWidth - 10f, EnergyHeight - 10f);
        AddImage(Stretch(CreateRect("Background", barTrack)), RoundedSprite(1, 0f)).color = WithAlpha(Dim, 0.16f);
        RectTransform fillRect = Stretch(CreateRect("Bar Fill", barTrack));
        fillRect.anchorMax = new Vector2(1f, 1f);
        energyFill = AddImage(fillRect, RoundedSprite(1, 0f));

        warningText = AddText(Place(CreateRect("Warning", energyPanel), 80f, 27f, EnergyWidth - 160f, 16f), boldFont, 9, "LOW POWER", Red, TextAnchor.MiddleCenter);
        warningText.enabled = false;
        UpdateEnergyMeter();
    }

    // ---------- Mode toggle ----------

    private void UpdateModeToggle(float deltaTime)
    {
        bool swimming = swimController.IsSwimming;

        // Slide the highlight between WALK and SWIM rather than snapping.
        modeBlend = Mathf.MoveTowards(modeBlend, swimming ? 1f : 0f, modeSwitchSpeed * deltaTime);
        float t = Mathf.SmoothStep(0f, 1f, modeBlend);
        highlight.anchoredPosition = new Vector2(Mathf.Lerp(13f, 106f, t), -13f);
        Color walkColor = Color.Lerp(Ink, Dim, t);
        Color swimColor = Color.Lerp(Dim, Ink, t);
        walkIcon.color = walkLabel.color = walkColor;
        swimIcon.color = swimLabel.color = swimColor;

        // The keycap sits pressed onto its shadow for as long as the swim key is held.
        keycap.anchoredPosition = new Vector2(keycap.anchoredPosition.x, swimming ? -61f : -58f);
    }

    private void BuildModeToggle()
    {
        modePanel = CreateRect("Mode Toggle", canvasObject.transform);
        modePanel.anchorMin = modePanel.anchorMax = modePanel.pivot = new Vector2(0.5f, 0f);
        modePanel.anchoredPosition = new Vector2(0f, screenMargin);
        modePanel.sizeDelta = new Vector2(212f, 100f);
        BuildGlassPanel(modePanel, 14, out Image glow, out Image outline);
        glow.color = WithAlpha(Cyan, 0.3f);
        outline.color = WithAlpha(Cyan, 0.4f);

        Image track = AddImage(Place(CreateRect("Segmented", modePanel), 10f, 10f, 192f, 38f), RoundedSprite(10, 0f));
        track.color = new Color(0f, 0.02f, 0.04f, 0.6f);

        highlight = Place(CreateRect("Highlight", modePanel), 13f, 13f, 93f, 32f);
        Image highlightGlow = AddImage(CreateRect("Glow", highlight), GlowSprite(8, 12));
        highlightGlow.rectTransform.anchorMin = Vector2.zero;
        highlightGlow.rectTransform.anchorMax = Vector2.one;
        highlightGlow.rectTransform.offsetMin = new Vector2(-12f, -12f);
        highlightGlow.rectTransform.offsetMax = new Vector2(12f, 12f);
        highlightGlow.color = WithAlpha(Cyan, 0.55f);
        AddImage(Stretch(CreateRect("Fill", highlight)), RoundedSprite(8, 0f)).color = Cyan;

        BuildOption("Walk", 13f, "walk", "WALK", out walkIcon, out walkLabel);
        BuildOption("Swim", 106f, "swim", "SWIM", out swimIcon, out swimLabel);

        AddImage(Place(CreateRect("Keycap Shadow", modePanel), 27f, 61f, 64f, 28f), RoundedSprite(6, 0f)).color = KeyShadow;
        keycap = Place(CreateRect("Keycap", modePanel), 27f, 58f, 64f, 28f);
        AddImage(Stretch(CreateRect("Face", keycap)), RoundedSprite(6, 0f)).color = Ice;
        AddText(Stretch(CreateRect("Key", keycap)), headingFont, 10, "SPACE", Ink, TextAnchor.MiddleCenter);
        AddText(Place(CreateRect("Hint", modePanel), 99f, 58f, 100f, 28f), semiBoldFont, 13, "HOLD TO SWIM", Dim, TextAnchor.MiddleLeft);

        modeBlend = swimController.IsSwimming ? 1f : 0f;
    }

    private void BuildOption(string name, float x, string icon, string label, out Image iconImage, out Text labelText)
    {
        RectTransform option = Place(CreateRect(name, modePanel), x, 13f, 93f, 32f);
        iconImage = AddImage(Place(CreateRect("Icon", option), 12f, 7f, 18f, 18f), LoadIcon(icon));
        labelText = AddText(Place(CreateRect("Label", option), 36f, 0f, 57f, 32f), boldFont, 14, label, Dim, TextAnchor.MiddleLeft);
    }

    // ---------- Shared building blocks ----------

    private void BuildCanvas()
    {
        canvasObject = new GameObject("Robot HUD", typeof(Canvas), typeof(CanvasScaler));
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 10;
        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = referenceResolution;
        scaler.matchWidthOrHeight = 0.5f;
    }

    /// <summary>Adds the outer glow, dark glass fill and outline that every HUD panel shares.</summary>
    private static void BuildGlassPanel(RectTransform panel, int radius, out Image glow, out Image outline)
    {
        glow = AddImage(CreateRect("Glow", panel), GlowSprite(radius, (int)GlowSize));
        glow.rectTransform.anchorMin = Vector2.zero;
        glow.rectTransform.anchorMax = Vector2.one;
        glow.rectTransform.offsetMin = new Vector2(-GlowSize, -GlowSize);
        glow.rectTransform.offsetMax = new Vector2(GlowSize, GlowSize);
        AddImage(Stretch(CreateRect("Glass", panel)), RoundedSprite(radius, 0f)).color = Glass;
        outline = AddImage(Stretch(CreateRect("Outline", panel)), RoundedSprite(radius, 1.5f));
    }

    private static RectTransform CreateRect(string name, Transform parent)
    {
        GameObject child = new GameObject(name, typeof(RectTransform));
        child.transform.SetParent(parent, false);
        return (RectTransform)child.transform;
    }

    /// <summary>Positions a rect by its top-left corner, matching Figma's coordinates.</summary>
    private static RectTransform Place(RectTransform rect, float x, float y, float width, float height)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(x, -y);
        rect.sizeDelta = new Vector2(width, height);
        return rect;
    }

    private static RectTransform Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        return rect;
    }

    private static Image AddImage(RectTransform rect, Sprite sprite)
    {
        Image image = rect.gameObject.AddComponent<Image>();
        image.sprite = sprite;
        image.type = sprite != null && sprite.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
        image.raycastTarget = false;
        return image;
    }

    private static Text AddText(RectTransform rect, Font font, int size, string value, Color color, TextAnchor alignment)
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

    private static Color WithAlpha(Color color, float alpha)
    {
        color.a = alpha;
        return color;
    }

    private Font LoadFont(string fileName)
    {
        Font font = Resources.Load<Font>("RobotHUD/" + fileName);
        if (font != null)
            return font;
        Debug.LogWarning("RobotHUD could not load font Resources/RobotHUD/" + fileName + "; using the built-in font.", this);
        return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
    }

    private Sprite LoadIcon(string iconName)
    {
        Texture2D texture = Resources.Load<Texture2D>("RobotHUD/icon_" + iconName);
        if (texture == null)
        {
            Debug.LogWarning("RobotHUD could not load icon Resources/RobotHUD/icon_" + iconName + ".", this);
            return null;
        }
        return Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f));
    }

    // ---------- Generated sprites ----------
    // Rounded shapes are drawn into small textures and 9-sliced, so no art assets are needed for panels.

    /// <summary>A rounded rectangle; a border above zero draws only an outline of that width.</summary>
    private static Sprite RoundedSprite(int radius, float border)
    {
        return BuildSprite(radius * 2 + 4, radius + 2, distance => ShapeAlpha(distance, border), radius);
    }

    /// <summary>An exact circle, drawn unsliced so it stays round at its intended size.</summary>
    private static Sprite CircleSprite(int diameter, float border)
    {
        return BuildSprite(diameter, 0, distance => ShapeAlpha(distance, border), diameter / 2);
    }

    private static float ShapeAlpha(float distance, float border)
    {
        float fill = Mathf.Clamp01(0.5f - distance);
        return border > 0f ? fill * Mathf.Clamp01(distance + border + 0.5f) : fill;
    }

    /// <summary>A soft halo that fades out over the given distance outside a rounded rectangle.</summary>
    private static Sprite GlowSprite(int radius, int spread)
    {
        int size = (radius + spread) * 2 + 4;
        return BuildSprite(size, radius + spread + 2, distance =>
        {
            // Nothing inside the shape, so translucent panels are not tinted by their own glow.
            if (distance < 0f)
                return 0f;
            float falloff = 1f - Mathf.Clamp01(distance / spread);
            return falloff * falloff;
        }, radius, spread);
    }

    private static Sprite BuildSprite(int size, int sliceBorder, System.Func<float, float> alphaForDistance, int radius, int inset = 0)
    {
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.filterMode = FilterMode.Bilinear;
        float center = size * 0.5f;
        // The shape reaches the texture edge (less any glow spread), so sliced panels keep their exact size.
        float halfExtent = center - inset;
        Color32[] pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                Vector2 p = new Vector2(Mathf.Abs(x + 0.5f - center), Mathf.Abs(y + 0.5f - center));
                Vector2 q = p - new Vector2(halfExtent - radius, halfExtent - radius);
                float distance = Vector2.Max(q, Vector2.zero).magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0f) - radius;
                byte alpha = (byte)Mathf.RoundToInt(alphaForDistance(distance) * 255f);
                pixels[y * size + x] = new Color32(255, 255, 255, alpha);
            }
        }
        texture.SetPixels32(pixels);
        texture.Apply(false, true);
        return Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
            SpriteMeshType.FullRect, new Vector4(sliceBorder, sliceBorder, sliceBorder, sliceBorder));
    }
}
