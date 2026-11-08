using System.Collections.Generic;
using UnityEngine;

/// <summary>A permanent, non-combat distraction unlocked by placed treasure.</summary>
public class ShallowsFishBoss : MonoBehaviour
{
    public Transform fish;
    public Transform offering;
    public Transform[] swimRoute;
    public Collider passageBlocker;
    public PickupItem[] treasures;
    [Min(1)] public int requiredTreasure = 3;
    [Min(0.1f)] public float offeringRadius = 3f;
    [Min(0.1f)] public float swimSpeed = 2f;
    public float turnSpeed = 90f;
    public float hintDistance = 18f;
    public enum Phase { Guarding, Swimming, Distracted }
    public Phase State { get; private set; }
    public bool PassageOpen => State == Phase.Distracted;
    public int OfferedCount => accepted.Count;
    readonly HashSet<PickupItem> accepted = new HashSet<PickupItem>();
    readonly Dictionary<PickupItem, UnityEngine.Events.UnityAction> listeners = new Dictionary<PickupItem, UnityEngine.Events.UnityAction>();
    Vector3 restingPosition;
    int waypoint;
    Transform player;
    GUIStyle hint;

    void Start()
    {
        restingPosition = fish.position;
        var controller = FindFirstObjectByType<UnderwaterSwimController>();
        if (controller != null) player = controller.transform;
        foreach (var item in treasures)
        {
            if (item == null || listeners.ContainsKey(item)) continue;
            var captured = item;
            UnityEngine.Events.UnityAction listener = () => TryAccept(captured);
            listeners.Add(item, listener);
            item.OnPlaced.AddListener(listener);
        }
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

    public GuardianSaveData CaptureSaveState()
    {
        var state = new GuardianSaveData
        {
            id = SaveIdentity.For(this), phase = State, waypoint = waypoint,
            fishPosition = fish.position, fishRotation = fish.rotation,
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
        waypoint = Mathf.Clamp(state.waypoint, 0, swimRoute != null ? swimRoute.Length : 0);
        fish.SetPositionAndRotation(state.fishPosition, state.fishRotation);
        restingPosition = state.fishPosition;
        if (passageBlocker != null) passageBlocker.enabled = !PassageOpen;
    }

    void Update()
    {
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

    void OnGUI()
    {
        if (player == null || Time.timeScale == 0 || Vector3.Distance(player.position, offering.position) > hintDistance) return;
        if (hint == null) hint = new GUIStyle(GUI.skin.box) { fontSize = 18, alignment = TextAnchor.MiddleCenter, wordWrap = true };
        string message = State == Phase.Guarding
            ? $"The guardian covets treasure.\nPlace treasure in the golden ring: {OfferedCount}/{requiredTreasure}"
            : State == Phase.Swimming ? "The guardian is following the treasure..." : "The guardian is distracted. Enter the cave behind him.";
        GUI.Box(new Rect((Screen.width - 560) / 2f, Screen.height * 0.15f, 560, 62), message, hint);
    }

    void OnDestroy()
    {
        foreach (var pair in listeners)
            if (pair.Key != null) pair.Key.OnPlaced.RemoveListener(pair.Value);
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
