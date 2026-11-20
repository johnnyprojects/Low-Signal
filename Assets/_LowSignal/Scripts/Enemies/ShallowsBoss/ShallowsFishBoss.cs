using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A permanent, non-combat distraction unlocked by placed treasure. Treasure set down inside the stone circle is
/// locked there (the robot places it from its PlayerInventory when in range: see Wants and TryPlaceOffering); once the circle holds them all, the guardian swims out to it and the passage behind it opens. After
/// the intro cutscene, a HUD-style quest tracker in the top-right corner shows the objective and the treasure count.
/// </summary>
public class ShallowsFishBoss : MonoBehaviour
{
    public Transform fish;
    public Transform offering;
    public Transform[] swimRoute;
    public Collider passageBlocker;
    public PickupItem[] treasures;
    [Min(1)] public int requiredTreasure = 4;
    [Min(0.1f)] public float offeringRadius = 3f;
    [Tooltip("How far above or below the circle the robot can be and still place treasure from its inventory.")]
    [Min(0.5f)] public float placementHeight = 4f;
    [Tooltip("Treasure placed from the inventory is set out on a ring this fraction of the circle's radius from its centre.")]
    [Range(0f, 0.9f)] public float offeringSpread = 0.4f;
    [Min(0.1f)] public float swimSpeed = 2f;
    public float turnSpeed = 90f;
    public enum Phase { Guarding, Swimming, Distracted }
    public Phase State { get; private set; }
    public bool PassageOpen => State == Phase.Distracted;
    public int OfferedCount => accepted.Count;
    /// <summary>Whether the player has seen the guardian's intro cutscene in this save.</summary>
    public bool IntroSeen { get; set; }
    readonly HashSet<PickupItem> accepted = new HashSet<PickupItem>();
    readonly Dictionary<PickupItem, UnityEngine.Events.UnityAction> listeners = new Dictionary<PickupItem, UnityEngine.Events.UnityAction>();
    Vector3 restingPosition;
    int waypoint;
    Transform player;
    GuardianIntroCutscene intro;
    GameObject hintCanvas;
    CanvasGroup hintGroup;
    Text hintText, hintCount, hintTip;
    Image[] hintGems;
    Sprite gemFilled, gemEmpty;

    void Start()
    {
        restingPosition = fish.position;
        var controller = FindFirstObjectByType<UnderwaterSwimController>();
        if (controller != null) player = controller.transform;
        intro = GetComponentInChildren<GuardianIntroCutscene>();
        foreach (var item in treasures)
        {
            if (item == null || listeners.ContainsKey(item)) continue;
            var captured = item;
            UnityEngine.Events.UnityAction listener = () => TryAccept(captured);
            listeners.Add(item, listener);
            item.OnPlaced.AddListener(listener);
        }
        BuildHint();
    }

    public bool TryAccept(PickupItem item)
    {
        if (State != Phase.Guarding || item == null || item.IsHeld || !item.isActiveAndEnabled ||
            System.Array.IndexOf(treasures, item) < 0 || accepted.Contains(item)) return false;
        Vector3 offset = item.transform.position - offering.position;
        if (new Vector2(offset.x, offset.z).magnitude > offeringRadius || Mathf.Abs(offset.y) > 2f) return false;
        accepted.Add(item);
        item.SetHighlighted(false);
        item.CanPickUp = false;
        item.GetComponent<Rigidbody>().isKinematic = true;
        if (accepted.Count >= requiredTreasure) State = Phase.Swimming;
        return true;
    }


    // ---------- Placing from the inventory ----------

    /// <summary>Whether this circle still needs the item: one of its treasures, not yet offered, while guarding.</summary>
    public bool Wants(PickupItem item) =>
        State == Phase.Guarding && item != null && System.Array.IndexOf(treasures, item) >= 0 && !accepted.Contains(item);

    /// <summary>Distance from the circle's edge (zero anywhere inside it), including any height difference.</summary>
    public float DistanceFromCircle(Vector3 position)
    {
        Vector3 offset = position - offering.position;
        float outside = Mathf.Max(0f, new Vector2(offset.x, offset.z).magnitude - offeringRadius);
        return new Vector2(outside, offset.y).magnitude;
    }

    /// <summary>Whether a robot at this position is close enough to place treasure in the circle.</summary>
    public bool InPlacementRange(Vector3 position, float margin)
    {
        Vector3 offset = position - offering.position;
        return new Vector2(offset.x, offset.z).magnitude <= offeringRadius + margin && Mathf.Abs(offset.y) <= placementHeight;
    }

    /// <summary>
    /// Sets a stored treasure down in the circle and accepts it. Offerings are spaced evenly round the centre, the
    /// first on the side facing the guardian, each resting on whatever is below its spot (the slab or the sand).
    /// </summary>
    public bool TryPlaceOffering(PickupItem item)
    {
        if (!Wants(item) || item.gameObject.activeSelf) return false;
        Vector3 toFish = fish.position - offering.position;
        float angle = Mathf.Atan2(toFish.z, toFish.x) + accepted.Count * Mathf.PI * 2f / Mathf.Max(1, requiredTreasure);
        Vector3 outward = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
        Vector3 spot = offering.position + outward * (offeringRadius * offeringSpread);
        spot.y = GroundBelow(spot) + 0.02f;
        item.PlaceFromInventory(spot, Quaternion.LookRotation(-outward));
        // The placed event normally accepts it; this covers a treasure added after Start.
        TryAccept(item);
        return true;
    }

    float GroundBelow(Vector3 spot)
    {
        float best = float.MaxValue, height = offering.position.y;
        foreach (var hit in Physics.RaycastAll(spot + Vector3.up * 3f, Vector3.down, 8f, ~0, QueryTriggerInteraction.Ignore))
        {
            // Ignore the robot and any loose items, so treasure rests on the circle itself.
            if (hit.distance >= best || hit.collider.GetComponentInParent<UnderwaterSwimController>() != null ||
                hit.collider.GetComponentInParent<PickupItem>() != null) continue;
            best = hit.distance;
            height = hit.point.y;
        }
        return height;
    }

    public GuardianSaveData CaptureSaveState()
    {
        var state = new GuardianSaveData
        {
            id = SaveIdentity.For(this), phase = State, waypoint = waypoint,
            fishPosition = fish.position, fishRotation = fish.rotation, introSeen = IntroSeen,
        };
        foreach (var item in accepted)
            if (item != null) state.acceptedItems.Add(SaveIdentity.For(item));
        return state;
    }

    public void RestoreSaveState(GuardianSaveData state)
    {
        accepted.Clear();
        foreach (var item in treasures)
        {
            if (item == null || !state.acceptedItems.Contains(SaveIdentity.For(item))) continue;
            accepted.Add(item);
            item.CanPickUp = false;
            item.GetComponent<Rigidbody>().isKinematic = true;
        }
        State = state.phase;
        IntroSeen = state.introSeen;
        waypoint = Mathf.Clamp(state.waypoint, 0, swimRoute != null ? swimRoute.Length : 0);
        fish.SetPositionAndRotation(state.fishPosition, state.fishRotation);
        restingPosition = state.fishPosition;
        if (passageBlocker != null) passageBlocker.enabled = !PassageOpen;
    }

    void Update()
    {
        UpdateHint();
        if (State != Phase.Swimming)
        {
            fish.position = restingPosition + Vector3.up * (Mathf.Sin(Time.time * 1.6f) * 0.12f);
            return;
        }
        if (swimRoute == null || waypoint >= swimRoute.Length) return;
        Vector3 target = swimRoute[waypoint].position;
        Vector3 delta = target - fish.position;
        if (delta.sqrMagnitude > 0.01f)
            fish.rotation = Quaternion.RotateTowards(fish.rotation, Quaternion.LookRotation(delta), turnSpeed * Time.deltaTime);
        fish.position = Vector3.MoveTowards(fish.position, target, swimSpeed * Time.deltaTime);
        if (Vector3.Distance(fish.position, target) > 0.05f) return;
        if (++waypoint < swimRoute.Length) return;
        restingPosition = target;
        State = Phase.Distracted;
        if (passageBlocker != null) passageBlocker.enabled = false;
    }

    // ---------- Quest tracker ----------

    // A HUD panel in the top-right corner, in the same style as the robot's readouts, tracking the guardian's quest.
    void BuildHint()
    {
        hintCanvas = HudStyle.CreateCanvas("Guardian Quest", new Vector2(1280, 720), 20);
        Font heading = HudStyle.LoadFont("Michroma-Regular", this), body = HudStyle.LoadFont("TitilliumWeb-SemiBold", this);
        RectTransform panel = HudStyle.CreateRect("Quest Panel", hintCanvas.transform);
        // Anchored and pivoted on the top-right corner, so it stays there at any resolution.
        panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(1, 1);
        panel.anchoredPosition = new Vector2(-32, -32);
        panel.sizeDelta = new Vector2(320, 122);
        hintGroup = panel.gameObject.AddComponent<CanvasGroup>();
        hintGroup.alpha = 0;
        HudStyle.BuildAngularPanel(panel, 18, out Image glow, out Image outline);
        glow.color = HudStyle.WithAlpha(HudStyle.Cyan, 0.3f);
        outline.color = HudStyle.Cyan;
        HudStyle.AddText(HudStyle.Place(HudStyle.CreateRect("Label", panel), 18, 10, 200, 16), heading, 10, "// QUEST", HudStyle.Dim, TextAnchor.MiddleLeft);
        HudStyle.AddText(HudStyle.Place(HudStyle.CreateRect("Title", panel), 18, 28, 290, 20), heading, 14, "THE GUARDIAN'S PRICE", HudStyle.Cyan, TextAnchor.MiddleLeft);
        hintText = HudStyle.AddText(HudStyle.Place(HudStyle.CreateRect("Objective", panel), 18, 54, 220, 22), body, 16, "", HudStyle.Ice, TextAnchor.MiddleLeft);
        hintCount = HudStyle.AddText(HudStyle.Place(HudStyle.CreateRect("Count", panel), 240, 54, 62, 22), heading, 14, "", HudStyle.Amber, TextAnchor.MiddleRight);
        gemFilled = HudStyle.DiamondSprite(14, 0);
        gemEmpty = HudStyle.DiamondSprite(14, 1.5f);
        hintGems = new Image[requiredTreasure];
        for (int i = 0; i < requiredTreasure; i++)
            hintGems[i] = HudStyle.AddImage(HudStyle.Place(HudStyle.CreateRect("Treasure " + (i + 1), panel), 18 + i * 22, 82, 14, 14), gemEmpty);
        hintTip = HudStyle.AddText(HudStyle.Place(HudStyle.CreateRect("Tip", panel), 18 + requiredTreasure * 22 + 8, 80, 300 - requiredTreasure * 22 - 26, 18),
            body, 13, "SCANNER finds hidden items", HudStyle.Dim, TextAnchor.MiddleLeft);
    }

    void UpdateHint()
    {
        if (hintGroup == null) return;
        // The quest starts once the intro cutscene has introduced the guardian.
        bool started = intro == null || IntroSeen || OfferedCount > 0;
        bool show = started && player != null && !SaveGameService.IsPaused && !GuardianIntroCutscene.IsPlaying;
        hintGroup.alpha = Mathf.MoveTowards(hintGroup.alpha, show ? 1 : 0, Time.unscaledDeltaTime * 4);
        if (hintGroup.alpha <= 0) return;
        hintText.text = State == Phase.Guarding ? "Place treasure in the stone circle"
            : State == Phase.Swimming ? "The guardian follows its treasure..."
            : "Enter the cave behind the guardian";
        hintCount.text = OfferedCount + " / " + requiredTreasure;
        hintTip.enabled = State == Phase.Guarding;
        for (int i = 0; i < hintGems.Length; i++)
        {
            bool have = i < OfferedCount;
            hintGems[i].sprite = have ? gemFilled : gemEmpty;
            hintGems[i].color = have ? HudStyle.Amber : HudStyle.Dim;
        }
    }

    void OnDestroy()
    {
        foreach (var pair in listeners)
            if (pair.Key != null) pair.Key.OnPlaced.RemoveListener(pair.Value);
        if (hintCanvas != null) Destroy(hintCanvas);
    }

    void OnDrawGizmosSelected()
    {
        if (offering == null) return;
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(offering.position, offeringRadius);
        if (fish == null || swimRoute == null) return;
        Vector3 previous = fish.position;
        foreach (var point in swimRoute)
        {
            if (point == null) continue;
            Gizmos.DrawLine(previous, point.position);
            previous = point.position;
        }
    }
}
