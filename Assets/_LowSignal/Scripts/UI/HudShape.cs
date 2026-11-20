using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Gives scene-authored UI the HUD's generated shapes. HudStyle draws its sprites at runtime, so they cannot be saved
/// in a scene; this re-applies them whenever the object is enabled, in Play mode and in the editor.
///  - Panel: fills the child images named Glow, Glass and Outline with a chamfered panel (see HudStyle.BuildAngularPanel).
///  - Diamond: fills this object's own Image with a rhombus.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
public sealed class HudShape : MonoBehaviour
{
    public enum Shape { Panel, Diamond }

    [SerializeField] private Shape shape = Shape.Panel;
    [Tooltip("Panel: how far the top-left and bottom-right corners are cut. Diamond: its size in pixels.")]
    [SerializeField, Min(2)] private int size = 16;
    [Tooltip("Diamond only: draw just an outline of this width; 0 fills it.")]
    [SerializeField, Min(0f)] private float border;

    // Shared between all shapes of the same kind, so a menu of buttons draws each sprite once.
    private static readonly Dictionary<string, Sprite> Cache = new Dictionary<string, Sprite>();

    public void Configure(Shape newShape, int newSize, float newBorder = 0f)
    {
        shape = newShape;
        size = newSize;
        border = newBorder;
        Apply();
    }

    private void OnEnable()
    {
        Apply();
    }

    private void OnValidate()
    {
        Apply();
    }

    private void Apply()
    {
        if (shape == Shape.Diamond)
        {
            SetSprite(GetComponent<Image>(), Cached("diamond", size, border, () => HudStyle.DiamondSprite(size, border)));
            return;
        }
        SetSprite(Child("Glow"), Cached("glow", size, 0f, () => HudStyle.ChamferGlowSprite(size, (int)HudStyle.GlowSize)));
        SetSprite(Child("Glass"), Cached("glass", size, 0f, () => HudStyle.ChamferSprite(size, 0f)));
        SetSprite(Child("Outline"), Cached("outline", size, 0f, () => HudStyle.ChamferSprite(size, 1.5f)));
    }

    private Image Child(string childName)
    {
        Transform child = transform.Find(childName);
        return child != null ? child.GetComponent<Image>() : null;
    }

    private static void SetSprite(Image image, Sprite sprite)
    {
        if (image == null)
            return;
        image.sprite = sprite;
        image.type = sprite.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
    }

    private static Sprite Cached(string kind, int size, float border, System.Func<Sprite> create)
    {
        string key = kind + size + "/" + border;
        if (!Cache.TryGetValue(key, out Sprite sprite) || sprite == null)
        {
            sprite = create();
            // Generated each session; never written into the scene or kept by the editor between loads.
            sprite.hideFlags = HideFlags.DontSave;
            sprite.texture.hideFlags = HideFlags.DontSave;
            Cache[key] = sprite;
        }
        return sprite;
    }
}
