/// <summary>Every ability the robot can slot into its ability wheel.</summary>
public enum AbilityId
{
    Empty,
    Light,
    // Was SONAR; kept at the same value so saved wheels still match.
    Scanner,
    Camo,
    // Was SCAN; kept at the same value so saved wheels still match.
    SonarBlast,
    Boost,
}

/// <summary>
/// The one place ability names are spelled, so the wheel, its hub and the HUD badge always agree.
/// </summary>
public static class AbilityNames
{
    public static string Label(AbilityId id)
    {
        switch (id)
        {
            case AbilityId.Light: return "LIGHT";
            case AbilityId.Scanner: return "SCANNER";
            case AbilityId.Camo: return "CAMO";
            case AbilityId.SonarBlast: return "SONAR BLAST";
            case AbilityId.Boost: return "BOOST";
            default: return "---";
        }
    }
}

/// <summary>
/// An ability component on the Player. The ability wheel finds it by Id and calls Use when the player right-clicks
/// with that ability selected.
/// </summary>
public interface IRobotAbility
{
    AbilityId Id { get; }
    /// <summary>Activates (or for toggles, flips) the ability. Returns false when it could not be used, such as on cooldown.</summary>
    bool Use();
    /// <summary>A short state for the HUD badge, such as ON, OFF, READY or a cooldown countdown.</summary>
    string Status { get; }
    /// <summary>True while a toggled ability is on, or while a timed one is cooling down; the badge highlights it.</summary>
    bool IsActive { get; }
}
