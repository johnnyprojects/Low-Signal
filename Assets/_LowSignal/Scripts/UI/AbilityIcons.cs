using System.Collections.Generic;
using UnityEngine;
using static HudStyle;

/// <summary>
/// Small white icons for the abilities, the lock on locked slots, and the right-click mouse hint, generated at runtime
/// so no art is needed yet. Each is drawn once at three times its display size and shared.
/// </summary>
public static class AbilityIcons
{
    private const int Size = 72;
    private static readonly Dictionary<AbilityId, Sprite> Cache = new Dictionary<AbilityId, Sprite>();
    private static Sprite lockSprite;
    private static Sprite mouseBody;
    private static Sprite mouseRightButton;

    /// <summary>The ability's icon, or null for the empty slot.</summary>
    public static Sprite For(AbilityId id)
    {
        if (id == AbilityId.Empty)
            return null;
        if (!Cache.TryGetValue(id, out Sprite sprite) || sprite == null)
            Cache[id] = sprite = CoverageSprite(Size, Shape(id));
        return sprite;
    }

    public static Sprite Lock => lockSprite != null ? lockSprite : lockSprite = CoverageSprite(Size, p =>
    {
        // A padlock: a rounded shackle over a solid body with a keyhole.
        bool body = Mathf.Abs(p.x) <= 0.55f && p.y >= -0.85f && p.y <= 0.1f;
        bool keyhole = (p - new Vector2(0f, -0.3f)).magnitude < 0.13f || (Mathf.Abs(p.x) < 0.06f && p.y > -0.6f && p.y < -0.3f);
        float shackle = (p - new Vector2(0f, 0.1f)).magnitude;
        bool arc = p.y >= 0.1f && shackle > 0.27f && shackle < 0.43f;
        return (body && !keyhole) || arc;
    });

    /// <summary>Outline of a mouse with its buttons split, 20 by 28 reference pixels (drawn at three times that).</summary>
    public static Sprite MouseBody => mouseBody != null ? mouseBody : mouseBody = MouseSprite(false);
    /// <summary>The right button's fill, to lay over MouseBody in an accent colour.</summary>
    public static Sprite MouseRightButton => mouseRightButton != null ? mouseRightButton : mouseRightButton = MouseSprite(true);

    private static Sprite MouseSprite(bool rightButton)
    {
        const float width = 20f, height = 28f, radius = 9.5f, buttonLine = 3f;
        return CoverageSprite(60, 84, p =>
        {
            // Back to reference pixels, centred, y up.
            float u = p.x * width * 0.5f, v = p.y * height * 0.5f;
            float d = RoundedBox(u, v, width * 0.5f, height * 0.5f, radius);
            if (rightButton)
                return d < -2.6f && u > 1.6f && v > buttonLine + 1.4f;
            bool rim = d <= 0f && d >= -1.7f;
            bool split = d < 0f && Mathf.Abs(u) < 0.85f && v > buttonLine;
            bool line = d < 0f && Mathf.Abs(v - buttonLine) < 0.85f;
            return rim || split || line;
        });
    }

    private static float RoundedBox(float u, float v, float halfWidth, float halfHeight, float radius)
    {
        float qx = Mathf.Abs(u) - (halfWidth - radius);
        float qy = Mathf.Abs(v) - (halfHeight - radius);
        return new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude + Mathf.Min(Mathf.Max(qx, qy), 0f) - radius;
    }

    private static System.Func<Vector2, bool> Shape(AbilityId id)
    {
        switch (id)
        {
            case AbilityId.Light:
                // A light bulb: a round glass narrowing into a neck, over a two-ring screw base.
                return p =>
                {
                    bool glass = (p - new Vector2(0f, 0.25f)).magnitude < 0.52f;
                    bool neck = p.y > -0.42f && p.y <= 0.0f && Mathf.Abs(p.x) < 0.24f + (p.y + 0.42f) * 0.45f;
                    bool baseRing = Mathf.Abs(p.x) < 0.24f && ((p.y > -0.6f && p.y < -0.48f) || (p.y > -0.78f && p.y < -0.66f));
                    bool tip = Mathf.Abs(p.x) < 0.12f && p.y > -0.9f && p.y <= -0.78f;
                    return glass || neck || baseRing || tip;
                };
            case AbilityId.SonarBlast:
                // A source dot with two wave fronts spreading right.
                return p =>
                {
                    Vector2 q = p - new Vector2(-0.6f, 0f);
                    float r = q.magnitude;
                    bool ahead = Mathf.Abs(Mathf.Atan2(q.y, q.x)) < 0.95f;
                    return r < 0.2f || (ahead && (Mathf.Abs(r - 0.68f) < 0.11f || Mathf.Abs(r - 1.18f) < 0.11f));
                };
            case AbilityId.Camo:
                // An eye with a slashed-out pupil: hidden from view.
                return p =>
                {
                    float lid = 0.5f * (1f - p.x * p.x);
                    bool eye = Mathf.Abs(p.y) < lid && Mathf.Abs(p.y) > lid - 0.16f;
                    bool pupil = p.magnitude < 0.22f;
                    bool slash = Mathf.Abs(p.y - p.x * 0.9f) < 0.1f && Mathf.Abs(p.x) < 0.75f;
                    return eye || pupil || slash;
                };
            case AbilityId.Scanner:
                // Corner brackets around a centre dot: a scanner picking out targets.
                return p =>
                {
                    float ax = Mathf.Abs(p.x), ay = Mathf.Abs(p.y);
                    bool corner = ax > 0.4f && ay > 0.4f && ax < 0.85f && ay < 0.85f && (ax > 0.7f || ay > 0.7f);
                    return corner || p.magnitude < 0.18f;
                };
            case AbilityId.Boost:
                // A double chevron pointing right.
                return p =>
                {
                    // Each chevron's tip is furthest right at the middle and sweeps back toward the top and bottom.
                    float y = Mathf.Abs(p.y);
                    bool first = p.x > -0.5f - y && p.x < -0.1f - y && y < 0.75f;
                    bool second = p.x > 0.25f - y && p.x < 0.65f - y && y < 0.75f;
                    return first || second;
                };
            default:
                return p => false;
        }
    }
}
