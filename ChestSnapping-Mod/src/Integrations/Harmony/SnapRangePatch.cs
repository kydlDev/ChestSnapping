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

        // A bigger range helps big chests find their snap point, but on a small piece (a 0.8 m pot) 1 m reaches the snap points of the
        // neighbour's neighbour, so the ghost jumps to the wrong one: the range never exceeds 0.6 times the piece's longest side.
        float range = Mathf.Max(maxSnapDistance, Mathf.Min(ModSettings.SnapRange.Value, SnapSize.Of(ghost.gameObject) * 0.6f));
        if (range <= maxSnapDistance) return;

        ModLog.Verbose("SnapRangePatch: " + ChestSnapPoints.GetPrefabName(ghost.gameObject) + " snap range " + maxSnapDistance + " -> " + range);
        maxSnapDistance = range;
    }
}

/// <summary>Longest horizontal side (m) of a chest prefab's box, measured once per prefab name.</summary>
internal static class SnapSize
{
    private static readonly System.Collections.Generic.Dictionary<string, float> Cache = new System.Collections.Generic.Dictionary<string, float>();

    internal static float Of(GameObject go)
    {
        string name = ChestSnapPoints.GetPrefabName(go);
        float size;
        if (Cache.TryGetValue(name, out size)) return size;

        Bounds bounds;
        size = ChestSnapPoints.TryGetLocalBounds(go.transform, out bounds) ? Mathf.Max(bounds.size.x, bounds.size.z) : float.MaxValue;
        Cache[name] = size;
        return size;
    }
}
