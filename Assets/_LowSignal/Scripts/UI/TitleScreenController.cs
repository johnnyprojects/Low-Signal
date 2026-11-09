using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>Title prompt, single-slot save loading, new-game confirmation, and options.</summary>
public sealed class TitleScreenController : MonoBehaviour
{
    [Header("Appearance")]
    [SerializeField] private Sprite logo;
    [Header("Start Game")]
    [SerializeField] private string startScene = "Shallows";
    [Tooltip("Enable when an intro Timeline handles starting the game instead of loading a scene.")]
    [SerializeField] private bool useStartEvent;
    [SerializeField] private UnityEvent onStartGame = new UnityEvent();
    [Header("Generated scene UI")]
    [SerializeField] private CanvasGroup prompt;
    [SerializeField] private CanvasGroup menu;
    [SerializeField] private CanvasGroup options;
    [SerializeField] private Image logoImage;
    [SerializeField] private RawImage background;
    [SerializeField] private Button startButton;
    [SerializeField] private Button loadButton;
    [SerializeField] private Button optionsButton;
    [SerializeField] private Button quitButton;
    [SerializeField] private Button volumeButton;
    [SerializeField] private Button flickerButton;
    [SerializeField] private Button backButton;
    [SerializeField] private Text status;
    private bool menuOpened;
    private bool transitioning;
    private bool flicker;
    private bool starting;
    private Font font;
    private Font bodyFont;
    private CanvasGroup newGameConfirmation;
    private Button cancelNewGame;
    private const string VolumeKey = "LowSignal.MasterVolume";
    private const string FlickerKey = "LowSignal.TitleFlicker";

    private void Awake()
    {
        if (prompt == null) BuildInterface();
        Time.timeScale = 1f;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        // Respect an image assigned directly in the scene as well as the controller's logo slot.
        if (logo == null) logo = logoImage.sprite;
        SetLogo(logo);
        flicker = PlayerPrefs.GetInt(FlickerKey, 1) != 0;
        AudioListener.volume = Mathf.Clamp01(PlayerPrefs.GetFloat(VolumeKey, 1f));
        Show(prompt, true);
        Show(menu, false);
        Show(options, false);
        bool canLoad = SaveGameService.Instance.CanLoad(out string saveInformation);
        SetLoadAvailable(canLoad);
        if (SaveGameService.Instance.HasSaveFiles && !canLoad) status.text = saveInformation;
        BuildNewGameConfirmation();
        startButton.onClick.AddListener(StartGame);
        loadButton.onClick.AddListener(LoadGame);
        optionsButton.onClick.AddListener(() => OpenOptions(true));
        backButton.onClick.AddListener(() => OpenOptions(false));
        quitButton.onClick.AddListener(Quit);
        volumeButton.onClick.AddListener(() =>
        {
            int step = Mathf.RoundToInt(AudioListener.volume * 10f);
            AudioListener.volume = (step >= 10 ? 0 : step + 1) / 10f;
            PlayerPrefs.SetFloat(VolumeKey, AudioListener.volume);
            PlayerPrefs.Save();
            UpdateOptionLabels();
        });
        flickerButton.onClick.AddListener(() =>
        {
            flicker = !flicker;
            PlayerPrefs.SetInt(FlickerKey, flicker ? 1 : 0);
            PlayerPrefs.Save();
            UpdateOptionLabels();
        });
        UpdateOptionLabels();
    }

    private void Update()
    {
        if (!menuOpened && !transitioning)
        {
            // A slow signal pulse, with a small, infrequent dip. Never a rapid full-screen flash.
            float t = Time.unscaledTime;
            prompt.alpha = flicker ? Mathf.Lerp(0.45f, 1f, (Mathf.Sin(t * 2.8f) + 1f) * 0.5f) : 1f;
            if (flicker && t % 6.7f < 0.1f) prompt.alpha = 0.35f;
            if (AnyStartPressed()) StartCoroutine(RevealMenu());
        }
        if (options.interactable && ((Keyboard.current?.escapeKey.wasPressedThisFrame ?? false)
            || (Gamepad.current?.buttonEast.wasPressedThisFrame ?? false))) OpenOptions(false);
        if (newGameConfirmation.interactable && ((Keyboard.current?.escapeKey.wasPressedThisFrame ?? false)
            || (Gamepad.current?.buttonEast.wasPressedThisFrame ?? false))) CancelNewGame();
    }

    private static bool AnyStartPressed()
    {
        if (Keyboard.current?.anyKey.wasPressedThisFrame ?? false) return true;
        if ((Mouse.current?.leftButton.wasPressedThisFrame ?? false)
            || (Mouse.current?.rightButton.wasPressedThisFrame ?? false)) return true;
        if (Touchscreen.current?.primaryTouch.press.wasPressedThisFrame ?? false) return true;
        foreach (Gamepad pad in Gamepad.all)
            foreach (InputControl control in pad.allControls)
                if (control is ButtonControl button && button.wasPressedThisFrame) return true;
        return false;
    }

    private IEnumerator RevealMenu()
    {
        transitioning = true;
        float initialAlpha = prompt.alpha;
        menu.gameObject.SetActive(true);
        for (float elapsed = 0f; elapsed < 0.35f; elapsed += Time.unscaledDeltaTime)
        {
            float amount = Mathf.SmoothStep(0f, 1f, elapsed / 0.35f);
            prompt.alpha = initialAlpha * (1f - amount);
            menu.alpha = amount;
            yield return null;
        }
        Show(prompt, false);
        // Do not let the input revealing the menu also activate Start Game.
        yield return null;
        Show(menu, true);
        menuOpened = true;
        transitioning = false;
        EventSystem.current.SetSelectedGameObject(startButton.gameObject);
    }

    private void OpenOptions(bool open)
    {
        Show(menu, !open);
        Show(options, open);
        EventSystem.current.SetSelectedGameObject(open ? volumeButton.gameObject : optionsButton.gameObject);
    }

    private void UpdateOptionLabels()
    {
        volumeButton.GetComponentInChildren<Text>().text = $"Volume: {Mathf.RoundToInt(AudioListener.volume * 100f)}%  (+10%)";
        flickerButton.GetComponentInChildren<Text>().text = "Prompt Flicker: " + (flicker ? "On" : "Off");
    }

    /// <summary>Reflects whether a usable save exists.</summary>
    public void SetLoadAvailable(bool available)
    {
        // The button dims itself while it cannot be used.
        loadButton.interactable = available;
    }

    private void StartGame()
    {
        if (starting) return;
        if (!useStartEvent && !Application.CanStreamedLevelBeLoaded(startScene))
        {
            status.text = "The starting scene is unavailable.";
            Debug.LogError($"Add '{startScene}' to the build scene list.", this);
            return;
        }
        if (SaveGameService.Instance.HasSaveFiles)
        {
            Show(menu, false);
            Show(newGameConfirmation, true);
            EventSystem.current.SetSelectedGameObject(cancelNewGame.gameObject);
            return;
        }
        BeginNewGame();
    }

    private void BeginNewGame()
    {
        SaveGameService.Instance.PrepareNewGame();
        starting = true;
        Show(menu, false);
        Show(newGameConfirmation, false);
        if (useStartEvent) onStartGame.Invoke();
        else SceneManager.LoadSceneAsync(startScene);
    }

    private void LoadGame()
    {
        if (starting) return;
        if (SaveGameService.Instance.TryLoad(out string information))
        {
            starting = true;
            menu.interactable = false;
        }
        status.text = information;
    }

    private void BuildNewGameConfirmation()
    {
        font = HudStyle.LoadFont("Michroma-Regular", this);
        bodyFont = HudStyle.LoadFont("TitilliumWeb-SemiBold", this);
        newGameConfirmation = Group("Confirm New Game", menu.transform.parent);
        Panel("Warning Panel", newGameConfirmation.transform, new Vector2(0, -70), new Vector2(760, 110), HudStyle.Amber);
        Label("Warning", newGameConfirmation.transform, "START A NEW GAME?", new Vector2(0, -52), new Vector2(740, 40), 24).color = HudStyle.Amber;
        Label("Detail", newGameConfirmation.transform, "Your saved progress will be replaced.", new Vector2(0, -92), new Vector2(740, 34), 20, bodyFont);
        var confirm = MakeButton(newGameConfirmation.transform, "Start New Game", -182);
        cancelNewGame = MakeButton(newGameConfirmation.transform, "Cancel", -264);
        confirm.onClick.AddListener(BeginNewGame);
        cancelNewGame.onClick.AddListener(CancelNewGame);
        Show(newGameConfirmation, false);
    }

    private void CancelNewGame()
    {
        Show(newGameConfirmation, false);
        Show(menu, true);
        EventSystem.current.SetSelectedGameObject(startButton.gameObject);
    }

    private void Quit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private static void Show(CanvasGroup group, bool visible)
    {
        group.alpha = visible ? 1f : 0f;
        group.interactable = visible;
        group.blocksRaycasts = visible;
        group.gameObject.SetActive(visible);
    }

    public void SetLogo(Sprite sprite)
    {
        logo = sprite;
        if (logoImage == null) return;
        logoImage.sprite = sprite;
        logoImage.preserveAspect = true;
        logoImage.gameObject.SetActive(sprite != null);
    }

    // Used by the editor scene creator so the UI is visible and editable outside Play mode.
    public void BuildInterface()
    {
        if (prompt != null) return;
        font = HudStyle.LoadFont("Michroma-Regular", this);
        bodyFont = HudStyle.LoadFont("TitilliumWeb-SemiBold", this);
        var canvasObject = new GameObject("Title Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasObject.transform.SetParent(transform, false);
        canvasObject.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        RectTransform bg = HudStyle.Stretch(HudStyle.CreateRect("Backdrop", canvasObject.transform));
        background = bg.gameObject.AddComponent<RawImage>();
        background.raycastTarget = false;
        bg.gameObject.AddComponent<HudBackdrop>();
        BuildFrame(canvasObject.transform);
        logoImage = Rect("Game Logo", canvasObject.transform, new Vector2(0, 160), new Vector2(820, 820)).gameObject.AddComponent<Image>();
        logoImage.preserveAspect = true;
        logoImage.raycastTarget = false;
        SetLogo(logo);
        prompt = Group("Press Any Key", canvasObject.transform);
        Panel("Prompt Panel", prompt.transform, new Vector2(0, -150), new Vector2(560, 64), HudStyle.WithAlpha(HudStyle.Cyan, 0.8f));
        Label("Prompt", prompt.transform, "PRESS ANY KEY", new Vector2(0, -150), new Vector2(560, 64), 24).color = HudStyle.Cyan;
        Diamond(prompt.transform, new Vector2(-310, -150), 14);
        Diamond(prompt.transform, new Vector2(310, -150), 14);
        menu = Group("Main Menu", canvasObject.transform);
        startButton = MakeButton(menu.transform, "Start Game", -70);
        loadButton = MakeButton(menu.transform, "Load Game", -152);
        SetLoadAvailable(false);
        optionsButton = MakeButton(menu.transform, "Options", -234);
        quitButton = MakeButton(menu.transform, "Quit", -316);
        status = Label("Status", menu.transform, "", new Vector2(0, -392), new Vector2(1300, 40), 20, bodyFont);
        status.color = HudStyle.Amber;
        options = Group("Options", canvasObject.transform);
        Label("Heading", options.transform, "OPTIONS", new Vector2(0, 10), new Vector2(800, 50), 30);
        volumeButton = MakeButton(options.transform, "Volume", -70);
        flickerButton = MakeButton(options.transform, "Prompt Flicker", -152);
        backButton = MakeButton(options.transform, "Back", -256);
        Show(menu, false);
        Show(options, false);
        if (FindAnyObjectByType<EventSystem>() == null)
        {
            var events = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            events.transform.SetParent(transform, false);
            events.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
        }
    }

    /// <summary>Replaces the generated UI with a fresh one, keeping the logo.</summary>
    public void RebuildInterface()
    {
        Transform old = transform.Find("Title Canvas");
        if (old != null) DestroyImmediate(old.gameObject);
        prompt = null;
        BuildInterface();
    }

    // Cyan corner brackets, a status line and key hints frame the screen like the robot's visor.
    private void BuildFrame(Transform canvas)
    {
        var frame = Group("Visor Frame", canvas);
        Color line = HudStyle.WithAlpha(HudStyle.Cyan, 0.55f);
        for (int corner = 0; corner < 4; corner++)
        {
            float sx = corner % 2 == 0 ? -1 : 1, sy = corner < 2 ? 1 : -1;
            Vector2 tip = new Vector2(sx * 900, sy * 480);
            Line(frame.transform, tip + new Vector2(-sx * 40, 0), new Vector2(82, 3), line);
            Line(frame.transform, tip + new Vector2(0, -sy * 40), new Vector2(3, 82), line);
        }
        Label("Link Status", frame.transform, "DIVE UNIT  //  SIGNAL LINK ESTABLISHED", new Vector2(-560, 440), new Vector2(620, 30), 18, bodyFont)
            .alignment = TextAnchor.MiddleLeft;
        Label("Key Hints", frame.transform, "ARROWS / W S   SELECT          ENTER   CONFIRM          ESC   BACK", new Vector2(0, -440), new Vector2(1300, 30), 18, bodyFont)
            .color = HudStyle.Dim;
    }

    private static RectTransform Rect(string name, Transform parent, Vector2 position, Vector2 size)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = size;
        rect.anchoredPosition = position;
        return rect;
    }

    private static CanvasGroup Group(string name, Transform parent)
    {
        var rect = Rect(name, parent, Vector2.zero, Vector2.zero);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        return rect.gameObject.AddComponent<CanvasGroup>();
    }

    private static void Line(Transform parent, Vector2 position, Vector2 size, Color color)
    {
        var image = Rect("Line", parent, position, size).gameObject.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
    }

    private static void Diamond(Transform parent, Vector2 position, int size)
    {
        var image = Rect("Diamond", parent, position, new Vector2(size, size)).gameObject.AddComponent<Image>();
        image.color = HudStyle.Cyan;
        image.raycastTarget = false;
        image.gameObject.AddComponent<HudShape>().Configure(HudShape.Shape.Diamond, size);
    }

    // A chamfered glass panel with a glow and outline, as on the HUD.
    private static RectTransform Panel(string name, Transform parent, Vector2 position, Vector2 size, Color outlineColor,
        out Image glow, out Image glass, out Image outline)
    {
        var rect = Rect(name, parent, position, size);
        glow = HudStyle.AddImage(HudStyle.Stretch(HudStyle.CreateRect("Glow", rect), HudStyle.GlowSize), null);
        glow.color = HudStyle.WithAlpha(outlineColor, 0.35f);
        glass = HudStyle.AddImage(HudStyle.Stretch(HudStyle.CreateRect("Glass", rect)), null);
        glass.color = HudStyle.Glass;
        outline = HudStyle.AddImage(HudStyle.Stretch(HudStyle.CreateRect("Outline", rect)), null);
        outline.color = outlineColor;
        rect.gameObject.AddComponent<HudShape>().Configure(HudShape.Shape.Panel, 18);
        return rect;
    }

    private static RectTransform Panel(string name, Transform parent, Vector2 position, Vector2 size, Color outlineColor)
    {
        return Panel(name, parent, position, size, outlineColor, out _, out _, out _);
    }

    private Text Label(string name, Transform parent, string value, Vector2 position, Vector2 size, int fontSize, Font labelFont = null)
    {
        var text = Rect(name, parent, position, size).gameObject.AddComponent<Text>();
        text.font = labelFont != null ? labelFont : font;
        text.fontSize = fontSize;
        text.alignment = TextAnchor.MiddleCenter;
        text.text = value;
        text.color = HudStyle.Ice;
        text.raycastTarget = false;
        return text;
    }

    private Button MakeButton(Transform parent, string label, float y)
    {
        var rect = Panel(label, parent, new Vector2(0, y), new Vector2(520, 64), HudStyle.Dim, out Image glow, out Image glass, out Image outline);
        glass.raycastTarget = true;
        var marker = Rect("Marker", rect, new Vector2(-226, 0), new Vector2(14, 14)).gameObject.AddComponent<Image>();
        marker.raycastTarget = false;
        marker.gameObject.AddComponent<HudShape>().Configure(HudShape.Shape.Diamond, 14);
        var text = Label("Label", rect, label.ToUpperInvariant(), Vector2.zero, rect.sizeDelta, 22);
        var button = rect.gameObject.AddComponent<HudMenuButton>();
        button.Bind(glow, glass, outline, text, marker);
        return button;
    }
}
