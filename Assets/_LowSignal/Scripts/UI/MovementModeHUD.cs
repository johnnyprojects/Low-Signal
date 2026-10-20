using UnityEngine;
using UnityEngine.UI;
using static HudStyle;

/// <summary>
/// Attach to the Player. Builds the movement hints in the bottom-right corner at runtime, in the robot HUD's style:
///  - A glowing rhombus holding the robot, with the SPACE keycap below it. Entering swim tips the robot clockwise
///    into its swimming pose while the jet-flame version fades in over the second half of the turn; returning to
///    hover plays it backward. The motion follows the controller's movement mode and reverses from wherever it is.
///  - An angular plate above it, against the right edge, with E (rise) stacked over Q (sink), which fades out while
///    swimming since they only work in hover.
/// Each keycap sinks onto its shadow while its key is held. The robot sprites live in Assets/_LowSignal/Hud and are
/// assigned by MovementModeHUDSetup. The Tab and equipped-ability strip sits under the energy panel (RobotHUD).
/// </summary>
[DisallowMultipleComponent]
public class MovementModeHUD : MonoBehaviour
{
    private const float SmallKey = 26f;
    private const float ArrowSize = 12f;
    private const float SpaceKeyWidth = 64f;
    private const float SpaceKeyHeight = 28f;
    private const float PlatePadding = 11f;
    private const float PlateWidth = PlatePadding * 2f + SmallKey + 6f + ArrowSize;
    private const float PlateHeight = PlatePadding * 2f + (SmallKey + KeyTravel) * 2f + 8f;
    // Space between the rise/sink plate and the rhombus's top tip.
    private const float RowGap = 28f;
    private const float GlowSpread = 10f;

    [Tooltip("Found on this object, or anywhere in the scene, when left empty.")]
    [SerializeField] private UnderwaterSwimController swimController;

    [Header("Sprites")]
    [Tooltip("The robot upright.")]
    [SerializeField] private Sprite hoverSprite;
    [Tooltip("The robot in the same upright framing with its jets firing; it is turned into the swimming pose.")]
    [SerializeField] private Sprite swimSprite;

    [Header("Layout")]
    [Tooltip("Canvas reference size; the hints scale with the screen from this.")]
    [SerializeField] private Vector2 referenceResolution = new Vector2(1280f, 720f);
    [Tooltip("Width and height of the rhombus badge, in reference pixels.")]
    [SerializeField, Min(16f)] private float iconSize = 130f;
    [Tooltip("Gap between the hints and the bottom-right corner of the screen, in reference pixels.")]
    [SerializeField] private Vector2 cornerPadding = new Vector2(28f, 20f);

    [Header("Transition")]
    [Tooltip("Degrees the robot turns clockwise as it tips into the swimming pose.")]
    [SerializeField, Range(0f, 180f)] private float rotationAngle = 90f;
    [Tooltip("Seconds for a full hover-to-swim transition. Matches the robot's lean by default.")]
    [SerializeField, Min(0.01f)] private float transitionDuration = 0.6f;

    [Header("Keys")]
    [Tooltip("Keycap colour while its key is held.")]
    [SerializeField] private Color pressedTint = new Color(0.6f, 0.95f, 1f);
    [Tooltip("Seconds for a keycap to press in and spring back.")]
    [SerializeField, Min(0.01f)] private float pressSeconds = 0.08f;

    private GameObject canvasObject;
    private RectTransform root;
    private RectTransform diamond;
    private Image glow;
    private Image outline;
    private RectTransform hoverIcon;
    private RectTransform swimIcon;
    private Image swimImage;
    private RectTransform heightPlate;
    private CanvasGroup heightKeys;
    private Image riseArrow;
    private Image sinkArrow;
    private Keycap spaceKey;
    private Keycap riseKey;
    private Keycap sinkKey;
    private Keycap[] keys;
    // 0 is hover, 1 is swim; moves at a constant rate so a reversal picks up from the current point.
    private float progress;

    private void Awake()
    {
        if (swimController == null)
            swimController = GetComponent<UnderwaterSwimController>();
        if (swimController == null)
            swimController = FindAnyObjectByType<UnderwaterSwimController>();
        if (swimController == null)
        {
            Debug.LogWarning("MovementModeHUD has no UnderwaterSwimController to report on.", this);
            enabled = false;
            return;
        }
        if (hoverSprite == null || swimSprite == null)
            Debug.LogWarning("MovementModeHUD is missing its robot sprites; they are assigned from Assets/_LowSignal/Hud when the editor reloads.", this);

        Build();
        progress = swimController.IsSwimming ? 1f : 0f;
        Apply();
    }

    private void LateUpdate()
    {
        float target = swimController.IsSwimming ? 1f : 0f;
        progress = Mathf.MoveTowards(progress, target, Time.unscaledDeltaTime / transitionDuration);
        Apply();
    }

    private void OnValidate()
    {
        // Lets the layout values be tuned live in Play mode.
        if (root != null)
            Layout();
    }

    private void OnDestroy()
    {
        if (canvasObject != null)
            Destroy(canvasObject);
    }

    private void Apply()
    {
        // Ease in and out; the eased value drives every part so they stay in step.
        float eased = SmoothStep01(progress);
        // Both robots share one turn (negative z is clockwise). The upright robot stays solid underneath while
        // the jet-flame version fades in over it during the second half, so the body never turns see-through.
        Quaternion turn = Quaternion.Euler(0f, 0f, -rotationAngle * eased);
        hoverIcon.localRotation = swimIcon.localRotation = turn;
        swimImage.color = new Color(1f, 1f, 1f, SmoothStep01(Mathf.Clamp01((eased - 0.5f) * 2f)));
        // The badge glows a little brighter while the jets are firing.
        glow.color = WithAlpha(Cyan, Mathf.Lerp(0.3f, 0.55f, eased));
        outline.color = WithAlpha(Cyan, Mathf.Lerp(0.45f, 0.75f, eased));

        // Rise and sink only work while hovering.
        heightKeys.alpha = 1f - eased;
        foreach (Keycap key in keys)
            key.Animate(Time.unscaledDeltaTime, pressSeconds, pressedTint);
        riseArrow.color = Color.Lerp(Dim, Cyan, riseKey.Pressed);
        sinkArrow.color = Color.Lerp(Dim, Cyan, sinkKey.Pressed);
    }

    // ---------- Building ----------

    private void Build()
    {
        canvasObject = CreateCanvas("Movement Mode HUD", referenceResolution, 10);
        root = CreateRect("Controls", canvasObject.transform);
        // Anchored and pivoted on the bottom-right corner, so it stays there at any resolution.
        root.anchorMin = root.anchorMax = root.pivot = new Vector2(1f, 0f);
        Font keyFont = LoadFont("Michroma-Regular", this);

        // Shapes are drawn at twice the badge size so they stay crisp when the canvas scales up.
        int texture = Mathf.RoundToInt(iconSize * 2f);
        diamond = CreateRect("Rhombus", root);
        glow = AddImage(Stretch(CreateRect("Glow", diamond), GlowSpread), DiamondGlowSprite(texture, Mathf.RoundToInt(GlowSpread * 2f), 6f));
        AddImage(Stretch(CreateRect("Glass", diamond)), DiamondSprite(texture, 0f, 6f)).color = Glass;
        outline = AddImage(Stretch(CreateRect("Outline", diamond)), DiamondSprite(texture, 3f, 6f));
        // A faint inner line, echoing the HUD's panel trim.
        AddImage(Stretch(CreateRect("Inner Line", diamond), -iconSize * 0.09f), DiamondSprite(texture, 2f, 6f)).color = WithAlpha(Cyan, 0.14f);
        Image hoverImage = AddImage(Stretch(CreateRect("Hover Robot", diamond)), hoverSprite);
        hoverIcon = hoverImage.rectTransform;
        swimImage = AddImage(Stretch(CreateRect("Swim Robot", diamond)), swimSprite);
        swimIcon = swimImage.rectTransform;
        hoverImage.preserveAspect = swimImage.preserveAspect = true;

        spaceKey = new Keycap("Space", root, keyFont, "SPACE", 10, new Vector2(SpaceKeyWidth, SpaceKeyHeight), () => swimController.IsModeKeyHeld);

        // Rise above sink, each with its arrow, on their own angular glass plate.
        heightPlate = CreateRect("Rise and Sink", root);
        heightKeys = heightPlate.gameObject.AddComponent<CanvasGroup>();
        BuildAngularPanel(heightPlate, 10, out Image plateGlow, out Image plateOutline);
        plateGlow.color = WithAlpha(Cyan, 0.2f);
        plateOutline.color = WithAlpha(Cyan, 0.45f);
        riseKey = new Keycap("Rise", heightPlate, keyFont, swimController.AscendKeyName, 11, new Vector2(SmallKey, SmallKey), () => swimController.IsAscendKeyHeld);
        sinkKey = new Keycap("Sink", heightPlate, keyFont, swimController.DescendKeyName, 11, new Vector2(SmallKey, SmallKey), () => swimController.IsDescendKeyHeld);
        int arrowTexture = Mathf.RoundToInt(ArrowSize * 3f);
        riseArrow = AddImage(CreateRect("Rise Arrow", heightPlate), ChevronSprite(arrowTexture, true));
        sinkArrow = AddImage(CreateRect("Sink Arrow", heightPlate), ChevronSprite(arrowTexture, false));
        keys = new[] { spaceKey, riseKey, sinkKey };
        Layout();
    }

    private void Layout()
    {
        // One column, top to bottom: the rise/sink plate (tucked against the right edge), the rhombus, then SPACE.
        float width = Mathf.Max(iconSize, PlateWidth);
        float diamondTop = PlateHeight + RowGap;
        float spaceTop = diamondTop + iconSize + 10f;
        root.anchoredPosition = new Vector2(-cornerPadding.x, cornerPadding.y);
        root.sizeDelta = new Vector2(width, spaceTop + SpaceKeyHeight + KeyTravel);
        Place(diamond, (width - iconSize) * 0.5f, diamondTop, iconSize, iconSize);
        spaceKey.PlaceAt((width - SpaceKeyWidth) * 0.5f, spaceTop);

        Place(heightPlate, width - PlateWidth, 0f, PlateWidth, PlateHeight);
        float arrowLeft = PlatePadding + SmallKey + 6f;
        float sinkTop = PlatePadding + SmallKey + KeyTravel + 8f;
        riseKey.PlaceAt(PlatePadding, PlatePadding);
        sinkKey.PlaceAt(PlatePadding, sinkTop);
        Place(riseArrow.rectTransform, arrowLeft, PlatePadding + (SmallKey - ArrowSize) * 0.5f, ArrowSize, ArrowSize);
        Place(sinkArrow.rectTransform, arrowLeft, sinkTop + (SmallKey - ArrowSize) * 0.5f, ArrowSize, ArrowSize);
    }
}
