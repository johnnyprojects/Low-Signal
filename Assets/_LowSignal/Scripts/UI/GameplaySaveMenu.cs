using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

/// <summary>Escape pause menu; saving is handled by checkpoints and future recharge stations.</summary>
[DefaultExecutionOrder(-90)]
public sealed class GameplaySaveMenu : MonoBehaviour
{
    private GameObject canvasObject;
    private GameObject panel;
    private Text message;
    private Button resume;
    private Button load;
    private Font font;
    private float messageUntil;
    private string confirmation;
    private Button confirmButton;
    private Button cancelButton;
    private bool ignoreToggleUntilReleased;
    private SaveGameService service;

    private void Start()
    {
        Build();
        service = SaveGameService.Instance;
        service.Message += DisplayMessage;
        if (!string.IsNullOrEmpty(SaveGameService.Instance.LastMessage)) DisplayMessage(SaveGameService.Instance.LastMessage);
    }

    private void Update()
    {
        if (SaveGameService.Instance.IsLoading) return;
        bool toggle = (Keyboard.current?.escapeKey.wasPressedThisFrame ?? false) || (Gamepad.current?.startButton.wasPressedThisFrame ?? false);
        if (ignoreToggleUntilReleased)
        {
            if (!(Keyboard.current?.escapeKey.isPressed ?? false) && !(Gamepad.current?.startButton.isPressed ?? false)) ignoreToggleUntilReleased = false;
            toggle = false;
        }
        if (toggle)
        {
            if (confirmation != null) CancelConfirmation();
            else TogglePause();
        }
        if (!SaveGameService.IsPaused && Time.unscaledTime > messageUntil) message.text = "";
    }

    private void TogglePause()
    {
        bool open = !SaveGameService.IsPaused;
        SaveGameService.Instance.SetPaused(open);
        panel.SetActive(open);
        if (open)
        {
            load.interactable = SaveGameService.Instance.CanLoad(out _);
            message.text = "Progress saves at checkpoints.     Esc: Resume";
            EventSystem.current.SetSelectedGameObject(resume.gameObject);
        }
        else EventSystem.current.SetSelectedGameObject(null);
    }

    private void DisplayMessage(string value)
    {
        if (message == null) return;
        message.text = value;
        messageUntil = Time.unscaledTime + 4f;
        if (load != null) load.interactable = SaveGameService.Instance.CanLoad(out _);
    }

    private void AskConfirmation(string action)
    {
        confirmation = action;
        message.text = action == "load" ? "Load the last save? Unsaved progress will be lost." : "Return to title? Unsaved progress will be lost.";
        foreach (Transform child in panel.transform) child.gameObject.SetActive(false);
        confirmButton.gameObject.SetActive(true);
        cancelButton.gameObject.SetActive(true);
        EventSystem.current.SetSelectedGameObject(cancelButton.gameObject);
    }

    private void CancelConfirmation()
    {
        confirmation = null;
        foreach (Transform child in panel.transform) child.gameObject.SetActive(true);
        confirmButton.gameObject.SetActive(false);
        cancelButton.gameObject.SetActive(false);
        message.text = "Progress saves at checkpoints.     Esc: Resume";
        EventSystem.current.SetSelectedGameObject(resume.gameObject);
    }

    private void Confirm()
    {
        string action = confirmation;
        string information;
        bool success = action == "load" ? SaveGameService.Instance.TryLoad(out information) : SaveGameService.Instance.ReturnToTitle(out information);
        if (!success) { CancelConfirmation(); DisplayMessage(information); }
        else panel.SetActive(false);
    }

    private void Build()
    {
        font = Resources.Load<Font>("RobotHUD/Michroma-Regular") ?? Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        canvasObject = HudStyle.CreateCanvas("Pause Menu", new Vector2(1280, 720), 100);
        canvasObject.AddComponent<GraphicRaycaster>();
        panel = Rect("Pause Panel", canvasObject.transform, Vector2.zero, Vector2.zero).gameObject;
        var panelRect = (RectTransform)panel.transform;
        panelRect.anchorMin = Vector2.zero;
        panelRect.anchorMax = Vector2.one;
        panel.AddComponent<Image>().color = new Color(0.01f, 0.05f, 0.09f, 0.95f);
        Label("PAUSED", panel.transform, 180, 32);
        resume = Button("Resume", 90, () => { ignoreToggleUntilReleased = true; TogglePause(); });
        load = Button("Load Last Save", 25, () => AskConfirmation("load"));
        Button("Return to Title", -40, () => AskConfirmation("title"));
        confirmButton = Button("Confirm", 20, Confirm);
        cancelButton = Button("Cancel", -55, CancelConfirmation);
        confirmButton.gameObject.SetActive(false);
        cancelButton.gameObject.SetActive(false);
        message = Label("", canvasObject.transform, -255, 18);
        panel.SetActive(false);
        if (FindAnyObjectByType<EventSystem>() == null)
        {
            var events = new GameObject("Save Menu EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            events.transform.SetParent(canvasObject.transform, false);
            events.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
        }
    }

    private Button Button(string text, float y, UnityEngine.Events.UnityAction action)
    {
        var rect = Rect(text, panel.transform, new Vector2(0, y), new Vector2(450, 52));
        var button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = rect.gameObject.AddComponent<Image>();
        var colors = button.colors;
        colors.normalColor = new Color(0.04f, 0.2f, 0.29f);
        colors.highlightedColor = colors.selectedColor = new Color(0.13f, 0.4f, 0.52f);
        colors.pressedColor = new Color(0.3f, 0.5f, 0.65f);
        button.colors = colors;
        Label(text, rect, 0, 20).rectTransform.sizeDelta = rect.sizeDelta;
        button.onClick.AddListener(action);
        return button;
    }

    private Text Label(string text, Transform parent, float y, int size)
    {
        var label = Rect(text, parent, new Vector2(0, y), new Vector2(1100, 70)).gameObject.AddComponent<Text>();
        label.text = text;
        label.font = font;
        label.fontSize = size;
        label.color = Color.white;
        label.alignment = TextAnchor.MiddleCenter;
        label.raycastTarget = false;
        return label;
    }

    private static RectTransform Rect(string name, Transform parent, Vector2 position, Vector2 size)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        return rect;
    }

    private void OnDestroy()
    {
        // Scene changes are responsible for their next cursor state; only clear pause here.
        if (SaveGameService.IsPaused && service != null) service.SetPaused(false);
        if (canvasObject != null) Destroy(canvasObject);
        // The persistent service outlives this scene-bound menu.
        if (service != null) service.Message -= DisplayMessage;
    }
}
