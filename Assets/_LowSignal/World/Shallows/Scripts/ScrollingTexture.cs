using UnityEngine;

/// <summary>
/// Animates the underside of the water surface. When the sun has a CausticsAnimator, the surface shows the same
/// smoothly flowing caustic pattern as the sea floor, at the same tile size, drift and gentle swell, so the ripples
/// overhead move in step with the light below. Without one, it slides its own ripple texture slowly instead.
/// Uses a property block, so the shared material asset is never changed.
/// </summary>
[RequireComponent(typeof(Renderer))]
// After the caustics animator has blended this frame's pattern.
[DefaultExecutionOrder(100)]
public class ScrollingTexture : MonoBehaviour
{
    private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
    private static readonly int BaseMapStId = Shader.PropertyToID("_BaseMap_ST");

    [Tooltip("Show the sea floor's caustics on the surface, moving in step with them, when the sun has a Caustics Animator.")]
    [SerializeField] private bool followSeaFloorCaustics = true;
    [Tooltip("Without caustics to follow: times the ripple texture repeats across the surface.")]
    [SerializeField] private Vector2 tiling = new Vector2(12f, 12f);
    [Tooltip("Without caustics to follow: texture widths per second the ripples slide.")]
    [SerializeField] private Vector2 speed = new Vector2(0.004f, 0.0025f);

    private Renderer target;
    private MaterialPropertyBlock properties;
    private CausticsAnimator caustics;

    private void Awake()
    {
        target = GetComponent<Renderer>();
        properties = new MaterialPropertyBlock();
        if (followSeaFloorCaustics)
            caustics = FindAnyObjectByType<CausticsAnimator>();
    }

    private void LateUpdate()
    {
        target.GetPropertyBlock(properties);
        Texture pattern = caustics != null && caustics.isActiveAndEnabled ? caustics.Pattern : null;
        if (pattern != null)
        {
            // One caustic tile covers the same ground here as on the floor, and drifts the same way.
            Vector3 size = target.bounds.size;
            Vector2 tile = caustics.CurrentTileSize;
            Vector2 offset = caustics.CurrentOffset;
            properties.SetTexture(BaseMapId, pattern);
            properties.SetVector(BaseMapStId, new Vector4(size.x / tile.x, size.z / tile.y, offset.x / tile.x, offset.y / tile.y));
        }
        else
        {
            // Fall back to the material's own ripple texture, sliding slowly.
            properties.Clear();
            Vector2 slide = speed * Time.time;
            properties.SetVector(BaseMapStId, new Vector4(tiling.x, tiling.y, slide.x, slide.y));
        }
        target.SetPropertyBlock(properties);
    }
}
