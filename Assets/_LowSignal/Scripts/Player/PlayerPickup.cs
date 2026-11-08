using System;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Look at an eligible item and press F to carry it; look at a surface and press F to place it.</summary>
[DisallowMultipleComponent]
public class PlayerPickup : MonoBehaviour
{
    [SerializeField] private Camera interactionCamera;
    [Tooltip("Optional child of the player at the desired carry position.")]
    [SerializeField] private Transform holdPoint;
    [SerializeField] private Vector3 localHoldOffset = new Vector3(0f, 0f, 1.5f);
    [SerializeField] private Key interactKey = Key.F;
    [SerializeField, Min(0.1f)] private float reach = 3f;
    [Tooltip("Include walls and terrain as well as items, so they block interaction.")]
    [SerializeField] private LayerMask interactionMask = ~0;
    [SerializeField, Range(0f, 89f)] private float maxPlacementSlope = 50f;
    [SerializeField, Min(0.001f)] private float placementClearance = 0.03f;
    [Tooltip("When the camera is not aiming at nearby ground, search below this distance in front of the player.")]
    [SerializeField, Min(0.1f)] private float placementDistance = 1.5f;
    [SerializeField] private bool showInteractionHint = true;

    private UnderwaterSwimController swimController;
    private PickupItem highlightedItem;
    private bool canPlace;
    private GUIStyle hintStyle;
    public PickupItem HeldItem { get; private set; }
    public bool IsHolding => HeldItem != null;
    public string InteractKeyName => interactKey.ToString().ToUpperInvariant();
    public event Action<PickupItem> ItemPickedUp;
    public event Action<PickupItem> ItemPlaced;
    public PickupItem TargetItem => highlightedItem;

    private bool InputAvailable => Time.timeScale > 0f && Application.isFocused &&
        Cursor.lockState == CursorLockMode.Locked && (swimController == null || !swimController.LookBlocked);

    private void Awake()
    {
        swimController = GetComponent<UnderwaterSwimController>();
        if (interactionCamera == null) interactionCamera = GetComponentInChildren<Camera>();
        if (interactionCamera == null) interactionCamera = Camera.main;
    }

    private void Update()
    {
        if (!InputAvailable) return;
        if (Keyboard.current != null && Keyboard.current[interactKey].wasPressedThisFrame)
            TryInteract();
    }

    private void LateUpdate()
    {
        if (IsHolding)
            HeldItem.MoveHeld(holdPoint != null ? holdPoint.position : transform.TransformPoint(localHoldOffset));
        RefreshFeedback();
    }

    /// <summary>Also callable by future input bindings. False means no valid target or placement.</summary>
    public bool TryInteract()
    {
        if (!isActiveAndEnabled) return false;
        if (IsHolding)
        {
            if (!TryGetPlacement(out Vector3 position)) return false;
            PickupItem placedItem = HeldItem;
            HeldItem = null;
            placedItem.Release(position, true);
            ItemPlaced?.Invoke(placedItem);
            RefreshFeedback();
            return true;
        }
        if (!TryGetTarget(out RaycastHit hit)) return false;

        PickupItem item = hit.collider.GetComponentInParent<PickupItem>();
        if (item == null || item.transform.IsChildOf(transform) || transform.IsChildOf(item.transform) ||
            !item.BeginCarry(this)) return false;
        HeldItem = item;
        SetHighlightedItem(null);
        item.OnPickedUp.Invoke();
        ItemPickedUp?.Invoke(item);
        return true;
    }

    private bool TryGetTarget(out RaycastHit target)
    {
        target = default;
        if (interactionCamera == null) return false;
        Ray ray = interactionCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f));
        // Third-person camera distance is separate from the player's interaction reach.
        float distance = Vector3.Distance(ray.origin, transform.position) + reach;
        RaycastHit[] hits = Physics.RaycastAll(ray, distance, interactionMask, QueryTriggerInteraction.Ignore);
        Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (RaycastHit hit in hits)
        {
            if (hit.collider.transform.IsChildOf(transform)) continue;
            if (Vector3.Distance(transform.position, hit.point) > reach) return false;
            Vector3 direction = hit.point - transform.position;
            // The camera can see around corners; the player must also have a clear path.
            foreach (RaycastHit obstruction in Physics.RaycastAll(transform.position, direction.normalized,
                         direction.magnitude, interactionMask, QueryTriggerInteraction.Ignore))
            {
                if (obstruction.collider.transform.IsChildOf(transform)) continue;
                PickupItem targetItem = hit.collider.GetComponentInParent<PickupItem>();
                if (targetItem != null && obstruction.collider.GetComponentInParent<PickupItem>() == targetItem) continue;
                if (obstruction.distance < direction.magnitude - 0.01f) return false;
            }
            target = hit;
            return true;
        }
        return false;
    }

    private bool TryGetPlacement(out Vector3 position)
    {
        position = default;
        if (!IsHolding) return false;
        // A third-person camera often points level or hits ground beyond interaction reach.
        // In that case F still puts the item on nearby ground in front of the player.
        if (TryGetTarget(out RaycastHit aimedHit) && TryPlacementOnSurface(aimedHit, out position))
            return true;

        Vector3 forward = Vector3.ProjectOnPlane(interactionCamera != null ?
            interactionCamera.transform.forward : transform.forward, Vector3.up).normalized;
        if (forward.sqrMagnitude < 0.01f)
            forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
        Vector3 origin = transform.position + forward * Mathf.Min(placementDistance, reach * 0.65f);
        RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, reach, interactionMask, QueryTriggerInteraction.Ignore);
        Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (RaycastHit hit in hits)
        {
            if (hit.collider.transform.IsChildOf(transform)) continue;
            return TryPlacementOnSurface(hit, out position);
        }
        return false;
    }

    private bool TryPlacementOnSurface(RaycastHit hit, out Vector3 position)
    {
        position = default;
        if (Vector3.Angle(Vector3.up, hit.normal) > maxPlacementSlope) return false;
        PickupItem item = HeldItem;
        Vector3 extents = item.BoundsExtents;
        // Support distance of the world bounds along the surface normal, including slopes.
        float support = Vector3.Dot(extents, new Vector3(Mathf.Abs(hit.normal.x),
            Mathf.Abs(hit.normal.y), Mathf.Abs(hit.normal.z)));
        Vector3 center = hit.point + hit.normal * (support + placementClearance);
        position = center - item.BoundsOffset;
        if (Vector3.Distance(transform.position, center) > reach) return false;
        // Use the full item bounds along the path, so fallback placement cannot cross a wall.
        Vector3 start = transform.position + Vector3.up * 0.2f;
        Vector3 path = center - start;
        foreach (RaycastHit obstruction in Physics.BoxCastAll(start, extents, path.normalized,
                     Quaternion.identity, path.magnitude, ~0, QueryTriggerInteraction.Ignore))
        {
            if (obstruction.collider.transform.IsChildOf(transform)) continue;
            return false;
        }
        // Check every physical layer, including the player. Conservative bounds prevent clipping.
        if (Physics.CheckBox(center, extents, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore)) return false;
        return true;
    }

    private void RefreshFeedback()
    {
        PickupItem target = null;
        canPlace = false;
        if (InputAvailable)
        {
            if (IsHolding) canPlace = TryGetPlacement(out _);
            else if (TryGetTarget(out RaycastHit hit))
            {
                PickupItem item = hit.collider.GetComponentInParent<PickupItem>();
                if (item != null && item.CanBeginCarry && !item.transform.IsChildOf(transform) &&
                    !transform.IsChildOf(item.transform)) target = item;
            }
        }
        SetHighlightedItem(target);
    }

    private void SetHighlightedItem(PickupItem item)
    {
        if (highlightedItem == item) return;
        if (highlightedItem != null) highlightedItem.SetHighlighted(false);
        highlightedItem = item;
        if (highlightedItem != null) highlightedItem.SetHighlighted(true);
    }

    private void OnGUI()
    {
        if (!showInteractionHint || !InputAvailable || (!IsHolding && highlightedItem == null)) return;
        if (hintStyle == null)
            hintStyle = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.MiddleCenter, fontSize = 18 };
        string hint = IsHolding ? (canPlace ? $"[{InteractKeyName}] Place item" : "Move closer to clear ground to place item") :
            $"[{InteractKeyName}] Pick up";
        GUI.Box(new Rect((Screen.width - 440f) * 0.5f, Screen.height * 0.78f, 440f, 38f), hint, hintStyle);
    }

    internal void ForgetItem(PickupItem item)
    {
        if (HeldItem == item) HeldItem = null;
    }

    /// <summary>Reattaches a saved item without replaying pickup/puzzle events.</summary>
    public bool RestoreHeldItem(PickupItem item)
    {
        if (HeldItem != null || item == null || !item.BeginCarry(this)) return false;
        HeldItem = item;
        return true;
    }

    private void OnDisable()
    {
        SetHighlightedItem(null);
        if (HeldItem != null) HeldItem.CancelCarry();
        HeldItem = null;
    }
}
