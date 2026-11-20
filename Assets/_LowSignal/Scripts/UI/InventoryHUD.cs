using UnityEngine;
using UnityEngine.UI;
using static HudStyle;

/// <summary>
/// Attach to the Player, next to PlayerInventory and PlayerPickup. Builds, at runtime and in the robot HUD's style:
///  - The inventory: a row of glowing rhombi along the bottom middle of the screen, like the swim badge, one for each
///    item held and nothing at all while the inventory is empty. Each holds its item's icon (a cut-gem glyph unless the
///    item has its own) and pops in as the item arrives; the row stays centred as it grows and shrinks. At a stone
///    circle, the rhombus of the item the next press will place blinks amber.
///  - The interaction prompt, low in the middle of the screen: the interact keycap with PICK UP or PLACE and the item's
///    name, or, without a key, INVENTORY FULL, or a hint to move closer when a circle that wants a stored item is near
///    but out of reach. It stands in for PlayerPickup's plain on-screen hint.
/// Both hide while the game is paused and during the guardian's intro cutscene.
/// </summary>
[DisallowMultipleComponent]
public class InventoryHUD : MonoBehaviour
{
    private const float SlotSize = 52f;
    private const float SlotGap = 12f;
    private const float GlowSpread = 8f;
    private const float PromptHeight = 44f;
    private const float PromptKeySize = 26f;

    [Tooltip("Found on this object when left empty.")]
    [SerializeField] private PlayerInventory inventory;
    [Tooltip("Found on this object when left empty. Supplies the item in view and the interact key.")]
    [SerializeField] private PlayerPickup pickup;

    [Header("Layout")]
    [Tooltip("Canvas reference size; matches the rest of the robot HUD.")]
    [SerializeField] private Vector2 referenceResolution = new Vector2(1280f, 720f);
    [Tooltip("Gap between the row of rhombi and the bottom of the screen, in reference pixels.")]
    [SerializeField, Min(0f)] private float rowBottom = 22f;
    [Tooltip("Height of the prompt's bottom edge above the bottom of the screen, in reference pixels.")]
    [SerializeField, Min(0f)] private float promptBottom = 150f;

    [Header("Animation")]
    [SerializeField] private Color pressedTint = new Color(0.6f, 0.95f, 1f);
    [SerializeField, Min(0.01f)] private float pressSeconds = 0.08f;
    [Tooltip("Seconds a slot pops when an item arrives in it.")]
    [SerializeField, Min(0.01f)] private float arriveSeconds = 0.5f;
    [SerializeField, Min(0.01f)] private float fadeSeconds = 0.15f;

    private sealed class Slot
    {
        public RectTransform Root;
        public Image Glow;
        public Image Glass;
        public Image Outline;
        public Image Icon;
    }

    private static Sprite gemGlyph;

    private GameObject canvasObject;
    private Font headingFont;
    private Font bodyFont;

    private RectTransform row;
    private CanvasGroup rowGroup;
    private Slot[] slots;
    private float arrivePulse;

    private RectTransform prompt;
    private CanvasGroup promptGroup;
    private Image promptGlow;
    private Image promptOutline;
    private Keycap promptKey;
    private Text promptText;

    private void Awake()
    {
        if (inventory == null) inventory = GetComponent<PlayerInventory>();
        if (pickup == null) pickup = GetComponent<PlayerPickup>();
        if (inventory == null)
        {
            Debug.LogWarning("InventoryHUD has no PlayerInventory to show.", this);
            enabled = false;
            return;
        }
        headingFont = LoadFont("Michroma-Regular", this);
        bodyFont = LoadFont("TitilliumWeb-SemiBold", this);
        canvasObject = CreateCanvas("Inventory HUD", referenceResolution, 10);
        BuildRow();
        BuildPrompt();
    }

    private void OnEnable()
    {
        if (canvasObject != null) canvasObject.SetActive(true);
        if (inventory != null) inventory.ItemAdded += OnItemAdded;
    }

    private void OnDisable()
    {
        if (canvasObject != null) canvasObject.SetActive(false);
        if (inventory != null) inventory.ItemAdded -= OnItemAdded;
    }

    private void OnDestroy()
    {
        if (canvasObject != null) Destroy(canvasObject);
    }

    private void OnItemAdded(PickupItem item)
    {
        arrivePulse = 1f;
    }

    private void LateUpdate()
    {
        bool hidden = SaveGameService.IsPaused || GuardianIntroCutscene.IsPlaying;
        float step = Time.unscaledDeltaTime / fadeSeconds;
        rowGroup.alpha = Mathf.MoveTowards(rowGroup.alpha, hidden ? 0f : 1f, step);
        UpdateRow();
        UpdatePrompt(hidden, step);
    }

    // ---------- Inventory row ----------

    private void BuildRow()
    {
        int count = inventory.Capacity;
        row = CreateRect("Inventory", canvasObject.transform);
        // Anchored and pivoted on the bottom centre, so it stays there at any resolution.
        row.anchorMin = row.anchorMax = row.pivot = new Vector2(0.5f, 0f);
        row.anchoredPosition = new Vector2(0f, rowBottom);
        rowGroup = row.gameObject.AddComponent<CanvasGroup>();
        rowGroup.alpha = 0f;

        // Shapes are drawn at twice the slot size so they stay crisp when the canvas scales up.
        int texture = Mathf.RoundToInt(SlotSize * 2f);
        Sprite glow = DiamondGlowSprite(texture, Mathf.RoundToInt(GlowSpread * 2f), 6f);
        Sprite glass = DiamondSprite(texture, 0f, 6f);
        Sprite ring = DiamondSprite(texture, 3f, 6f);
        slots = new Slot[count];
        for (int i = 0; i < count; i++)
        {
            RectTransform root = PlaceCentred(CreateRect("Slot " + (i + 1), row), i * (SlotSize + SlotGap), 0f, SlotSize, SlotSize);
            slots[i] = new Slot
            {
                Root = root,
                Glow = AddImage(Stretch(CreateRect("Glow", root), GlowSpread), glow),
                Glass = AddImage(Stretch(CreateRect("Glass", root)), glass),
                Outline = AddImage(Stretch(CreateRect("Outline", root)), ring),
                Icon = AddImage(Stretch(CreateRect("Icon", root), -SlotSize * 0.26f), null),
            };
            slots[i].Icon.preserveAspect = true;
        }
        UpdateRow();
    }

    private void UpdateRow()
    {
        var items = inventory.Items;
        bool placing = inventory.Status == PlayerInventory.CircleStatus.InRange;
        arrivePulse = Mathf.MoveTowards(arrivePulse, 0f, Time.unscaledDeltaTime / arriveSeconds);
        float pop = SmoothStep01(arrivePulse);
        float blink = 0.7f + 0.3f * Mathf.Sin(Time.unscaledTime * 5f);
        // Only held items get a rhombus; sizing the row to them keeps it centred.
        int shown = Mathf.Min(items.Count, slots.Length);
        row.sizeDelta = new Vector2(Mathf.Max(0f, shown * SlotSize + (shown - 1) * SlotGap), SlotSize);

        for (int i = 0; i < slots.Length; i++)
        {
            Slot slot = slots[i];
            PickupItem item = i < items.Count ? items[i] : null;
            if (slot.Root.gameObject.activeSelf != (item != null)) slot.Root.gameObject.SetActive(item != null);
            if (item == null) continue;
            bool next = placing && item != null && item == inventory.NextOffering;
            float arrive = i == items.Count - 1 ? pop : 0f;
            // A new rhombus grows in from small, overshoots a little and settles.
            float grow = 1f - arrivePulse;
            slot.Root.localScale = Vector3.one * (i == items.Count - 1 && arrivePulse > 0f
                ? Mathf.Lerp(0.4f, 1f, SmoothStep01(Mathf.Clamp01(grow * 2f))) + 0.15f * Mathf.Sin(grow * Mathf.PI)
                : 1f);
            Sprite icon = item.Icon != null ? item.Icon : GemGlyph;
            if (slot.Icon.sprite != icon) slot.Icon.sprite = icon;
            Color accent = next ? Amber : Cyan;
            slot.Glow.color = WithAlpha(accent, Mathf.Lerp(next ? 0.5f * blink : 0.3f, 0.7f, arrive));
            slot.Glass.color = Glass;
            slot.Outline.color = Color.Lerp(WithAlpha(accent, next ? blink : 0.75f), Ice, arrive);
            slot.Icon.color = Color.Lerp(next ? Amber : Ice, Color.white, arrive);
        }
    }

    // ---------- Prompt ----------

    private void BuildPrompt()
    {
        prompt = CreateRect("Interaction Prompt", canvasObject.transform);
        // Anchored on the bottom centre, so it stays under the crosshair at any resolution.
        prompt.anchorMin = prompt.anchorMax = prompt.pivot = new Vector2(0.5f, 0f);
        prompt.anchoredPosition = new Vector2(0f, promptBottom);
        prompt.sizeDelta = new Vector2(200f, PromptHeight);
        promptGroup = prompt.gameObject.AddComponent<CanvasGroup>();
        promptGroup.alpha = 0f;
        BuildAngularPanel(prompt, 12, out promptGlow, out promptOutline);
        string key = pickup != null ? pickup.InteractKeyName : "F";
        promptKey = new Keycap("Interact", prompt, headingFont, key, 11, new Vector2(PromptKeySize, PromptKeySize),
            () => pickup != null && pickup.IsInteractKeyHeld);
        promptText = AddText(Place(CreateRect("Text", prompt), 0f, 0f, 100f, PromptHeight), headingFont, 13, "", Ice, TextAnchor.MiddleLeft);
    }

    private void UpdatePrompt(bool hidden, float step)
    {
        promptKey.Animate(Time.unscaledDeltaTime, pressSeconds, pressedTint);
        // Without PlayerPickup there is no interact key, so nothing to prompt.
        bool show = !hidden && pickup != null && pickup.InteractionAvailable;
        PickupItem target = pickup != null ? pickup.TargetItem : null;
        if (show)
        {
            // The same order PlayerPickup acts in: pick up what is in view, else place at a circle in range.
            if (target != null && !inventory.IsFull)
                ShowPrompt(true, "PICK UP " + Highlight(target, Cyan), Cyan);
            else if (inventory.Status == PlayerInventory.CircleStatus.InRange)
                ShowPrompt(true, "PLACE " + Highlight(inventory.NextOffering, Amber) + " IN THE CIRCLE", Amber);
            else if (target != null)
                ShowPrompt(false, "INVENTORY FULL", Amber);
            else if (inventory.Status == PlayerInventory.CircleStatus.TooFar)
                ShowHint("Too far: swim into the stone circle to place treasure");
            else
                show = false;
        }
        // Fading out keeps the last message, so it does not blank before it disappears.
        promptGroup.alpha = Mathf.MoveTowards(promptGroup.alpha, show ? 1f : 0f, step);
    }

    private void ShowPrompt(bool withKey, string message, Color accent)
    {
        promptText.font = headingFont;
        promptText.fontSize = 13;
        promptText.color = Ice;
        LayOutPrompt(withKey, message, accent, 0.3f, 0.7f);
    }

    private void ShowHint(string message)
    {
        // A softer, sentence-case note, like the quest tracker's objective line.
        promptText.font = bodyFont;
        promptText.fontSize = 16;
        promptText.color = Dim;
        LayOutPrompt(false, message, Dim, 0.15f, 0.4f);
    }

    private void LayOutPrompt(bool withKey, string message, Color accent, float glow, float outline)
    {
        SetText(promptText, message);
        promptGlow.color = WithAlpha(accent, glow);
        promptOutline.color = WithAlpha(accent, outline);
        promptKey.Shadow.gameObject.SetActive(withKey);
        promptKey.Face.gameObject.SetActive(withKey);
        float textLeft = withKey ? 14f + PromptKeySize + 12f : 20f;
        float textWidth = Mathf.Ceil(promptText.preferredWidth) + 2f;
        if (withKey) promptKey.PlaceAt(14f, (PromptHeight - PromptKeySize - KeyTravel) * 0.5f);
        Place(promptText.rectTransform, textLeft, 0f, textWidth, PromptHeight);
        prompt.sizeDelta = new Vector2(textLeft + textWidth + 20f, PromptHeight);
    }

    private static string Highlight(PickupItem item, Color color)
    {
        string label = item != null ? item.DisplayName.ToUpperInvariant() : "ITEM";
        return "<color=#" + ColorUtility.ToHtmlStringRGB(color) + ">" + label + "</color>";
    }

    private static void SetText(Text text, string value)
    {
        if (text.text != value) text.text = value;
    }

    /// <summary>A cut gem seen from the side (table, crown and pavilion with facet lines), for items with no icon.</summary>
    private static Sprite GemGlyph => gemGlyph != null ? gemGlyph : gemGlyph = CoverageSprite(72, p =>
    {
        const float table = 0.55f, girdle = 0.15f, tip = -0.8f;
        float x = Mathf.Abs(p.x);
        bool crown = p.y >= girdle && p.y <= table && x <= 0.4f + (table - p.y) / (table - girdle) * 0.45f;
        bool pavilion = p.y < girdle && p.y >= tip && x <= 0.85f * (p.y - tip) / (girdle - tip);
        // Thin gaps: the girdle line, and two facet lines running from the table's corners down to the tip.
        bool girdleLine = Mathf.Abs(p.y - girdle) < 0.045f;
        float facet = Mathf.Abs((p.y - tip) * 0.4f - x * (girdle - tip)) / Mathf.Sqrt(0.16f + (girdle - tip) * (girdle - tip));
        bool facetLine = p.y < girdle && facet < 0.04f;
        return (crown || pavilion) && !girdleLine && !facetLine;
    });
}
