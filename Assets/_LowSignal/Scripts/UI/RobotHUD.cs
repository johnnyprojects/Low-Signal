using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using static HudStyle;

/// <summary>
/// Attach to the Player. Builds the robot's status readouts in the top-left corner at runtime, in the angular HUD style:
///  - Energy: a chamfered glass panel with a rhombus charge gauge sweeping round the bolt, the percentage, and a bar
///    of slanted cells. Spent energy leaves a white trail that drains away after a moment, and the cell at the charge
///    edge shimmers while recharging. Amber when low; red with a blinking LOW POWER label when critical.
///  - Abilities: a strip below it. On the left, the six-dot ability wheel icon and the TAB keycap, which press in and
///    light up while the ability wheel (AbilityWheel) is open. On the right, the equipped ability's icon, name and state
///    (ON/OFF, READY or a cooldown) with a right-click mouse hint; it changes as soon as the wheel equips something new
///    and flashes when the ability is used.
/// The hover/swim controls live in MovementModeHUD. Fonts and the bolt icon load from Assets/Resources/RobotHUD.
/// </summary>
[DisallowMultipleComponent]
public class RobotHUD : MonoBehaviour
{
    private const float PanelWidth = 400f;
    private const float PanelHeight = 92f;
    private const int PanelCut = 20;
    private const float GaugeSize = 70f;
    private const float TextLeft = 98f;
    private const float CellHeight = 22f;
    private const int CellCount = 16;
    private const float CellGap = 3f;
    private const float CellSlant = 0.35f;
    private const float StripWidth = 300f;
    private const float StripHeight = 58f;
    private const float StripDivider = 104f;
    private const float GemSize = 44f;
    private const float AbilityGap = 10f;

    [Tooltip("Found on this object, or anywhere in the scene, when left empty.")]
    [SerializeField] private PlayerEnergy energySource;

    [Header("Energy")]
    [Tooltip("At or below this fraction the meter turns amber.")]
    [SerializeField, Range(0f, 1f)] private float lowThreshold = 0.3f;
    [Tooltip("At or below this fraction the meter turns red and shows the warning.")]
    [SerializeField, Range(0f, 1f)] private float criticalThreshold = 0.1f;
    [Tooltip("Seconds spent energy lingers as a white trail before draining away.")]
    [SerializeField, Min(0f)] private float trailDelay = 0.35f;
    [Tooltip("Fraction of the bar per second the trail drains once it starts.")]
    [SerializeField, Min(0.01f)] private float trailDrainSpeed = 0.6f;

    [Header("Abilities")]
    [Tooltip("The wheel the TAB hint and equipped-ability readout report on. Found on this object, or anywhere in the scene, when left empty.")]
    [SerializeField] private AbilityWheel abilityWheel;
    [Tooltip("Optional right-click icon. Leave empty to use the built-in mouse drawing.")]
    [SerializeField] private Sprite rightClickIcon;
    [Tooltip("Seconds the equipped ability flashes when it is used.")]
    [SerializeField, Min(0.01f)] private float usePulseSeconds = 0.4f;
    [Tooltip("Keycap colour while its key is held.")]
    [SerializeField] private Color pressedTint = new Color(0.6f, 0.95f, 1f);
    [Tooltip("Seconds for the keycap to press in and spring back.")]
    [SerializeField, Min(0.01f)] private float pressSeconds = 0.08f;

    [Header("Layout")]
    [Tooltip("Canvas reference size. 1280x720 matches the Figma frame, so its pixel values carry over 1:1.")]
    [SerializeField] private Vector2 referenceResolution = new Vector2(1280f, 720f);
    [Tooltip("Gap between the panels and the top-left corner of the screen, in reference pixels.")]
    [SerializeField, Min(0f)] private float screenMargin = 32f;
    [Tooltip("Energy panel opacity while the battery is full; it brightens fully as soon as energy is spent.")]
    [SerializeField, Range(0f, 1f)] private float fullOpacity = 0.6f;
    [SerializeField, Min(0.01f)] private float energyFadeSeconds = 0.3f;

    private GameObject canvasObject;
    private Font headingFont;
    private Font boldFont;

    private RectTransform energyPanel;
    private CanvasGroup energyCanvasGroup;
    private Image panelGlow;
    private Image panelOutline;
    private Image gaugeFill;
    private Image bolt;
    private Text labelText;
    private Text percentText;
    private Image[] cells;
    private int shownPercent = -1;
    private bool shownCritical;
    private float trail = 1f;
    private float trailHoldUntil;
    private float lastEnergy = 1f;

    private Keycap tabKey;
    private RectTransform wheel;
    private Image wheelImage;
    private Image stripGlow;
    private Image stripOutline;
    private RectTransform gem;
    private Image gemOutline;
    private Image abilityIcon;
    private Text abilityName;
    private Text abilityStatus;
    private Image mouseBody;
    private Image mouseButton;
    private AbilityId shownAbility = (AbilityId)(-1);
    private float usePulse;

    private void Awake()
    {
        if (energySource == null)
            energySource = GetComponent<PlayerEnergy>();
        if (energySource == null)
            energySource = FindAnyObjectByType<PlayerEnergy>();
        if (abilityWheel == null)
            abilityWheel = GetComponent<AbilityWheel>();
        if (abilityWheel == null)
            abilityWheel = FindAnyObjectByType<AbilityWheel>();

        headingFont = LoadFont("Michroma-Regular", this);
        boldFont = LoadFont("TitilliumWeb-Bold", this);

        canvasObject = CreateCanvas("Robot HUD", referenceResolution, 10);
        // The meter is skipped when the script it reports on is missing; the ability strip then takes its place.
        if (energySource != null)
            BuildEnergyMeter();
        BuildAbilityStrip(energySource != null ? screenMargin + PanelHeight + AbilityGap : screenMargin);
    }

    private void OnEnable()
    {
        if (abilityWheel != null)
            abilityWheel.AbilityUsed += OnAbilityUsed;
    }

    private void OnDisable()
    {
        if (abilityWheel != null)
            abilityWheel.AbilityUsed -= OnAbilityUsed;
    }

    private void LateUpdate()
    {
        if (energyPanel != null)
            UpdateEnergyMeter();
        UpdateAbilityStrip();
    }

    private void OnDestroy()
    {
        if (canvasObject != null)
            Destroy(canvasObject);
    }

    /// <summary>A rect anchored and pivoted on the top-left corner of the screen, so it stays there at any resolution.</summary>
    private RectTransform CreateCornerPanel(string name, float top, float width, float height)
    {
        RectTransform panel = CreateRect(name, canvasObject.transform);
        panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0f, 1f);
        panel.anchoredPosition = new Vector2(screenMargin, -top);
        panel.sizeDelta = new Vector2(width, height);
        return panel;
    }

    // ---------- Energy meter ----------

    private void UpdateEnergyMeter()
    {
        float energy = energySource.Energy01;
        float targetAlpha = energy >= 1f ? fullOpacity : 1f;
        energyCanvasGroup.alpha = Mathf.MoveTowards(energyCanvasGroup.alpha, targetAlpha,
            Time.unscaledDeltaTime / Mathf.Max(0.01f, energyFadeSeconds));

        // The trail holds where the charge was, then drains down to meet it; recharging pulls it straight up.
        if (energy < lastEnergy - 0.0001f)
            trailHoldUntil = Time.unscaledTime + trailDelay;
        if (energy >= trail)
            trail = energy;
        else if (Time.unscaledTime >= trailHoldUntil)
            trail = Mathf.MoveTowards(trail, energy, trailDrainSpeed * Time.unscaledDeltaTime);
        bool recharging = energy > lastEnergy + 0.00001f;
        lastEnergy = energy;

        bool critical = energy <= criticalThreshold;
        Color accent = critical ? Red : energy <= lowThreshold ? Amber : Cyan;
        // Blink while critical so it reads as urgent without hiding it.
        float pulse = critical ? 0.55f + 0.45f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 3f)) : 1f;

        gaugeFill.fillAmount = energy;
        gaugeFill.color = bolt.color = WithAlpha(accent, pulse);
        percentText.color = accent;
        panelOutline.color = WithAlpha(accent, 0.55f * pulse);
        panelGlow.color = WithAlpha(accent, 0.28f * pulse);
        labelText.color = critical ? WithAlpha(Red, pulse) : Dim;
        if (critical != shownCritical)
        {
            shownCritical = critical;
            labelText.text = critical ? "// LOW POWER" : "// ENERGY";
        }

        int percent = Mathf.CeilToInt(energy * 100f);
        if (percent != shownPercent)
        {
            shownPercent = percent;
            percentText.text = percent + "%";
        }

        Color empty = WithAlpha(Dim, 0.14f);
        Color spent = WithAlpha(Ice, 0.55f);
        for (int i = 0; i < CellCount; i++)
        {
            float fill = Mathf.Clamp01(energy * CellCount - i);
            float lost = Mathf.Clamp01(trail * CellCount - i);
            Color cell = Color.Lerp(Color.Lerp(empty, spent, lost), accent, fill);
            // The cell being refilled shimmers while the battery recharges.
            if (recharging && fill > 0f && fill < 1f)
                cell = Color.Lerp(cell, Ice, 0.35f + 0.35f * Mathf.Sin(Time.unscaledTime * 12f));
            cells[i].color = cell;
        }
    }

    private void BuildEnergyMeter()
    {
        energyPanel = CreateCornerPanel("Energy Meter", screenMargin, PanelWidth, PanelHeight);
        energyCanvasGroup = energyPanel.gameObject.AddComponent<CanvasGroup>();
        energyCanvasGroup.alpha = fullOpacity;
        BuildAngularPanel(energyPanel, PanelCut, out panelGlow, out panelOutline);

        // Rhombus charge gauge, echoing the swim badge: a dim track, the charge sweeping clockwise from the top, and the bolt.
        RectTransform gauge = Place(CreateRect("Gauge", energyPanel), 14f, (PanelHeight - GaugeSize) * 0.5f, GaugeSize, GaugeSize);
        int gaugeTexture = Mathf.RoundToInt(GaugeSize * 2f);
        AddImage(Stretch(CreateRect("Core", gauge)), DiamondSprite(gaugeTexture, 0f, 4f)).color = WithAlpha(Ink, 0.6f);
        Sprite gaugeRing = DiamondSprite(gaugeTexture, 6f, 4f);
        AddImage(Stretch(CreateRect("Track", gauge)), gaugeRing).color = WithAlpha(Dim, 0.2f);
        gaugeFill = AddImage(Stretch(CreateRect("Charge", gauge)), gaugeRing);
        gaugeFill.type = Image.Type.Filled;
        gaugeFill.fillMethod = Image.FillMethod.Radial360;
        gaugeFill.fillOrigin = (int)Image.Origin360.Top;
        gaugeFill.fillClockwise = true;
        bolt = AddImage(Stretch(CreateRect("Bolt", gauge), -GaugeSize * 0.3f), LoadIcon("bolt", this));
        bolt.preserveAspect = true;

        float barWidth = PanelWidth - TextLeft - 24f;
        labelText = AddText(Place(CreateRect("Label", energyPanel), TextLeft, 16f, 200f, 18f), headingFont, 12, "// ENERGY", Dim, TextAnchor.MiddleLeft);
        percentText = AddText(Place(CreateRect("Percent", energyPanel), PanelWidth - 24f - 100f, 8f, 100f, 32f), boldFont, 28, "100%", Cyan, TextAnchor.MiddleRight);

        RectTransform bar = Place(CreateRect("Cells", energyPanel), TextLeft, 50f, barWidth, CellHeight);
        float cellWidth = (barWidth - CellGap * (CellCount - 1)) / CellCount;
        Sprite cellSprite = SlantedCellSprite(Mathf.RoundToInt(cellWidth * 3f), Mathf.RoundToInt(CellHeight * 3f), CellSlant);
        cells = new Image[CellCount];
        for (int i = 0; i < CellCount; i++)
            cells[i] = AddImage(Place(CreateRect("Cell " + (i + 1), bar), i * (cellWidth + CellGap), 0f, cellWidth, CellHeight), cellSprite);

        trail = lastEnergy = energySource.Energy01;
        UpdateEnergyMeter();
    }

    // ---------- Ability strip ----------

    private void OnAbilityUsed(AbilityWheel.AbilitySlot slot)
    {
        usePulse = 1f;
    }

    private void UpdateAbilityStrip()
    {
        tabKey.Animate(Time.unscaledDeltaTime, pressSeconds, pressedTint);
        // The ability wheel icon lights up and clicks round a little while the wheel is open.
        wheelImage.color = Color.Lerp(WithAlpha(Ice, 0.8f), Cyan, tabKey.Pressed);
        wheel.localRotation = Quaternion.Euler(0f, 0f, -30f * SmoothStep01(tabKey.Pressed));
        if (abilityWheel == null)
            return;

        AbilityWheel.AbilitySlot slot = abilityWheel.SelectedSlot;
        if (slot.ability != shownAbility)
        {
            // Swap straight away when the wheel equips something new.
            shownAbility = slot.ability;
            abilityName.text = slot.Label;
            abilityIcon.sprite = AbilityIcons.For(slot.ability);
            abilityIcon.enabled = abilityIcon.sprite != null;
        }
        IRobotAbility ability = abilityWheel.SelectedAbility;
        bool active = ability != null && ability.IsActive;
        string status = !slot.Usable ? "LOCKED" : ability != null ? ability.Status : "";
        if (abilityStatus.text != status)
            abilityStatus.text = status;
        abilityStatus.color = !slot.Usable ? Amber : active ? Cyan : Dim;

        // A quick flash when the ability fires, easing back down.
        usePulse = Mathf.MoveTowards(usePulse, 0f, Time.unscaledDeltaTime / usePulseSeconds);
        float flash = SmoothStep01(usePulse);
        gem.localScale = Vector3.one * (1f + 0.15f * flash);
        stripGlow.color = WithAlpha(Cyan, Mathf.Lerp(0.2f, 0.7f, flash));
        stripOutline.color = WithAlpha(Cyan, Mathf.Lerp(0.45f, 1f, flash));
        gemOutline.color = WithAlpha(Cyan, Mathf.Lerp(active ? 0.9f : 0.5f, 1f, flash));
        abilityIcon.color = Color.Lerp(active ? Cyan : Ice, Color.white, flash);
        // The right button lights up with the flash.
        mouseButton.color = Color.Lerp(WithAlpha(Cyan, 0.75f), Ice, flash);
        mouseBody.color = WithAlpha(Ice, Mathf.Lerp(0.7f, 1f, flash));
    }

    /// <summary>
    /// One plate under the energy panel: pick with TAB on the left, and on the right the equipped ability (icon, name,
    /// state) with the right-click hint to use it.
    /// </summary>
    private void BuildAbilityStrip(float top)
    {
        float width = abilityWheel != null ? StripWidth : StripDivider;
        RectTransform strip = CreateCornerPanel("Ability Strip", top, width, StripHeight);
        BuildAngularPanel(strip, 14, out stripGlow, out stripOutline);
        stripGlow.color = WithAlpha(Cyan, 0.2f);
        stripOutline.color = WithAlpha(Cyan, 0.45f);

        float keyTop = (StripHeight - 24f - KeyTravel) * 0.5f;
        wheel = PlaceCentred(CreateRect("Ability Wheel", strip), 14f, (StripHeight - 24f) * 0.5f, 24f, 24f);
        wheelImage = AddImage(Stretch(CreateRect("Dots", wheel)), DotRingSprite(72));
        tabKey = new Keycap("Tab", strip, headingFont, "TAB", 9, new Vector2(44f, 24f),
            () => abilityWheel != null ? abilityWheel.IsOpen : Keyboard.current != null && Keyboard.current.tabKey.isPressed);
        tabKey.PlaceAt(46f, keyTop);
        if (abilityWheel == null)
            return;

        AddImage(Place(CreateRect("Divider", strip), StripDivider, 12f, 1.5f, StripHeight - 24f), null).color = WithAlpha(Cyan, 0.25f);

        // A small rhombus holding the equipped ability's icon, echoing the wheel's slots.
        float gemLeft = StripDivider + 10f;
        gem = PlaceCentred(CreateRect("Gem", strip), gemLeft, (StripHeight - GemSize) * 0.5f, GemSize, GemSize);
        int gemTexture = Mathf.RoundToInt(GemSize * 2f);
        AddImage(Stretch(CreateRect("Glass", gem)), DiamondSprite(gemTexture, 0f, 4f)).color = WithAlpha(Ink, 0.7f);
        gemOutline = AddImage(Stretch(CreateRect("Outline", gem)), DiamondSprite(gemTexture, 3f, 4f));
        abilityIcon = AddImage(Stretch(CreateRect("Icon", gem), -GemSize * 0.27f), null);

        float textLeft = gemLeft + GemSize + 10f;
        abilityName = AddText(Place(CreateRect("Name", strip), textLeft, 11f, 90f, 18f), headingFont, 13, "", Ice, TextAnchor.MiddleLeft);
        abilityStatus = AddText(Place(CreateRect("Status", strip), textLeft, 31f, 90f, 14f), boldFont, 11, "", Dim, TextAnchor.MiddleLeft);

        // Right click uses the ability: a mouse with its right button lit.
        RectTransform mouse = Place(CreateRect("Right Click", strip), StripWidth - 16f - 20f, (StripHeight - 28f) * 0.5f, 20f, 28f);
        mouseBody = AddImage(Stretch(CreateRect("Mouse", mouse)), rightClickIcon != null ? rightClickIcon : AbilityIcons.MouseBody);
        mouseBody.preserveAspect = true;
        mouseButton = AddImage(Stretch(CreateRect("Right Button", mouse)), AbilityIcons.MouseRightButton);
        // A custom icon already shows its own button.
        mouseButton.enabled = rightClickIcon == null;
        UpdateAbilityStrip();
    }
}
