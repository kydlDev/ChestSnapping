using System;

namespace ChestSnapping;

/// <summary>Pure rotation rule for chests that snap to another chest (no game types, unit-tested).</summary>
internal static class ChestAlignment
{
    private const float Quarter = 90f;

    /// <summary>
    /// The yaw a chest ghost gets when it snaps to a chest: the target's yaw plus a multiple of 90 degrees, whichever multiple is nearest to the ghost's
    /// own yaw. So a ghost that is a few degrees off lines up with the chest it snaps to, and a ghost the player turned a quarter / half turn keeps that turn.
    /// </summary>
    /// <param name="ghostYaw">The ghost's current yaw in degrees.</param>
    /// <param name="targetYaw">The placed chest's yaw in degrees.</param>
    /// <returns>The yaw to use, in 0..360.</returns>
    internal static float AlignedYaw(float ghostYaw, float targetYaw)
    {
        float turns = (float)Math.Round(Delta(ghostYaw, targetYaw) / Quarter, MidpointRounding.AwayFromZero);
        float yaw = targetYaw + turns * Quarter;
        yaw %= 360f;
        return yaw < 0f ? yaw + 360f : yaw;
    }

    /// <summary>The signed difference a - b in degrees, wrapped to -180..180.</summary>
    internal static float Delta(float a, float b)
    {
        float d = (a - b) % 360f;
        if (d > 180f) d -= 360f;
        if (d < -180f) d += 360f;
        return d;
    }
}
