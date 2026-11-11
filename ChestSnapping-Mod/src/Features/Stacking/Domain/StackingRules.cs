namespace ChestSnapping;

/// <summary>Pure stacking rule (no game types, unit-tested).</summary>
internal static class StackingRules
{
    /// <summary>
    /// Vanilla (Player.UpdatePlacementGhost) marks a placement Invalid when the piece you aim at has a WearNTear that does not
    /// support (WearNTear.m_supports false). Chests do not support, planks do - so chests only stack on planks. The aimed-at
    /// piece has to count as a support only for a chest that is placed on another chest.
    /// </summary>
    /// <param name="ghostIsChest">The piece being placed is a chest.</param>
    /// <param name="hitIsChest">The piece aimed at is a chest.</param>
    /// <param name="hitSupports">The aimed-at piece's WearNTear.m_supports.</param>
    /// <returns>True when the aimed-at chest has to be treated as a support for this placement test.</returns>
    internal static bool NeedsSupportOverride(bool ghostIsChest, bool hitIsChest, bool hitSupports)
    {
        bool result = ghostIsChest && hitIsChest && !hitSupports;
        return result;
    }
}
