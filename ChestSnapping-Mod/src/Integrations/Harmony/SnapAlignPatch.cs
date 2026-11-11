using HarmonyLib;
using KydlCore.Common;
using UnityEngine;

namespace ChestSnapping;

/// <summary>
/// A chest that snaps to another chest takes over the other chest's rotation (plus the quarter turns the player chose). Without it the ghost is a few degrees off,
/// its bounding-box corners overlap the other chest by more than the 0.2 m vanilla allows (Player.TestGhostClipping) and the ghost turns red although the two
/// only touch along an edge. Runs right after Player.FindClosestSnapPoints, before UpdatePlacementGhost moves the ghost onto the snap point, so the position
/// is computed with the corrected rotation.
/// </summary>
[HarmonyPatch(typeof(Player), "FindClosestSnapPoints")]
internal static class SnapAlignPatch
{
    private static void Postfix(bool __result, Transform ghost, Transform b)
    {
        if (!__result || !ModSettings.PrefabsOn || !ModSettings.AlignRotation.Value || ghost == null || b == null) return;
        if (!ChestSnapPoints.IsChest(ghost.gameObject)) return;

        Piece target = b.GetComponentInParent<Piece>();
        if (target == null || !ChestSnapPoints.IsChest(target.gameObject)) return;

        float before = ghost.eulerAngles.y;
        float yaw = ChestAlignment.AlignedYaw(before, target.transform.eulerAngles.y);
        if (Mathf.Abs(ChestAlignment.Delta(yaw, before)) < 0.01f) return;

        ghost.rotation = Quaternion.Euler(0f, yaw, 0f);
        ModLog.Verbose("SnapAlignPatch: " + ChestSnapPoints.GetPrefabName(ghost.gameObject) + " snapped to " + ChestSnapPoints.GetPrefabName(target.gameObject)
            + ", yaw " + before.ToString("0.0") + " -> " + yaw.ToString("0.0"));
    }
}
