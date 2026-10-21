using UnityEngine;
using UnityEngine.Events;

/// <summary>Opt-in marker for a movable item. Use one root Rigidbody and child colliders.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
public class PickupItem : MonoBehaviour
{
    [Tooltip("Identifier for puzzles or other systems that need to know what item this is.")]
    [SerializeField] private string itemId;
    [SerializeField] private bool canPickUp = true;
    [SerializeField] private UnityEvent onPickedUp = new UnityEvent();
    [SerializeField] private UnityEvent onPlaced = new UnityEvent();
    [Header("Pickup Highlight")]
    [SerializeField, ColorUsage(false, true)] private Color highlightColor = new Color(0.2f, 1.4f, 1.8f, 1f);

    private Renderer[] highlightRenderers;
    private Material[][] originalMaterials;
    private Material[][] highlightMaterials;
    public bool IsHighlighted { get; private set; }

    private Rigidbody body;
    private Collider[] colliders;
    private bool[] colliderStates;
    private bool wasKinematic;
    private Vector3 originalPosition;
    private Quaternion originalRotation;
    private Vector3 boundsOffset;
    private Vector3 boundsExtents;

    public string ItemId => itemId;
    public bool CanPickUp { get => canPickUp; set => canPickUp = value; }
    public PlayerPickup Carrier { get; private set; }
    public bool IsHeld => Carrier != null;
    public UnityEvent OnPickedUp => onPickedUp;
    public UnityEvent OnPlaced => onPlaced;
    internal Vector3 BoundsOffset => boundsOffset;
    internal Vector3 BoundsExtents => boundsExtents;
    internal bool CanBeginCarry => isActiveAndEnabled && canPickUp && !IsHeld &&
        GetComponentsInChildren<Rigidbody>(true).Length == 1 && GetComponentsInChildren<Joint>(true).Length == 0;

    /// <summary>Uses temporary materials; shared prefab materials are never edited.</summary>
    public void SetHighlighted(bool highlighted)
    {
        highlighted &= CanBeginCarry;
        if (IsHighlighted == highlighted) return;
        if (highlighted && highlightRenderers == null)
        {
            highlightRenderers = GetComponentsInChildren<Renderer>(true);
            originalMaterials = new Material[highlightRenderers.Length][];
            highlightMaterials = new Material[highlightRenderers.Length][];
            for (int i = 0; i < highlightRenderers.Length; i++)
            {
                originalMaterials[i] = highlightRenderers[i].sharedMaterials;
                highlightMaterials[i] = new Material[originalMaterials[i].Length];
                for (int j = 0; j < originalMaterials[i].Length; j++)
                {
                    Material source = originalMaterials[i][j];
                    if (source == null) continue;
                    Material glow = new Material(source) { name = source.name + " (Pickup Highlight)", hideFlags = HideFlags.HideAndDontSave };
                    if (glow.HasProperty("_BaseColor")) glow.SetColor("_BaseColor", Color.Lerp(source.GetColor("_BaseColor"), Color.cyan, 0.65f));
                    if (glow.HasProperty("_Color")) glow.SetColor("_Color", Color.Lerp(source.GetColor("_Color"), Color.cyan, 0.65f));
                    if (glow.HasProperty("_EmissionColor"))
                    {
                        glow.EnableKeyword("_EMISSION");
                        glow.SetColor("_EmissionColor", highlightColor);
                    }
                    highlightMaterials[i][j] = glow;
                }
            }
        }
        IsHighlighted = highlighted;
        if (highlightRenderers == null) return;
        for (int i = 0; i < highlightRenderers.Length; i++)
            if (highlightRenderers[i] != null)
                highlightRenderers[i].sharedMaterials = highlighted ? highlightMaterials[i] : originalMaterials[i];
    }

    internal bool BeginCarry(PlayerPickup carrier)
    {
        if (!isActiveAndEnabled || !canPickUp || IsHeld)
            return false;

        body = GetComponent<Rigidbody>();
        // Jointed or multi-body props need their own carrying implementation.
        if (GetComponentsInChildren<Rigidbody>(true).Length != 1 ||
            GetComponentsInChildren<Joint>(true).Length != 0)
            return false;

        colliders = GetComponentsInChildren<Collider>(true);
        colliderStates = new bool[colliders.Length];
        Bounds bounds = new Bounds();
        bool hasBounds = false;
        for (int i = 0; i < colliders.Length; i++)
        {
            colliderStates[i] = colliders[i].enabled;
            if (!colliders[i].enabled || !colliders[i].gameObject.activeInHierarchy || colliders[i].isTrigger)
                continue;
            if (!hasBounds) bounds = colliders[i].bounds;
            else bounds.Encapsulate(colliders[i].bounds);
            hasBounds = true;
        }
        if (!hasBounds)
            return false;

        SetHighlighted(false);
        originalPosition = transform.position;
        originalRotation = transform.rotation;
        boundsOffset = bounds.center - originalPosition;
        boundsExtents = bounds.extents;
        wasKinematic = body.isKinematic;
        if (!wasKinematic)
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }
        body.isKinematic = true;
        foreach (Collider itemCollider in colliders)
            itemCollider.enabled = false;
        Carrier = carrier;
        return true;
    }

    internal void MoveHeld(Vector3 position)
    {
        // Keep the original world orientation, including for placement bounds.
        transform.SetPositionAndRotation(position, originalRotation);
        body.position = position;
        body.rotation = originalRotation;
    }

    internal void Release(Vector3 position, bool placed)
    {
        MoveHeld(position);
        Carrier = null;
        for (int i = 0; i < colliders.Length; i++)
            if (colliders[i] != null) colliders[i].enabled = colliderStates[i];
        body.isKinematic = wasKinematic;
        if (!wasKinematic)
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }
        if (placed) onPlaced.Invoke();
    }

    internal void CancelCarry()
    {
        if (IsHeld) Release(originalPosition, false);
    }

    private void OnDisable()
    {
        SetHighlighted(false);
        if (!IsHeld) return;
        PlayerPickup previousCarrier = Carrier;
        CancelCarry();
        previousCarrier.ForgetItem(this);
    }

    private void OnDestroy()
    {
        SetHighlighted(false);
        if (highlightMaterials == null) return;
        foreach (Material[] materials in highlightMaterials)
            foreach (Material material in materials)
                if (material != null)
                {
                    if (Application.isPlaying) Destroy(material);
                    else DestroyImmediate(material);
                }
    }
}
