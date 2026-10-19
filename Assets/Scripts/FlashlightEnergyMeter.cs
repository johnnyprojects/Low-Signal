using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Attach to the Player alongside PlayerEnergy. Builds a screen-space energy bar at runtime
/// showing the player's shared energy.
/// </summary>
[DisallowMultipleComponent]
public class FlashlightEnergyMeter : MonoBehaviour
{
    [Tooltip("Found on this object, or anywhere in the scene, when left empty.")]
    [SerializeField] private PlayerEnergy energySource;

    [Header("Layout")]
    [SerializeField] private Vector2 screenOffset = new Vector2(40f, 40f);
    [SerializeField] private Vector2 barSize = new Vector2(320f, 18f);

    [Header("Colors")]
    [SerializeField] private Color fullColor = new Color(0.35f, 0.9f, 1f);
    [SerializeField] private Color lowColor = new Color(1f, 0.3f, 0.25f);
    [Tooltip("Below this fraction the bar shifts toward the low color.")]
    [SerializeField, Range(0f, 1f)] private float lowThreshold = 0.25f;
    [SerializeField] private Color backgroundColor = new Color(0f, 0f, 0f, 0.55f);
    [SerializeField] private Color textColor = new Color(0.85f, 0.95f, 1f);

    private GameObject canvasObject;
    private RectTransform fill;
    private Image fillImage;
    private Text percentText;
    private int shownPercent = -1;

    private void Awake()
    {
        if (energySource == null)
            energySource = GetComponent<PlayerEnergy>();
        if (energySource == null)
            energySource = FindAnyObjectByType<PlayerEnergy>();
        if (energySource == null)
        {
            Debug.LogError("FlashlightEnergyMeter could not find PlayerEnergy.", this);
            enabled = false;
            return;
        }

        // The active HUD is RobotHUD. This legacy meter is intentionally disabled so
        // it cannot create the duplicate bottom-left energy bar.
        enabled = false;
    }

    private void LateUpdate()
    {
        float energy = energySource.Energy01;
        // Scale the fill by its anchors so no sprite is needed.
        fill.anchorMax = new Vector2(energy, 1f);
        fillImage.color = energy < lowThreshold
            ? Color.Lerp(lowColor, fullColor, energy / Mathf.Max(lowThreshold, 0.0001f))
            : fullColor;

        int percent = Mathf.CeilToInt(energy * 100f);
        if (percent != shownPercent)
        {
            shownPercent = percent;
            percentText.text = percent + "%";
        }
    }

    private void OnDestroy()
    {
        if (canvasObject != null)
            Destroy(canvasObject);
    }

    private void BuildUI()
    {
        canvasObject = new GameObject("Flashlight Energy HUD", typeof(Canvas), typeof(CanvasScaler));
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        RectTransform root = CreateRect("Energy Meter", canvasObject.transform);
        root.anchorMin = root.anchorMax = root.pivot = Vector2.zero;
        root.anchoredPosition = screenOffset;
        root.sizeDelta = new Vector2(barSize.x, barSize.y + 28f);

        Text label = CreateText("Label", root, font, "ENERGY", TextAnchor.LowerLeft);
        Text percent = CreateText("Percent", root, font, "100%", TextAnchor.LowerRight);
        percentText = percent;

        RectTransform background = CreateRect("Bar", root);
        background.anchorMin = Vector2.zero;
        background.anchorMax = new Vector2(1f, 0f);
        background.pivot = new Vector2(0.5f, 0f);
        background.sizeDelta = new Vector2(0f, barSize.y);
        background.gameObject.AddComponent<Image>().color = backgroundColor;

        // Inset the fill area slightly so the background reads as a frame.
        RectTransform fillArea = CreateRect("Fill Area", background);
        fillArea.anchorMin = Vector2.zero;
        fillArea.anchorMax = Vector2.one;
        fillArea.offsetMin = new Vector2(2f, 2f);
        fillArea.offsetMax = new Vector2(-2f, -2f);

        fill = CreateRect("Fill", fillArea);
        fill.anchorMin = Vector2.zero;
        fill.anchorMax = Vector2.one;
        fill.offsetMin = fill.offsetMax = Vector2.zero;
        fillImage = fill.gameObject.AddComponent<Image>();
        fillImage.color = fullColor;
        fillImage.raycastTarget = false;
        background.GetComponent<Image>().raycastTarget = false;
        label.raycastTarget = percent.raycastTarget = false;
    }

    private Text CreateText(string name, RectTransform parent, Font font, string value, TextAnchor alignment)
    {
        RectTransform rect = CreateRect(name, parent);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(0f, barSize.y + 4f);
        rect.offsetMax = Vector2.zero;

        Text text = rect.gameObject.AddComponent<Text>();
        text.font = font;
        text.fontSize = 20;
        text.fontStyle = FontStyle.Bold;
        text.color = textColor;
        text.alignment = alignment;
        text.text = value;
        return text;
    }

    private static RectTransform CreateRect(string name, Transform parent)
    {
        GameObject child = new GameObject(name, typeof(RectTransform));
        child.transform.SetParent(parent, false);
        return (RectTransform)child.transform;
    }
}
