using System;
using HarmonyLib;
using KydlCore.Common;
using UnityEngine;

namespace ChestSnapping;

/// <summary>
/// Lets a chest be placed on another chest. Vanilla refuses it only because the chest you aim at has WearNTear.m_supports = false
/// (see StackingRules). Prefix: when a chest ghost is aimed at a chest, that chest's m_supports is set to true for this one call;
/// Finalizer: it is set back (also after an exception), so no chest ever stays a structural support and building stability is untouched.
/// Everything else vanilla checks (clipping, wards, water, ...) still runs.
/// </summary>
[HarmonyPatch(typeof(Player), "UpdatePlacementGhost")]
internal static class StackingPatch
{
    private const float RayLength = 50f; // same as Player.PieceRayTest

    private static void Prefix(GameObject ___m_placementGhost, int ___m_placeRayMask, int ___m_placeWaterRayMask, out State __state)
    {
        __state = null;
        if (!ModSettings.StackingOn || ___m_placementGhost == null || GameCamera.instance == null) return;

        // Cheap name test first: the raycast only runs while a chest is in the hand.
        if (!ChestSnapPoints.IsChest(___m_placementGhost)) return;

        Piece ghostPiece = ___m_placementGhost.GetComponent<Piece>();
        if (ghostPiece == null) return;

        // Same mask choice as Player.UpdatePlacementGhost: a piece that may not be in water uses the water ray mask.
        int mask = (ghostPiece.m_waterPiece || ghostPiece.m_noInWater) ? ___m_placeWaterRayMask : ___m_placeRayMask;
        Transform cam = GameCamera.instance.transform;
        RaycastHit hit;
        if (!Physics.Raycast(cam.position, cam.forward, out hit, RayLength, mask))
        {
            ModLog.Verbose("StackingPatch: camera ray hit nothing (mask " + mask + ") - vanilla decides");
            return;
        }

        if (hit.collider == null || hit.collider.attachedRigidbody != null)
        {
            ModLog.Verbose("StackingPatch: ray hit " + (hit.collider == null ? "no collider" : hit.collider.name + " (has a rigidbody)") + " - vanilla decides");
            return;
        }

        Piece hitPiece = hit.collider.GetComponentInParent<Piece>();
        ModLog.Verbose("StackingPatch: ray hit " + hit.collider.name + ", piece " + (hitPiece == null ? "none" : hitPiece.name));
        WearNTear wear = Decide(___m_placementGhost, hitPiece);
        if (wear == null) return;

        __state = new State { Wear = wear, Ghost = ghostPiece, GhostNotOnTilting = ghostPiece.m_notOnTiltingSurface };
        wear.m_supports = true;
        // Aiming at the SIDE of a chest (side by side / front to back) gives a ray normal that is not "up": vanilla's m_notOnTiltingSurface
        // would call that a tilted surface. The snap moves the ghost onto the ground / floor anyway, so it is allowed for this call.
        ghostPiece.m_notOnTiltingSurface = false;
        ModLog.Verbose("StackingPatch: " + ChestSnapPoints.GetPrefabName(___m_placementGhost) + " aimed at chest "
            + ChestSnapPoints.GetPrefabName(wear.gameObject) + " - counted as a support, side faces allowed, for this placement test");
    }

    private static void Finalizer(State __state)
    {
        if (__state == null) return;
        __state.Wear.m_supports = false;
        if (__state.Ghost != null) __state.Ghost.m_notOnTiltingSurface = __state.GhostNotOnTilting;
    }

    /// <summary>What Prefix changed, so Finalizer can put it back.</summary>
    internal sealed class State
    {
        public WearNTear Wear;
        public Piece Ghost;
        public bool GhostNotOnTilting;
    }

    /// <summary>The WearNTear that has to count as a support for this placement test, or null (also used by the TestLab hook).</summary>
    /// <param name="ghost">The piece being placed.</param>
    /// <param name="hitPiece">The piece aimed at (may be null: ground, rock).</param>
    /// <returns>The aimed-at chest's WearNTear, or null when nothing has to change.</returns>
    internal static WearNTear Decide(GameObject ghost, Piece hitPiece)
    {
        if (!ModSettings.StackingOn || ghost == null || hitPiece == null) return null;

        WearNTear wear = hitPiece.GetComponent<WearNTear>();
        if (wear == null) return null;

        bool needed = StackingRules.NeedsSupportOverride(ChestSnapPoints.IsChest(ghost), ChestSnapPoints.IsChest(hitPiece.gameObject), wear.m_supports);
        return needed ? wear : null;
    }
}
