using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// A menu button in the HUD style: a chamfered glass panel whose outline, glow, label and side marker light up cyan
/// while it is selected or hovered, and dim when it is unavailable. Hovering selects it, so the mouse and keyboard
/// always agree on which button is lit.
/// </summary>
public sealed class HudMenuButton : Button
{
    [SerializeField] private Image glow;
    [SerializeField] private Image glass;
    [SerializeField] private Image outline;
    [SerializeField] private Text label;
    [SerializeField] private Graphic marker;

    private static readonly Color GlassPressed = new Color(0.08f, 0.32f, 0.42f, 0.92f);

    public Text Label => label;

    public void Bind(Image newGlow, Image newGlass, Image newOutline, Text newLabel, Graphic newMarker)
    {
        glow = newGlow;
        glass = newGlass;
        outline = newOutline;
        label = newLabel;
        marker = newMarker;
        transition = Transition.None;
        targetGraphic = newGlass;
        DoStateTransition(currentSelectionState, true);
    }

    public override void OnPointerEnter(PointerEventData eventData)
    {
        base.OnPointerEnter(eventData);
        if (IsInteractable() && EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(gameObject);
    }

    protected override void DoStateTransition(SelectionState state, bool instant)
    {
        base.DoStateTransition(state, instant);
        if (outline == null)
            return;
        bool lit = state == SelectionState.Selected || state == SelectionState.Highlighted || state == SelectionState.Pressed;
        bool disabled = state == SelectionState.Disabled;
        outline.color = disabled ? HudStyle.WithAlpha(HudStyle.Dim, 0.2f) : lit ? HudStyle.Cyan : HudStyle.WithAlpha(HudStyle.Dim, 0.55f);
        glow.color = HudStyle.WithAlpha(HudStyle.Cyan, lit ? 0.55f : 0f);
        glass.color = state == SelectionState.Pressed ? GlassPressed : HudStyle.Glass;
        label.color = disabled ? HudStyle.WithAlpha(HudStyle.Dim, 0.4f) : lit ? HudStyle.Cyan : HudStyle.Ice;
        if (marker != null)
            marker.color = HudStyle.WithAlpha(HudStyle.Cyan, lit ? 1f : 0f);
    }
}
