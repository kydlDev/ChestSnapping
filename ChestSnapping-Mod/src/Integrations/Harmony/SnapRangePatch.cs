using HarmonyLib;
using KydlCore.Common;
using UnityEngine;

namespace ChestSnapping;

/// <summary>
/// Player.FindClosestSnapPoints(ghost, maxSnapDistance, ...) is called with a fixed 0.5 m. For a chest ghost the distance becomes the
/// [Prefabs] SnapRange (default 1 m) so the snap point is found earlier. Other pieces keep 0.5.
/// </summary>
[HarmonyPatch(typeof(Player), "FindClosestSnapPoints")]
internal static class SnapRangePatch
{
    private static void Prefix(Transform ghost, ref float maxSnapDistance)
    {
        if (!ModSettings.PrefabsOn || ghost == null || !ChestSnapPoints.IsChest(ghost.gameObject)) return;

        float range = Mathf.Max(maxSnapDistance, ModSettings.SnapRange.Value);
        if (range == maxSnapDistance) return;

        ModLog.Verbose("SnapRangePatch: " + ChestSnapPoints.GetPrefabName(ghost.gameObject) + " snap range " + maxSnapDistance + " -> " + range);
        maxSnapDistance = range;
    }
}
