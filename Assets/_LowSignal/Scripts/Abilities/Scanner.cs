using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using static HudStyle;

/// <summary>
/// Attach to the Player. The SCANNER ability finds shiny things: using it (right click with SCANNER selected on the ability
/// wheel) sends a faint golden shell sweeping slowly out to the edge of its range, and every item that can still be
/// picked up glints as the sweep passes it: a warm light flares at the item a few times, so treasure hidden among rocks
/// and seagrass stands out. Each found item also gets a HUD marker for a while: a gold rhombus over it with its distance,
/// which slides to the edge of the screen with an arrow when the item is off-screen or behind the camera, and goes once
/// the item is picked up. Each sweep costs a chunk of the shared energy (PlayerEnergy), on top of its cooldown, so it
/// cannot be spammed; without enough energy it does not fire and the HUD shows NO POWER. SONAR BLAST is the short,
/// bright pulse.
/// </summary>
[DisallowMultipleComponent]
public class Scanner : MonoBehaviour, IRobotAbility
{
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int RimPowerId = Shader.PropertyToID("_RimPower");

    [Header("Sweep")]
    [Tooltip("Seconds before the scanner can sweep again.")]
    [SerializeField, Min(0f)] private float cooldown = 4f;
    [Tooltip("Energy each sweep costs, out of PlayerEnergy's maximum (100 by default). The whole cost must be available.")]
    [SerializeField, Min(0f)] private float energyCost = 20f;
    [Tooltip("Shared player energy. Found on this object when left empty; with none, sweeps are free.")]
    [SerializeField] private PlayerEnergy energySource;
    [Tooltip("How far the sweep reaches, in metres. Items within it glint.")]
    [SerializeField, Min(1f)] private float range = 45f;
    [Tooltip("How fast the sweep travels outward, in metres per second.")]
    [SerializeField, Min(1f)] private float sweepSpeed = 30f;
    [SerializeField] private Color sweepColor = new Color(1f, 0.85f, 0.45f, 0.5f);
    [SerializeField, Range(0.5f, 8f)] private float rimSharpness = 4f;
    [Tooltip("Material using the Low Signal/Sonar Pulse shader. Assigned from Characters/Robot/Effects by the editor setup.")]
    [SerializeField] private Material pulseMaterial;

    [Header("Glint")]
    [Tooltip("Seconds each found item glints for.")]
    [SerializeField, Min(0.1f)] private float glintSeconds = 4f;
    [SerializeField] private Color glintColor = new Color(1f, 0.78f, 0.35f);
    [SerializeField, Min(0f)] private float glintIntensity = 4f;
    [SerializeField, Min(0.5f)] private float glintRange = 5f;

    [Header("Markers")]
    [Tooltip("Seconds each found item stays marked on the HUD.")]
    [SerializeField, Min(0.5f)] private float markerSeconds = 8f;
    [Tooltip("Width and height of a marker's rhombus, in reference pixels (1280x720 canvas).")]
    [SerializeField, Min(8f)] private float markerSize = 26f;
    [Tooltip("How far from the screen's edges markers of off-screen items are held, in reference pixels.")]
    [SerializeField, Min(0f)] private float edgeMargin = 48f;
    [Tooltip("Height above the item's origin that its marker points at, in metres.")]
    [SerializeField] private float markerLift = 0.4f;

    private sealed class Marker
    {
        public PickupItem Item;
        public RectTransform Root;
        public CanvasGroup Group;
        public RectTransform Diamond;
        public RectTransform Arrow;
        public Text Distance;
        public float Started;
    }

    private MaterialPropertyBlock properties;
    private float readyAt;
    private Camera viewCamera;
    private GameObject markerCanvas;
    private RectTransform markerArea;
    private CanvasGroup markerGroup;
    private Font markerFont;
    private Sprite markerGlow, markerGlass, markerRing, markerCore, markerArrow;
    private readonly List<Marker> markers = new List<Marker>();

    public AbilityId Id => AbilityId.Scanner;
    public bool IsActive => Time.time < readyAt;
    public string Status => IsActive ? (readyAt - Time.time).ToString("0.0") + "s" : HasEnergyForSweep ? "READY" : "NO POWER";
    private bool HasEnergyForSweep => energySource == null || energySource.CurrentEnergy >= energyCost;

    private void Awake()
    {
        properties = new MaterialPropertyBlock();
        if (energySource == null)
            energySource = GetComponent<PlayerEnergy>();
        if (pulseMaterial == null)
        {
            Shader shader = Shader.Find("Low Signal/Sonar Pulse");
            if (shader != null)
                pulseMaterial = new Material(shader);
        }
    }

    public bool Use()
    {
        if (Time.time < readyAt)
            return false;
        // The whole cost or nothing: a sweep never fires on a nearly flat battery.
        if (energySource != null && !energySource.TryConsume(energyCost))
            return false;
        readyAt = Time.time + cooldown;
        StartCoroutine(Sweep(transform.position));
        return true;
    }

    private IEnumerator Sweep(Vector3 centre)
    {
        var waiting = new List<PickupItem>();
        foreach (PickupItem item in FindObjectsByType<PickupItem>(FindObjectsSortMode.None))
            if (item.CanPickUp && !item.IsHeld && item.isActiveAndEnabled && Vector3.Distance(item.transform.position, centre) <= range)
                waiting.Add(item);
        GameObject sphere = null;
        MeshRenderer shell = null;
        if (pulseMaterial != null)
        {
            sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = "Scanner Sweep";
            Destroy(sphere.GetComponent<Collider>());
            sphere.transform.position = centre;
            shell = sphere.GetComponent<MeshRenderer>();
            shell.sharedMaterial = pulseMaterial;
            shell.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            shell.receiveShadows = false;
        }
        float duration = range / sweepSpeed;
        for (float elapsed = 0f; elapsed < duration; elapsed += Time.deltaTime)
        {
            float radius = range * elapsed / duration;
            // Each item glints the moment the sweep reaches it.
            for (int i = waiting.Count - 1; i >= 0; i--)
            {
                if (waiting[i] == null) { waiting.RemoveAt(i); continue; }
                if (Vector3.Distance(waiting[i].transform.position, centre) > radius) continue;
                StartCoroutine(Glint(waiting[i]));
                waiting.RemoveAt(i);
            }
            if (sphere != null)
            {
                Color color = sweepColor;
                color.a *= 1f - elapsed / duration;
                sphere.transform.localScale = Vector3.one * (radius * 2f);
                properties.SetColor(ColorId, color);
                properties.SetFloat(RimPowerId, rimSharpness);
                shell.SetPropertyBlock(properties);
            }
            yield return null;
        }
        foreach (PickupItem item in waiting)
            if (item != null) StartCoroutine(Glint(item));
        if (sphere != null)
            Destroy(sphere);
    }

    // A warm light that flares three times at the item and fades.
    private IEnumerator Glint(PickupItem item)
    {
        MarkItem(item);
        var glint = new GameObject("Scanner Glint");
        glint.transform.SetParent(item.transform, false);
        glint.transform.localPosition = Vector3.up * 0.4f;
        Light light = glint.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = glintColor;
        light.range = glintRange;
        light.shadows = LightShadows.None;
        for (float elapsed = 0f; elapsed < glintSeconds && glint != null; elapsed += Time.deltaTime)
        {
            float t = elapsed / glintSeconds;
            float flare = Mathf.Pow(Mathf.Abs(Mathf.Sin(t * Mathf.PI * 3f)), 2f);
            light.intensity = glintIntensity * flare * (1f - t * 0.5f);
            yield return null;
        }
        if (glint != null)
            Destroy(glint);
    }

    // ---------- HUD markers ----------

    private void MarkItem(PickupItem item)
    {
        EnsureMarkerCanvas();
        Marker marker = markers.Find(m => m.Item == item);
        if (marker == null)
        {
            marker = BuildMarker(item);
            markers.Add(marker);
        }
        // Scanning an item that is already marked starts its time again.
        marker.Started = Time.time;
    }

    private void EnsureMarkerCanvas()
    {
        if (markerCanvas != null) return;
        markerCanvas = CreateCanvas("Scanner Markers", new Vector2(1280f, 720f), 9);
        markerArea = (RectTransform)markerCanvas.transform;
        markerGroup = markerCanvas.AddComponent<CanvasGroup>();
        markerGroup.blocksRaycasts = false;
        markerFont = LoadFont("Michroma-Regular", this);
        // Drawn at twice the marker size so they stay crisp when the canvas scales up.
        int texture = Mathf.RoundToInt(markerSize * 2f);
        markerGlow = DiamondGlowSprite(texture, 12, 4f);
        markerGlass = DiamondSprite(texture, 0f, 4f);
        markerRing = DiamondSprite(texture, 3f, 4f);
        markerCore = DiamondSprite(Mathf.RoundToInt(texture * 0.3f), 0f, 2f);
        markerArrow = ChevronSprite(36, true);
        if (viewCamera == null) viewCamera = GetComponentInChildren<Camera>();
    }

    private Marker BuildMarker(PickupItem item)
    {
        var marker = new Marker { Item = item };
        marker.Root = CreateRect("Marker " + item.name, markerArea);
        marker.Root.anchorMin = marker.Root.anchorMax = Vector2.zero;
        marker.Root.pivot = new Vector2(0.5f, 0.5f);
        marker.Root.sizeDelta = Vector2.one * markerSize;
        marker.Group = marker.Root.gameObject.AddComponent<CanvasGroup>();
        marker.Group.alpha = 0f;
        // A gold rhombus with a bright core, like the quest tracker's treasure gems.
        marker.Diamond = Stretch(CreateRect("Rhombus", marker.Root));
        AddImage(Stretch(CreateRect("Glow", marker.Diamond), 6f), markerGlow).color = WithAlpha(Amber, 0.45f);
        AddImage(Stretch(CreateRect("Glass", marker.Diamond)), markerGlass).color = WithAlpha(Ink, 0.55f);
        AddImage(Stretch(CreateRect("Outline", marker.Diamond)), markerRing).color = Amber;
        AddImage(Stretch(CreateRect("Core", marker.Diamond), -markerSize * 0.35f), markerCore).color = Ice;
        marker.Arrow = CreateRect("Arrow", marker.Root);
        marker.Arrow.sizeDelta = Vector2.one * 12f;
        AddImage(marker.Arrow, markerArrow).color = Amber;
        RectTransform label = CreateRect("Distance", marker.Root);
        label.anchorMin = label.anchorMax = new Vector2(0.5f, 0f);
        label.pivot = new Vector2(0.5f, 1f);
        label.anchoredPosition = new Vector2(0f, -6f);
        label.sizeDelta = new Vector2(80f, 16f);
        marker.Distance = AddText(label, markerFont, 11, "", Ice, TextAnchor.MiddleCenter);
        return marker;
    }

    private void LateUpdate()
    {
        if (markerCanvas == null) return;
        if (viewCamera == null || !viewCamera.isActiveAndEnabled) viewCamera = Camera.main;
        markerGroup.alpha = SaveGameService.IsPaused || GuardianIntroCutscene.IsPlaying || viewCamera == null ? 0f : 1f;
        Vector2 size = markerArea.rect.size;
        for (int i = markers.Count - 1; i >= 0; i--)
        {
            Marker marker = markers[i];
            float age = Time.time - marker.Started;
            // Gone once picked up (stored items are inactive), placed in the circle, or timed out.
            if (marker.Item == null || !marker.Item.isActiveAndEnabled || !marker.Item.CanPickUp || marker.Item.IsHeld || age > markerSeconds)
            {
                Destroy(marker.Root.gameObject);
                markers.RemoveAt(i);
                continue;
            }
            if (viewCamera != null) PlaceMarker(marker, age, size);
        }
    }

    private void PlaceMarker(Marker marker, float age, Vector2 size)
    {
        Vector3 world = marker.Item.transform.position + Vector3.up * markerLift;
        Vector3 viewport = viewCamera.WorldToViewportPoint(world);
        bool behind = viewport.z < 0f;
        // Behind the camera the projection is mirrored, so flip it to point the right way round.
        Vector2 point = behind ? Vector2.one - (Vector2)viewport : (Vector2)viewport;
        Vector2 position = Vector2.Scale(point, size);
        Vector2 centre = size * 0.5f;
        Vector2 half = Vector2.Max(centre - Vector2.one * edgeMargin, Vector2.one);
        Vector2 offset = position - centre;
        bool offScreen = behind || Mathf.Abs(offset.x) > half.x || Mathf.Abs(offset.y) > half.y;
        if (offScreen)
        {
            if (offset.sqrMagnitude < 1f) offset = Vector2.down;
            // Slide out along the same direction until it meets the inset screen edge.
            float scale = Mathf.Min(half.x / Mathf.Max(Mathf.Abs(offset.x), 0.001f), half.y / Mathf.Max(Mathf.Abs(offset.y), 0.001f));
            position = centre + offset * scale;
        }
        marker.Root.anchoredPosition = position;

        marker.Arrow.gameObject.SetActive(offScreen);
        if (offScreen)
        {
            Vector2 direction = offset.normalized;
            marker.Arrow.anchoredPosition = direction * (markerSize * 0.5f + 10f);
            marker.Arrow.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg - 90f);
        }

        int metres = Mathf.RoundToInt(Vector3.Distance(transform.position, marker.Item.transform.position));
        string distance = metres + " M";
        if (marker.Distance.text != distance) marker.Distance.text = distance;

        // Pops in as the sweep finds it, then fades over its last second.
        float appear = SmoothStep01(Mathf.Clamp01(age / 0.3f));
        marker.Diamond.localScale = Vector3.one * Mathf.Lerp(1.8f, 1f, appear);
        marker.Group.alpha = appear * Mathf.Clamp01(markerSeconds - age);
    }

    private void OnDestroy()
    {
        if (markerCanvas != null) Destroy(markerCanvas);
    }
}
