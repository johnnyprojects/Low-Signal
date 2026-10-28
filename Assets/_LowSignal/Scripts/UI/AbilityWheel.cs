using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using static HudStyle;

/// <summary>
/// Attach to the Player. Picks and uses the robot's abilities.
///  - Holding Tab pops up the ability wheel in the centre of the screen: six rhombus slots around a hub, in the HUD's
///    angular style. While it is open the camera holds still and time slows, and moving the mouse points at a slot;
///    releasing Tab equips the highlighted ability. Locked slots are dimmed, show a padlock, and cannot be equipped.
///  - Right click uses the equipped ability, by calling the IRobotAbility component on the Player with the same Id
///    (Flashlight for LIGHT, Sonar for SONAR). It does nothing while the wheel is open or if the ability is locked.
/// Ability names come from AbilityNames, so the slots, hub and HUD badge always match.
/// </summary>
[DisallowMultipleComponent]
// Runs before the swim controller, so the camera is frozen on the same frame the wheel opens.
[DefaultExecutionOrder(-60)]
public class AbilityWheel : MonoBehaviour
{
    private const float SlotSize = 92f;
    private const float HubSize = 150f;
    private const float GlowSpread = 10f;

    [Serializable]
    public class AbilitySlot
    {
        public AbilityId ability;
        [Tooltip("Locked slots show on the wheel but cannot be equipped or used.")]
        public bool unlocked;

        public string Label => AbilityNames.Label(ability);
        public bool Usable => unlocked && ability != AbilityId.Empty;
    }

    [Tooltip("Found on this object, or anywhere in the scene, when left empty.")]
    [SerializeField] private UnderwaterSwimController swimController;
    [Tooltip("Hold to open the wheel; release to equip the highlighted ability.")]
    [SerializeField] private Key wheelKey = Key.Tab;

    [Header("Abilities")]
    [Tooltip("Clockwise from the top. Six fit the wheel best.")]
    [SerializeField] private AbilitySlot[] abilities =
    {
        new AbilitySlot { ability = AbilityId.Light, unlocked = true },
        new AbilitySlot { ability = AbilityId.Sonar, unlocked = true },
        new AbilitySlot { ability = AbilityId.Camo },
        new AbilitySlot { ability = AbilityId.Boost },
        new AbilitySlot { ability = AbilityId.Scan },
        new AbilitySlot { ability = AbilityId.Empty },
    };
    [Tooltip("Slot equipped at the start.")]
    [SerializeField, Min(0)] private int startingSlot;

    [Header("Feel")]
    [Tooltip("Time scale while the wheel is open; 1 keeps the game running at full speed.")]
    [SerializeField, Range(0.05f, 1f)] private float slowMotion = 0.35f;
    [Tooltip("Seconds for the wheel to pop open or closed.")]
    [SerializeField, Min(0.01f)] private float openSeconds = 0.12f;
    [Tooltip("How far the mouse has to move to point at a slot. Higher needs a bigger flick.")]
    [SerializeField, Min(1f)] private float pointerTravel = 120f;
    [Tooltip("Fraction of the pointer travel the mouse must reach before a slot highlights.")]
    [SerializeField, Range(0f, 0.9f)] private float deadZone = 0.3f;

    [Header("Layout")]
    [SerializeField] private Vector2 referenceResolution = new Vector2(1280f, 720f);
    [Tooltip("Distance from the centre of the screen to the centre of each slot, in reference pixels.")]
    [SerializeField, Min(60f)] private float radius = 165f;

    private GameObject canvasObject;
    private RectTransform root;
    private CanvasGroup group;
    private Image backdrop;
    private RectTransform[] slotRects;
    private Image[] slotGlows;
    private Image[] slotOutlines;
    private Image[] slotTrims;
    private Image[] slotIcons;
    private Text[] slotLabels;
    private float[] slotHighlight;
    private Text hubTitle;
    private Text hubStatus;
    private RectTransform pointer;
    private Image pointerImage;

    private IRobotAbility[] abilityComponents;
    private Vector2 aim;
    private float openAmount;
    private float timeScaleBeforeOpen = 1f;
    private bool slowed;

    public bool IsOpen { get; private set; }
    /// <summary>0 when closed, 1 when fully open, easing in between; HUD elements can fade with it.</summary>
    public float OpenAmount => openAmount;
    /// <summary>The slot the mouse points at while open, or -1.</summary>
    public int HighlightedIndex { get; private set; } = -1;
    public int SelectedIndex { get; private set; }
    public AbilitySlot SelectedSlot => abilities[SelectedIndex];
    /// <summary>The component behind the equipped ability, or null if it has none yet.</summary>
    public IRobotAbility SelectedAbility => Find(SelectedSlot.ability);
    /// <summary>Raised when releasing the wheel key equips an ability.</summary>
    public event Action<AbilitySlot> AbilitySelected;
    /// <summary>Raised when right click successfully uses the equipped ability.</summary>
    public event Action<AbilitySlot> AbilityUsed;

    private void Awake()
    {
        if (swimController == null)
            swimController = GetComponent<UnderwaterSwimController>();
        if (swimController == null)
            swimController = FindAnyObjectByType<UnderwaterSwimController>();
        if (abilities == null || abilities.Length == 0)
        {
            Debug.LogWarning("AbilityWheel has no ability slots.", this);
            enabled = false;
            return;
        }
        abilityComponents = GetComponents<IRobotAbility>();
        SelectedIndex = Mathf.Clamp(startingSlot, 0, abilities.Length - 1);
        Build();
        Refresh(0f);
    }

    private void Update()
    {
        if (SaveGameService.InputBlockedThisFrame) return;
        Keyboard keyboard = Keyboard.current;
        Mouse mouse = Mouse.current;
        bool canUse = keyboard != null && Cursor.lockState == CursorLockMode.Locked && Application.isFocused;
        bool held = canUse && keyboard[wheelKey].isPressed;
        if (held && !IsOpen)
            Open();
        else if (!held && IsOpen)
            Close(canUse);

        if (IsOpen && mouse != null)
        {
            // A virtual stick: mouse movement pushes the aim around, capped at the edge of the wheel.
            aim = Vector2.ClampMagnitude(aim + mouse.delta.ReadValue() / pointerTravel, 1f);
            HighlightedIndex = aim.magnitude < deadZone ? -1 : SlotAt(aim);
        }
        else if (canUse && mouse != null && mouse.rightButton.wasPressedThisFrame)
        {
            UseSelected();
        }

        openAmount = Mathf.MoveTowards(openAmount, IsOpen ? 1f : 0f, Time.unscaledDeltaTime / openSeconds);
        if (slowed)
            Time.timeScale = Mathf.Lerp(timeScaleBeforeOpen, slowMotion * timeScaleBeforeOpen, SmoothStep01(openAmount));
        if (slowed && !IsOpen && openAmount <= 0f)
        {
            Time.timeScale = timeScaleBeforeOpen;
            slowed = false;
        }
        Refresh(Time.unscaledDeltaTime);
    }

    public void CaptureSaveState(PlayerSaveData state)
    {
        state.selectedAbility = SelectedSlot.ability;
        foreach (var slot in abilities)
            if (slot.unlocked && !state.unlockedAbilities.Contains(slot.ability)) state.unlockedAbilities.Add(slot.ability);
    }

    public void RestoreSaveState(PlayerSaveData state)
    {
        if (IsOpen) Close(false);
        for (int i = 0; i < abilities.Length; i++)
        {
            abilities[i].unlocked = state.unlockedAbilities.Contains(abilities[i].ability);
            if (abilities[i].ability == state.selectedAbility && abilities[i].Usable) SelectedIndex = i;
        }
        AbilitySelected?.Invoke(SelectedSlot);
    }

    public void CloseForPause()
    {
        if (IsOpen) Close(false);
        openAmount = 0f;
        if (slowed) Time.timeScale = timeScaleBeforeOpen;
        slowed = false;
        Refresh(0f);
    }

    /// <summary>Uses the equipped ability. Locked or empty slots do nothing.</summary>
    public bool UseSelected()
    {
        AbilitySlot slot = SelectedSlot;
        if (!slot.Usable)
            return false;
        IRobotAbility ability = Find(slot.ability);
        if (ability == null)
        {
            Debug.LogWarning($"{slot.Label} is unlocked on the ability wheel but nothing on {name} implements it yet.", this);
            return false;
        }
        if (!ability.Use())
            return false;
        AbilityUsed?.Invoke(slot);
        return true;
    }

    private IRobotAbility Find(AbilityId id)
    {
        if (abilityComponents == null)
            return null;
        foreach (IRobotAbility ability in abilityComponents)
            if (ability != null && ability.Id == id)
                return ability;
        return null;
    }

    private void Open()
    {
        IsOpen = true;
        aim = Vector2.zero;
        HighlightedIndex = -1;
        if (!slowed)
        {
            timeScaleBeforeOpen = Time.timeScale;
            slowed = true;
        }
        if (swimController != null)
            swimController.LookBlocked = true;
    }

    private void Close(bool equip)
    {
        IsOpen = false;
        if (swimController != null)
            swimController.LookBlocked = false;
        int choice = HighlightedIndex;
        HighlightedIndex = -1;
        if (!equip || choice < 0 || !abilities[choice].Usable)
            return;
        SelectedIndex = choice;
        AbilitySelected?.Invoke(abilities[choice]);
    }

    private void OnDisable()
    {
        if (IsOpen)
            Close(false);
        if (slowed)
        {
            Time.timeScale = timeScaleBeforeOpen;
            slowed = false;
        }
    }

    private void OnDestroy()
    {
        if (canvasObject != null)
            Destroy(canvasObject);
    }

    /// <summary>The slot whose direction is nearest the aim; slot 0 is at the top and they run clockwise.</summary>
    private int SlotAt(Vector2 direction)
    {
        float clockwiseFromTop = Mathf.Repeat(Mathf.Atan2(direction.x, direction.y) * Mathf.Rad2Deg, 360f);
        float step = 360f / abilities.Length;
        return Mathf.RoundToInt(clockwiseFromTop / step) % abilities.Length;
    }

    private Vector2 SlotDirection(int index)
    {
        float angle = (90f - index * 360f / abilities.Length) * Mathf.Deg2Rad;
        return new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
    }

    // ---------- Presentation ----------

    private void Refresh(float deltaTime)
    {
        float eased = SmoothStep01(openAmount);
        group.alpha = eased;
        group.gameObject.SetActive(openAmount > 0f);
        root.localScale = Vector3.one * Mathf.Lerp(0.85f, 1f, eased);
        backdrop.color = WithAlpha(Ink, 0.45f * eased);

        for (int i = 0; i < abilities.Length; i++)
        {
            bool highlighted = i == HighlightedIndex;
            slotHighlight[i] = Mathf.MoveTowards(slotHighlight[i], highlighted ? 1f : 0f, deltaTime / 0.08f);
            float h = SmoothStep01(slotHighlight[i]);
            bool usable = abilities[i].Usable;
            bool equipped = i == SelectedIndex;
            // Locked slots stay dim and highlight amber, so it is clear they cannot be taken.
            Color accent = usable ? Cyan : Amber;
            slotRects[i].localScale = Vector3.one * Mathf.Lerp(1f, usable ? 1.16f : 1.06f, h);
            slotGlows[i].color = WithAlpha(accent, Mathf.Lerp(usable ? 0.18f : 0f, usable ? 0.6f : 0.35f, h));
            slotOutlines[i].color = usable
                ? WithAlpha(Cyan, Mathf.Lerp(0.5f, 1f, h))
                : WithAlpha(Color.Lerp(Dim, Amber, h), Mathf.Lerp(0.2f, 0.7f, h));
            slotTrims[i].color = WithAlpha(Cyan, equipped ? 0.55f : usable ? 0.1f : 0.04f);
            Color content = usable ? Color.Lerp(Dim, Ice, Mathf.Max(h, equipped ? 0.6f : 0f)) : WithAlpha(Color.Lerp(Dim, Amber, h * 0.6f), 0.45f);
            slotLabels[i].color = content;
            slotIcons[i].color = content;
        }

        int shown = HighlightedIndex >= 0 ? HighlightedIndex : SelectedIndex;
        AbilitySlot slot = abilities[shown];
        hubTitle.text = slot.Label;
        hubTitle.color = slot.Usable ? Ice : WithAlpha(Dim, 0.6f);
        if (!slot.Usable)
            SetStatus("LOCKED", Amber);
        else if (shown == SelectedIndex)
            SetStatus("EQUIPPED", Cyan);
        else
            SetStatus("RELEASE TO EQUIP", Dim);

        // The pointer rides the hub's edge toward the highlighted slot.
        pointerImage.enabled = HighlightedIndex >= 0;
        if (HighlightedIndex >= 0)
        {
            Vector2 direction = SlotDirection(HighlightedIndex);
            pointer.anchoredPosition = direction * (HubSize * 0.5f + 14f);
            pointer.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg - 90f);
            pointerImage.color = abilities[HighlightedIndex].Usable ? Cyan : Amber;
        }
    }

    private void SetStatus(string status, Color color)
    {
        if (hubStatus.text != status)
            hubStatus.text = status;
        hubStatus.color = color;
    }

    private void Build()
    {
        Font headingFont = LoadFont("Michroma-Regular", this);
        Font boldFont = LoadFont("TitilliumWeb-Bold", this);
        canvasObject = CreateCanvas("Ability Wheel", referenceResolution, 20);
        group = CreateRect("Wheel", canvasObject.transform).gameObject.AddComponent<CanvasGroup>();
        RectTransform screen = Stretch((RectTransform)group.transform);
        group.blocksRaycasts = group.interactable = false;

        // Darkens the scene behind the wheel so it reads clearly.
        backdrop = AddImage(Stretch(CreateRect("Backdrop", screen)), null);

        root = CreateRect("Ring", screen);
        root.anchorMin = root.anchorMax = new Vector2(0.5f, 0.5f);
        root.sizeDelta = Vector2.zero;

        int hubTexture = Mathf.RoundToInt(HubSize * 2f);
        RectTransform hub = CreateRect("Hub", root);
        hub.sizeDelta = new Vector2(HubSize, HubSize);
        AddImage(Stretch(CreateRect("Glow", hub), GlowSpread), DiamondGlowSprite(hubTexture, Mathf.RoundToInt(GlowSpread * 2f), 6f)).color = WithAlpha(Cyan, 0.3f);
        AddImage(Stretch(CreateRect("Glass", hub)), DiamondSprite(hubTexture, 0f, 6f)).color = Glass;
        AddImage(Stretch(CreateRect("Outline", hub)), DiamondSprite(hubTexture, 3f, 6f)).color = WithAlpha(Cyan, 0.6f);
        AddImage(Stretch(CreateRect("Inner Line", hub), -HubSize * 0.09f), DiamondSprite(hubTexture, 2f, 6f)).color = WithAlpha(Cyan, 0.14f);
        hubTitle = AddText(Centred(CreateRect("Title", hub), 0f, 6f, HubSize, 20f), headingFont, 13, "", Ice, TextAnchor.MiddleCenter);
        hubStatus = AddText(Centred(CreateRect("Status", hub), 0f, -14f, HubSize, 16f), boldFont, 10, "", Dim, TextAnchor.MiddleCenter);

        pointer = CreateRect("Pointer", root);
        pointer.sizeDelta = new Vector2(18f, 18f);
        pointerImage = AddImage(Stretch(CreateRect("Chevron", pointer)), ChevronSprite(54, true));

        int slotTexture = Mathf.RoundToInt(SlotSize * 2f);
        Sprite slotGlow = DiamondGlowSprite(slotTexture, Mathf.RoundToInt(GlowSpread * 2f), 5f);
        Sprite slotFill = DiamondSprite(slotTexture, 0f, 5f);
        Sprite slotLine = DiamondSprite(slotTexture, 3f, 5f);
        Sprite slotTrim = DiamondSprite(slotTexture, 2f, 5f);
        int count = abilities.Length;
        slotRects = new RectTransform[count];
        slotGlows = new Image[count];
        slotOutlines = new Image[count];
        slotTrims = new Image[count];
        slotIcons = new Image[count];
        slotLabels = new Text[count];
        slotHighlight = new float[count];
        for (int i = 0; i < count; i++)
        {
            AbilitySlot ability = abilities[i];
            RectTransform slot = CreateRect("Slot " + (i + 1) + " " + ability.Label, root);
            slot.sizeDelta = new Vector2(SlotSize, SlotSize);
            slot.anchoredPosition = SlotDirection(i) * radius;
            slotRects[i] = slot;
            slotGlows[i] = AddImage(Stretch(CreateRect("Glow", slot), GlowSpread), slotGlow);
            AddImage(Stretch(CreateRect("Glass", slot)), slotFill).color = ability.Usable ? Glass : WithAlpha(Glass, 0.5f);
            slotOutlines[i] = AddImage(Stretch(CreateRect("Outline", slot)), slotLine);
            // The inner trim lights on the equipped slot.
            slotTrims[i] = AddImage(Stretch(CreateRect("Trim", slot), -SlotSize * 0.1f), slotTrim);
            // Unlocked slots show the ability's icon above its name; locked ones show a padlock instead.
            Sprite icon = ability.Usable ? AbilityIcons.For(ability.ability) : ability.ability == AbilityId.Empty ? null : AbilityIcons.Lock;
            slotIcons[i] = AddImage(Centred(CreateRect("Icon", slot), 0f, 9f, 24f, 24f), icon);
            slotIcons[i].enabled = icon != null;
            slotLabels[i] = AddText(Centred(CreateRect("Label", slot), 0f, icon != null ? -13f : 0f, SlotSize, 14f), headingFont, 9, ability.Label, Dim, TextAnchor.MiddleCenter);
        }
    }

    /// <summary>A rect centred on its parent, offset by (x, y).</summary>
    private static RectTransform Centred(RectTransform rect, float x, float y, float width, float height)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(x, y);
        rect.sizeDelta = new Vector2(width, height);
        return rect;
    }
}
