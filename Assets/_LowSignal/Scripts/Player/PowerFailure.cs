using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using static HudStyle;

/// <summary>
/// Attach to the Player, next to PlayerEnergy. Low energy and running out of it:
///  - At or below the warning level (25% by default) the world drains to grey, through a global post-processing volume
///    made at runtime (it needs post-processing on the camera, which the player camera has), and a big warning sign in
///    the HUD's style pulses at the top of the screen: a red rhombus with an exclamation mark, LOW ENERGY, the charge
///    left and a hint to stop spending power. It pulses faster as the battery empties, and clears once it recharges.
///  - At zero the robot dies: its controls shut off, the screen fades to the scanline backdrop with SIGNAL LOST, and the
///    last save (checkpoint) loads, or the area restarts when there is no save yet. The robot comes back fully charged.
/// </summary>
[DisallowMultipleComponent]
public class PowerFailure : MonoBehaviour
{
    private const float SignSize = 104f;

    [Tooltip("Found on this object when left empty.")]
    [SerializeField] private PlayerEnergy energySource;

    [Header("Low energy")]
    [Tooltip("At or below this fraction of energy the screen turns grey and the warning shows.")]
    [SerializeField, Range(0.01f, 1f)] private float warningThreshold = 0.25f;
    [Tooltip("How far above the threshold energy must recharge before the warning clears, so it does not flicker.")]
    [SerializeField, Range(0f, 0.2f)] private float clearMargin = 0.02f;
    [Tooltip("Seconds for the grey and the warning to fade in or out.")]
    [SerializeField, Min(0.01f)] private float fadeSeconds = 0.6f;
    [Tooltip("How grey the world goes: -100 removes all colour.")]
    [SerializeField, Range(-100f, 0f)] private float saturation = -100f;
    [SerializeField, Range(0f, 1f)] private float vignette = 0.35f;

    [Header("Death")]
    [Tooltip("Seconds to fade to the SIGNAL LOST screen.")]
    [SerializeField, Min(0.01f)] private float deathFadeSeconds = 1.2f;
    [Tooltip("Seconds from dying to the checkpoint loading.")]
    [SerializeField, Min(0.5f)] private float deathSeconds = 3.5f;

    /// <summary>Set when a death reload starts; the player in the reloaded area is refilled once it has finished loading.</summary>
    private static bool respawnPending;

    private Volume volume;
    private VolumeProfile profile;
    private GameObject canvasObject;
    private CanvasGroup warningGroup;
    private RectTransform sign;
    private Image signGlow;
    private Text percentText;
    private CanvasGroup deathGroup;
    private bool warning;
    private float warningAmount;
    private bool dead;
    private float deadFor;

    public bool IsDead => dead;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => respawnPending = false;

    private void Awake()
    {
        if (energySource == null) energySource = GetComponent<PlayerEnergy>();
        if (energySource == null)
        {
            Debug.LogWarning("PowerFailure has no PlayerEnergy to watch.", this);
            enabled = false;
            return;
        }
        BuildGrade();
        BuildUI();
    }

    private void OnDestroy()
    {
        if (volume != null) Destroy(volume.gameObject);
        if (profile != null) Destroy(profile);
        if (canvasObject != null) Destroy(canvasObject);
    }

    private void Update()
    {
        bool loading = SaveGameService.Instance.IsLoading;
        if (respawnPending && !loading)
        {
            // The reloaded save is in place; come back with a full battery rather than the saved level.
            respawnPending = false;
            energySource.RestoreSavedEnergy(energySource.MaxEnergy);
        }
        if (dead)
        {
            UpdateDeath();
            return;
        }
        float energy = energySource.Energy01;
        if (!loading && energySource.CurrentEnergy <= 0f)
        {
            Die();
            return;
        }
        warning = warning ? energy <= warningThreshold + clearMargin : energy <= warningThreshold;
        warningAmount = Mathf.MoveTowards(warningAmount, warning ? 1f : 0f, Time.unscaledDeltaTime / fadeSeconds);
        volume.weight = SmoothStep01(warningAmount);
        UpdateWarning(energy);
    }

    // ---------- Low energy ----------

    private void BuildGrade()
    {
        var grade = new GameObject("Low Energy Grade");
        grade.transform.SetParent(transform, false);
        volume = grade.AddComponent<Volume>();
        volume.isGlobal = true;
        // Above the scene's own volumes, so the grey wins wherever the robot is.
        volume.priority = 1000f;
        volume.weight = 0f;
        profile = ScriptableObject.CreateInstance<VolumeProfile>();
        profile.name = "Low Energy Grade (runtime)";
        ColorAdjustments colour = profile.Add<ColorAdjustments>(true);
        colour.saturation.Override(saturation);
        colour.contrast.Override(10f);
        colour.postExposure.Override(-0.25f);
        Vignette edges = profile.Add<Vignette>(true);
        edges.intensity.Override(vignette);
        edges.smoothness.Override(0.5f);
        edges.color.Override(Color.black);
        volume.sharedProfile = profile;
    }

    private void UpdateWarning(float energy)
    {
        bool hidden = SaveGameService.IsPaused || GuardianIntroCutscene.IsPlaying;
        warningGroup.alpha = hidden ? 0f : SmoothStep01(warningAmount);
        if (warningGroup.alpha <= 0f) return;
        // Pulses faster the closer the battery is to empty.
        float urgency = 1f - Mathf.Clamp01(energy / warningThreshold);
        float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * Mathf.Lerp(4f, 12f, urgency));
        sign.localScale = Vector3.one * (1f + 0.06f * pulse);
        signGlow.color = WithAlpha(Red, Mathf.Lerp(0.35f, 0.8f, pulse));
        string percent = Mathf.CeilToInt(energy * 100f) + "% ENERGY";
        if (percentText.text != percent) percentText.text = percent;
    }

    // ---------- Death ----------

    private void Die()
    {
        dead = true;
        deadFor = 0f;
        warningGroup.alpha = 0f;
        volume.weight = 1f;
        // Power is gone: nothing more can be moved, picked up or used, and no save can be made while dead.
        var wheel = GetComponent<AbilityWheel>();
        if (wheel != null)
        {
            wheel.CloseForPause();
            wheel.enabled = false;
        }
        foreach (Behaviour control in new Behaviour[] { GetComponent<PlayerPickup>(), GetComponent<UnderwaterSwimController>() })
            if (control != null) control.enabled = false;
        var body = GetComponent<Rigidbody>();
        if (body != null && !body.isKinematic) body.linearVelocity = Vector3.zero;
    }

    private void UpdateDeath()
    {
        // The pause menu stops the countdown; it is not lost.
        if (SaveGameService.IsPaused) return;
        float before = deadFor;
        deadFor += Time.unscaledDeltaTime;
        deathGroup.alpha = SmoothStep01(Mathf.Clamp01(deadFor / deathFadeSeconds));
        if (before < deathSeconds && deadFor >= deathSeconds) Respawn();
    }

    private void Respawn()
    {
        respawnPending = true;
        if (SaveGameService.Instance.TryLoad(out _)) return;
        // No save yet: start the area over.
        SceneManager.LoadScene(gameObject.scene.path);
    }

    // ---------- UI ----------

    private void BuildUI()
    {
        canvasObject = CreateCanvas("Power Failure", new Vector2(1280f, 720f), 30);
        Font heading = LoadFont("Michroma-Regular", this);
        Font body = LoadFont("TitilliumWeb-SemiBold", this);

        // The warning sign: a red rhombus with an exclamation mark, centred near the top of the screen.
        RectTransform warningRoot = CreateRect("Low Energy Warning", canvasObject.transform);
        warningRoot.anchorMin = warningRoot.anchorMax = warningRoot.pivot = new Vector2(0.5f, 1f);
        warningRoot.anchoredPosition = new Vector2(0f, -40f);
        warningRoot.sizeDelta = new Vector2(420f, SignSize + 92f);
        warningGroup = warningRoot.gameObject.AddComponent<CanvasGroup>();
        warningGroup.alpha = 0f;
        warningGroup.blocksRaycasts = false;

        int texture = Mathf.RoundToInt(SignSize * 2f);
        sign = PlaceCentred(CreateRect("Sign", warningRoot), (420f - SignSize) * 0.5f, 0f, SignSize, SignSize);
        signGlow = AddImage(Stretch(CreateRect("Glow", sign), 16f), DiamondGlowSprite(texture, 32, 8f));
        AddImage(Stretch(CreateRect("Glass", sign)), DiamondSprite(texture, 0f, 8f)).color = Glass;
        AddImage(Stretch(CreateRect("Outline", sign)), DiamondSprite(texture, 6f, 8f)).color = Red;
        AddImage(Stretch(CreateRect("Inner Line", sign), -SignSize * 0.09f), DiamondSprite(texture, 2f, 8f)).color = WithAlpha(Red, 0.3f);
        AddImage(Stretch(CreateRect("Exclamation", sign), -SignSize * 0.26f), ExclamationSprite()).color = Ice;

        AddText(Place(CreateRect("Title", warningRoot), 0f, SignSize + 14f, 420f, 36f), heading, 28, "LOW ENERGY", Red, TextAnchor.MiddleCenter);
        percentText = AddText(Place(CreateRect("Charge", warningRoot), 0f, SignSize + 50f, 420f, 18f), heading, 13, "", Amber, TextAnchor.MiddleCenter);
        AddText(Place(CreateRect("Hint", warningRoot), 0f, SignSize + 70f, 420f, 22f), body, 16,
            "Stop swimming and using abilities to recharge", Ice, TextAnchor.MiddleCenter);

        // The death screen: the title screen's scanline backdrop with SIGNAL LOST.
        RectTransform death = Stretch(CreateRect("Signal Lost", canvasObject.transform));
        deathGroup = death.gameObject.AddComponent<CanvasGroup>();
        deathGroup.alpha = 0f;
        deathGroup.blocksRaycasts = false;
        RawImage backdrop = Stretch(CreateRect("Backdrop", death)).gameObject.AddComponent<RawImage>();
        backdrop.raycastTarget = false;
        backdrop.gameObject.AddComponent<HudBackdrop>();
        RectTransform message = CreateRect("Message", death);
        message.anchorMin = message.anchorMax = message.pivot = new Vector2(0.5f, 0.5f);
        message.sizeDelta = new Vector2(700f, 120f);
        AddText(Place(CreateRect("Title", message), 0f, 20f, 700f, 52f), heading, 44, "SIGNAL LOST", Red, TextAnchor.MiddleCenter);
        AddText(Place(CreateRect("Detail", message), 0f, 78f, 700f, 26f), body, 20,
            "Power depleted. Returning to the last checkpoint...", Dim, TextAnchor.MiddleCenter);
    }

    /// <summary>A bold exclamation mark: a bar tapering towards its foot over a square dot.</summary>
    private static Sprite ExclamationSprite() => CoverageSprite(72, p =>
    {
        bool bar = p.y > -0.35f && p.y < 0.95f && Mathf.Abs(p.x) < Mathf.Lerp(0.12f, 0.2f, (p.y + 0.35f) / 1.3f);
        bool dot = p.y > -0.95f && p.y < -0.62f && Mathf.Abs(p.x) < 0.17f;
        return bar || dot;
    });
}
