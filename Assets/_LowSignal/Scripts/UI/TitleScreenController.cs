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
    [SerializeField] private Color backgroundColor = new Color(11f / 255f, 61f / 255f, 92f / 255f);
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
    [SerializeField] private Image background;
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
        background.color = backgroundColor;
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
        loadButton.interactable = available;
        loadButton.GetComponentInChildren<Text>().color = available ? Color.white : new Color(0.48f, 0.61f, 0.68f);
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
        font = Resources.Load<Font>("RobotHUD/Michroma-Regular") ?? Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        newGameConfirmation = Group("Confirm New Game", menu.transform.parent);
        Label("Warning", newGameConfirmation.transform, "Start a new game?\nYour saved progress will be replaced.", new Vector2(0, -100), new Vector2(1400, 120), 24);
        var confirm = MakeButton(newGameConfirmation.transform, "Start New Game", -220);
        cancelNewGame = MakeButton(newGameConfirmation.transform, "Cancel", -295);
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
        font = Resources.Load<Font>("RobotHUD/Michroma-Regular") ?? Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        var canvasObject = new GameObject("Title Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasObject.transform.SetParent(transform, false);
        canvasObject.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        RectTransform bg = Rect("Blue Background", canvasObject.transform, Vector2.zero, Vector2.zero);
        bg.anchorMin = Vector2.zero;
        bg.anchorMax = Vector2.one;
        background = bg.gameObject.AddComponent<Image>();
        background.color = backgroundColor;
        background.raycastTarget = false;
        logoImage = Rect("Game Logo", canvasObject.transform, new Vector2(0, 75), new Vector2(800, 800)).gameObject.AddComponent<Image>();
        logoImage.preserveAspect = true;
        logoImage.raycastTarget = false;
        SetLogo(logo);
        prompt = Group("Press Any Key", canvasObject.transform);
        Label("Prompt", prompt.transform, "PRESS ANY KEY TO START", new Vector2(0, -185), new Vector2(1100, 70), 26);
        menu = Group("Main Menu", canvasObject.transform);
        startButton = MakeButton(menu.transform, "Start Game", -100);
        loadButton = MakeButton(menu.transform, "Load Game", -175);
        SetLoadAvailable(false);
        optionsButton = MakeButton(menu.transform, "Options", -250);
        quitButton = MakeButton(menu.transform, "Quit", -325);
        status = Label("Status", menu.transform, "", new Vector2(0, -420), new Vector2(1300, 60), 20);
        options = Group("Options", canvasObject.transform);
        volumeButton = MakeButton(options.transform, "Volume", -100);
        flickerButton = MakeButton(options.transform, "Prompt Flicker", -175);
        backButton = MakeButton(options.transform, "Back", -280);
        Show(menu, false);
        Show(options, false);
        if (FindAnyObjectByType<EventSystem>() == null)
        {
            var events = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            events.transform.SetParent(transform, false);
            events.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
        }
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

    private Text Label(string name, Transform parent, string value, Vector2 position, Vector2 size, int fontSize)
    {
        var text = Rect(name, parent, position, size).gameObject.AddComponent<Text>();
        text.font = font;
        text.fontSize = fontSize;
        text.alignment = TextAnchor.MiddleCenter;
        text.text = value;
        text.color = Color.white;
        text.raycastTarget = false;
        return text;
    }

    private Button MakeButton(Transform parent, string label, float y)
    {
        var rect = Rect(label, parent, new Vector2(0, y), new Vector2(580, 62));
        var image = rect.gameObject.AddComponent<Image>();
        image.color = Color.white;
        var button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        var colors = button.colors;
        colors.normalColor = new Color(0.025f, 0.16f, 0.25f);
        colors.highlightedColor = colors.selectedColor = new Color(0.13f, 0.38f, 0.51f);
        colors.pressedColor = new Color(0.3f, 0.52f, 0.63f);
        colors.disabledColor = new Color(0.08f, 0.19f, 0.26f, 0.3f);
        button.colors = colors;
        Label("Label", rect, label, Vector2.zero, rect.sizeDelta, 24);
        return button;
    }
}
