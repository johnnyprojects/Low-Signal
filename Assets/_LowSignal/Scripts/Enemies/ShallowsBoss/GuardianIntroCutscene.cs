using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// The first time the player swims near the guardian, the camera leaves the robot and flies through a few authored
/// shots of the cave, the guardian on its gold and the offering ring, with captions in the HUD style between
/// letterbox bars, then fades back to the player. The robot, its abilities and the HUD pause meanwhile. Enter, Space or
/// the gamepad's south button skips it. It plays once per save: the guardian records that it has been seen.
/// Each shot eases the camera from its From marker to its To marker; move and turn the markers to restage it.
/// </summary>
public class GuardianIntroCutscene : MonoBehaviour
{
    [System.Serializable]
    public class Shot
    {
        public Transform from;
        public Transform to;
        [Min(0.1f)] public float seconds = 4f;
        [TextArea] public string caption;
    }

    public static bool IsPlaying { get; private set; }

    // Cleared on entering Play mode even when domain reload is off.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => IsPlaying = false;

    public ShallowsFishBoss boss;
    [Tooltip("Plays when the player comes within this horizontal distance of the guardian or its offering ring.")]
    [Min(1f)] public float triggerDistance = 28f;
    public Shot[] shots;
    [Tooltip("Seconds to fade through black when the cutscene starts, between shots and when it ends.")]
    [Min(0.05f)] public float fadeSeconds = 0.45f;
    public string title = "THE GUARDIAN";
    [Tooltip("Shown over the last shot once the shots are done, under a TIP heading. Leave empty for none.")]
    [TextArea] public string closingTip;
    [Min(0.5f)] public float tipSeconds = 4f;

    UnderwaterSwimController player;
    Camera view;
    readonly List<Behaviour> paused = new List<Behaviour>();
    readonly List<Canvas> hiddenCanvases = new List<Canvas>();
    GameObject overlay;
    Image fade;
    RectTransform topBar, bottomBar;
    CanvasGroup captionGroup;
    Text captionTitle, captionText;
    bool skip;

    void Start()
    {
        player = FindFirstObjectByType<UnderwaterSwimController>();
        if (player != null) view = player.GetComponentInChildren<Camera>();
    }

    void Update()
    {
        if (IsPlaying)
        {
            if ((Keyboard.current?.enterKey.wasPressedThisFrame ?? false) || (Keyboard.current?.spaceKey.wasPressedThisFrame ?? false)
                || (Gamepad.current?.buttonSouth.wasPressedThisFrame ?? false))
                skip = true;
            return;
        }
        if (player == null || view == null || boss == null || boss.IntroSeen || boss.State != ShallowsFishBoss.Phase.Guarding
            || SaveGameService.IsPaused || shots == null || shots.Length == 0) return;
        if (Near(boss.fish.position) || (boss.offering != null && Near(boss.offering.position))) StartCoroutine(Play());
    }

    bool Near(Vector3 point)
    {
        Vector3 offset = player.transform.position - point;
        return new Vector2(offset.x, offset.z).magnitude <= triggerDistance;
    }

    IEnumerator Play()
    {
        IsPlaying = true;
        skip = false;
        // Recorded first, so a save made mid-cutscene never replays it.
        boss.IntroSeen = true;
        BuildOverlay();
        yield return Fade(0, 1);
        TakeControl();
        StartCoroutine(Letterbox(1));
        for (int i = 0; i < shots.Length && !skip; i++)
        {
            Shot shot = shots[i];
            if (shot.from == null || shot.to == null) continue;
            Frame(shot, 0);
            captionText.text = shot.caption;
            yield return Fade(1, 0);
            for (float t = 0; t < shot.seconds && !skip; t += Time.deltaTime)
            {
                Frame(shot, t / shot.seconds);
                // Captions fade in after the cut and out before the next one.
                captionGroup.alpha = Mathf.Clamp01(Mathf.Min(t - 0.3f, shot.seconds - 0.3f - t) / 0.4f);
                yield return null;
            }
            captionGroup.alpha = 0;
            if (i < shots.Length - 1 || string.IsNullOrEmpty(closingTip)) yield return Fade(0, 1);
        }
        // The tip holds on the last shot before the fade back to the robot.
        if (!skip && !string.IsNullOrEmpty(closingTip))
        {
            captionTitle.text = "TIP";
            captionText.text = closingTip;
            for (float t = 0; t < tipSeconds && !skip; t += Time.deltaTime)
            {
                captionGroup.alpha = Mathf.Clamp01(Mathf.Min(t, tipSeconds - t) / 0.4f);
                yield return null;
            }
            captionGroup.alpha = 0;
            captionTitle.text = title;
        }
        if (fade.color.a < 1) yield return Fade(fade.color.a, 1);
        yield return Letterbox(0, true);
        ReturnControl();
        yield return Fade(1, 0);
        Destroy(overlay);
        IsPlaying = false;
    }

    void Frame(Shot shot, float t)
    {
        float eased = Mathf.SmoothStep(0, 1, t);
        view.transform.SetPositionAndRotation(Vector3.Lerp(shot.from.position, shot.to.position, eased),
            Quaternion.Slerp(shot.from.rotation, shot.to.rotation, eased));
    }

    void TakeControl()
    {
        paused.Clear();
        // The pickup stays on, so a carried treasure stays carried; it takes no input while the cursor is free.
        foreach (Behaviour behaviour in new Behaviour[] { player, FindFirstObjectByType<AbilityWheel>() })
            if (behaviour != null && behaviour.enabled) { behaviour.enabled = false; paused.Add(behaviour); }
        var body = player.GetComponent<Rigidbody>();
        if (body != null && !body.isKinematic) body.linearVelocity = Vector3.zero;
        hiddenCanvases.Clear();
        // The pause menu (sorting order 100 and up) stays available.
        foreach (Canvas canvas in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
            if (canvas.isRootCanvas && canvas.enabled && canvas.gameObject != overlay && canvas.sortingOrder < 100)
            { canvas.enabled = false; hiddenCanvases.Add(canvas); }
    }

    void ReturnControl()
    {
        foreach (Behaviour behaviour in paused)
            if (behaviour != null) behaviour.enabled = true;
        foreach (Canvas canvas in hiddenCanvases)
            if (canvas != null) canvas.enabled = true;
        // Disabling the swim controller frees the cursor; gameplay keeps it locked.
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    IEnumerator Fade(float from, float to)
    {
        for (float t = 0; t < fadeSeconds; t += Time.deltaTime)
        {
            fade.color = new Color(0, 0, 0, Mathf.Lerp(from, to, t / fadeSeconds));
            yield return null;
        }
        fade.color = new Color(0, 0, 0, to);
    }

    IEnumerator Letterbox(float to, bool instant = false)
    {
        const float height = 92;
        float from = topBar.sizeDelta.y / height;
        for (float t = 0; !instant && t < 0.6f; t += Time.deltaTime)
        {
            SetBars(Mathf.Lerp(from, to, Mathf.SmoothStep(0, 1, t / 0.6f)) * height);
            yield return null;
        }
        SetBars(to * height);
    }

    void SetBars(float height)
    {
        topBar.sizeDelta = new Vector2(0, height);
        bottomBar.sizeDelta = new Vector2(0, height);
    }

    // Letterbox bars, a HUD-style caption panel and a full-screen fade, above the HUD and below the pause menu.
    void BuildOverlay()
    {
        overlay = HudStyle.CreateCanvas("Guardian Cutscene", new Vector2(1280, 720), 90);
        Font heading = HudStyle.LoadFont("Michroma-Regular", this), body = HudStyle.LoadFont("TitilliumWeb-SemiBold", this);
        topBar = Bar("Top Bar", new Vector2(0, 1));
        bottomBar = Bar("Bottom Bar", new Vector2(0, 0));
        RectTransform panel = HudStyle.CreateRect("Caption", overlay.transform);
        panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0.5f, 0);
        panel.anchoredPosition = new Vector2(0, 112);
        panel.sizeDelta = new Vector2(760, 84);
        captionGroup = panel.gameObject.AddComponent<CanvasGroup>();
        captionGroup.alpha = 0;
        HudStyle.BuildAngularPanel(panel, 16, out Image glow, out Image outline);
        glow.color = HudStyle.WithAlpha(HudStyle.Cyan, 0.35f);
        outline.color = HudStyle.Cyan;
        RectTransform titleRect = HudStyle.Place(HudStyle.CreateRect("Title", panel), 0, 10, 760, 24);
        captionTitle = HudStyle.AddText(titleRect, heading, 16, title, HudStyle.Cyan, TextAnchor.MiddleCenter);
        RectTransform captionRect = HudStyle.Place(HudStyle.CreateRect("Text", panel), 20, 38, 720, 36);
        captionText = HudStyle.AddText(captionRect, body, 19, "", HudStyle.Ice, TextAnchor.MiddleCenter);
        captionText.horizontalOverflow = HorizontalWrapMode.Wrap;
        RectTransform skipRect = HudStyle.CreateRect("Skip", bottomBar);
        skipRect.anchorMin = skipRect.anchorMax = skipRect.pivot = new Vector2(1, 0.5f);
        skipRect.anchoredPosition = new Vector2(-32, 0);
        skipRect.sizeDelta = new Vector2(300, 24);
        HudStyle.AddText(skipRect, body, 15, "ENTER  SKIP", HudStyle.Dim, TextAnchor.MiddleRight);
        fade = HudStyle.AddImage(HudStyle.Stretch(HudStyle.CreateRect("Fade", overlay.transform)), null);
        fade.color = new Color(0, 0, 0, 0);
    }

    RectTransform Bar(string name, Vector2 anchor)
    {
        RectTransform bar = HudStyle.CreateRect(name, overlay.transform);
        bar.anchorMin = new Vector2(0, anchor.y);
        bar.anchorMax = new Vector2(1, anchor.y);
        bar.pivot = new Vector2(0.5f, anchor.y);
        bar.anchoredPosition = Vector2.zero;
        bar.sizeDelta = Vector2.zero;
        HudStyle.AddImage(bar, null).color = Color.black;
        return bar;
    }

    void OnDisable()
    {
        if (!IsPlaying) return;
        // Leaving the scene mid-cutscene: hand everything back.
        StopAllCoroutines();
        ReturnControl();
        if (overlay != null) Destroy(overlay);
        IsPlaying = false;
    }
}
