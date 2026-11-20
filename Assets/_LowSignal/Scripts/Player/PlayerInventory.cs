using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Attach to the Player, next to PlayerPickup. Items the robot picks up go in here and are hidden from the world,
/// instead of being carried. An item only leaves the inventory by being placed in a stone circle (a ShallowsFishBoss
/// offering) that wants it, while the robot is within range of that circle: one item per press of the interact key.
/// InventoryHUD shows the contents and the prompts. Contents are saved with each scene's player state, since the
/// items themselves are objects in that scene.
/// </summary>
[DisallowMultipleComponent]
public class PlayerInventory : MonoBehaviour
{
    public enum CircleStatus { None, TooFar, InRange }

    [SerializeField, Min(1)] private int capacity = 6;
    [Tooltip("How far beyond a stone circle's edge the robot can still place an item in it.")]
    [SerializeField, Min(0f)] private float placeMargin = 1.5f;
    [Tooltip("Within this distance of a circle that wants an item, but out of range, the HUD hints to move closer.")]
    [SerializeField, Min(0f)] private float hintDistance = 14f;

    private readonly List<PickupItem> items = new List<PickupItem>();
    private ShallowsFishBoss[] circles = Array.Empty<ShallowsFishBoss>();

    public IReadOnlyList<PickupItem> Items => items;
    public int Capacity => capacity;
    public int Count => items.Count;
    public bool IsFull => items.Count >= capacity;
    /// <summary>Whether a stone circle that wants a stored item is in range, nearby, or neither.</summary>
    public CircleStatus Status { get; private set; }
    /// <summary>The item the next press would place in the nearest wanting circle, or null.</summary>
    public PickupItem NextOffering { get; private set; }
    public event Action<PickupItem> ItemAdded;
    public event Action<PickupItem> ItemRemoved;

    private ShallowsFishBoss nearestCircle;

    private void Start()
    {
        circles = FindObjectsByType<ShallowsFishBoss>(FindObjectsSortMode.None);
        RefreshStatus();
    }

    private void Update()
    {
        RefreshStatus();
    }

    public bool CanStore(PickupItem item) =>
        item != null && !IsFull && !items.Contains(item) && item.CanBeginCarry;

    public bool TryStore(PickupItem item)
    {
        if (!CanStore(item)) return false;
        items.Add(item);
        item.StoreInInventory();
        ItemAdded?.Invoke(item);
        RefreshStatus();
        return true;
    }

    /// <summary>Places the next wanted item in the stone circle, if the robot is within range of one.</summary>
    public bool TryPlaceInCircle(out PickupItem placed)
    {
        placed = null;
        RefreshStatus();
        if (Status != CircleStatus.InRange || !nearestCircle.TryPlaceOffering(NextOffering)) return false;
        placed = NextOffering;
        items.Remove(placed);
        ItemRemoved?.Invoke(placed);
        RefreshStatus();
        return true;
    }

    private void RefreshStatus()
    {
        items.RemoveAll(item => item == null);
        Status = CircleStatus.None;
        NextOffering = null;
        nearestCircle = null;
        float nearest = float.MaxValue;
        foreach (ShallowsFishBoss circle in circles)
        {
            if (circle == null || !circle.isActiveAndEnabled) continue;
            PickupItem wanted = items.Find(circle.Wants);
            if (wanted == null) continue;
            float distance = circle.DistanceFromCircle(transform.position);
            if (distance >= nearest) continue;
            nearest = distance;
            nearestCircle = circle;
            NextOffering = wanted;
        }
        if (nearestCircle == null) return;
        Status = nearestCircle.InPlacementRange(transform.position, placeMargin) ? CircleStatus.InRange
            : nearest <= hintDistance ? CircleStatus.TooFar : CircleStatus.None;
    }

    // ---------- Saving ----------

    public void CaptureSaveState(PlayerSaveData state)
    {
        state.inventory = new List<string>();
        foreach (PickupItem item in items)
            if (item != null) state.inventory.Add(SaveIdentity.For(item));
    }

    /// <summary>Call after the scene's items are restored; stored items come back inactive and are hidden again here.</summary>
    public void RestoreSaveState(PlayerSaveData state, IReadOnlyDictionary<string, PickupItem> sceneItems)
    {
        items.Clear();
        if (state?.inventory != null)
            foreach (string id in state.inventory)
                if (items.Count < capacity && sceneItems.TryGetValue(id, out PickupItem item) && item != null && !items.Contains(item))
                {
                    items.Add(item);
                    item.StoreInInventory();
                }
        RefreshStatus();
    }
}
