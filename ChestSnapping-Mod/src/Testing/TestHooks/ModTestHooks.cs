using System;
using System.Reflection;
using ChestSnapping.Bootstrap;
using HarmonyLib;
using KydlCore.Common;
using UnityEngine;

namespace ChestSnapping.Testing;

/// <summary>KydlCore D-084: hooks for DrakesTestLab (tests/TestLab/suites/ChestSnapping). They read the real prefabs of the loaded world.</summary>
internal static partial class ModTestHooks
{
    private static GameObject Prefab(string name)
    {
        GameObject p = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(name) : null;
        if (p == null) throw new Exception("prefab " + name + " not found (no world loaded?)");
        return p;
    }

    /// <summary>Every test starts here (MIGRATION_DECISIONS C26): the player on the open arena spot 70 m from the lab start, facing +z, god mode on.</summary>
    [TestHook("chestsnapping.arena")]
    private static string Arena() { return TestArena.MovePlayer(); }

    /// <summary>Is the feature running (FeatureRegistry state after its Enable switches)? ids: snapping, adjustments.</summary>
    [TestHook("chestsnapping.featureRunning")]
    private static bool FeatureRunning(string id) { return ModInstaller.Current != null && ModInstaller.Current.Features.IsRunning(id); }

    /// <summary>Is this tab (settings) registered in the KY hub right now?</summary>
    [TestHook("chestsnapping.tabRegistered")]
    private static bool TabRegistered(string id) { return KydlCore.Features.Hub.Contracts.KyHub.HasTab(Plugin.ModGuid, id); }

    [TestHook("chestsnapping.selftest")]
    private static string SelfTest() { return TestLabSupport.RunInGameTests(typeof(InGameTests)); }

    /// <summary>"default", "extra", "api", "auto" or "none": does this prefab get snap points under the current config?</summary>
    [TestHook("chestsnapping.source")]
    private static string Source(string prefab) { return ChestSnapPoints.Source(Prefab(prefab)) ?? "none"; }

    /// <summary>
    /// Builds a placement ghost of the prefab (what the player holds while building; Piece.Awake runs) and counts its snap points.
    /// The ghost is destroyed again; nothing is placed in the world.
    /// </summary>
    [TestHook("chestsnapping.ghostPoints")]
    private static int GhostPoints(string prefab)
    {
        GameObject source = Prefab(prefab);
        GameObject ghost;
        ZNetView.m_forceDisableInit = true;
        try { ghost = UnityEngine.Object.Instantiate(source); }
        finally { ZNetView.m_forceDisableInit = false; }
        try { return ChestSnapPoints.CountPoints(ghost.transform); }
        finally { UnityEngine.Object.Destroy(ghost); }
    }

    /// <summary>
    /// How far the saved adjustment moves the first snap point on Y (metres, rounded to 3 decimals): applied position minus the
    /// plain measured one. 0 when there is no adjustment or [Offsets] Enable is off.
    /// </summary>
    [TestHook("chestsnapping.adjustDeltaY")]
    private static float AdjustDeltaY(string prefab)
    {
        GameObject p = Prefab(prefab);
        Bounds bounds;
        if (!ChestSnapPoints.TryGetLocalBounds(p.transform, out bounds)) throw new Exception("no bounds on " + prefab);
        Vector3[] plain = ChestSnapPoints.ComputePositions(bounds, null);
        Vector3[] applied = ChestSnapPoints.ComputePositions(bounds, SnapAdjustments.Active(prefab));
        return (float)Math.Round(applied[0].y - plain[0].y, 3);
    }

    /// <summary>
    /// "override" when StackingPatch would count the aimed-at piece as a support for a placement test of the ghost piece (chest on chest), else "none".
    /// Uses the real prefabs and the real decision code; nothing is placed.
    /// </summary>
    [TestHook("chestsnapping.stackDecision")]
    private static string StackDecision(string ghost, string hit)
    {
        WearNTear wear = StackingPatch.Decide(Prefab(ghost), Prefab(hit).GetComponent<Piece>());
        string r = wear != null ? "override" : "none";
        ModLog.Verbose("ModTestHooks: stackDecision " + ghost + " on " + hit + " -> " + r);
        return r;
    }

    /// <summary>WearNTear.m_supports of the prefab (a chest must never stay a support after the patch ran).</summary>
    [TestHook("chestsnapping.supports")]
    private static bool Supports(string prefab)
    {
        WearNTear wear = Prefab(prefab).GetComponent<WearNTear>();
        if (wear == null) throw new Exception("no WearNTear on " + prefab);
        return wear.m_supports;
    }

    /// <summary>Calls the public API like another mod would: ChestSnappingApi.RegisterChest. Returns the API's answer.</summary>
    [TestHook("chestsnapping.apiRegister")]
    private static bool ApiRegister(string prefab) { return ChestSnappingApi.RegisterChest(prefab); }

    /// <summary>Calls ChestSnappingApi.UnregisterChest.</summary>
    [TestHook("chestsnapping.apiUnregister")]
    private static bool ApiUnregister(string prefab) { return ChestSnappingApi.UnregisterChest(prefab); }

    /// <summary>
    /// The REAL placement test: puts a ghost of the prefab into the local player's hands, runs the game's own Player.UpdatePlacementGhost
    /// at wherever the camera looks (so StackingPatch runs for real) and returns the resulting Player.PlacementStatus ("Valid", "Invalid", ...).
    /// The camera is pointed at the top of the nearest placed piece of prefab <paramref name="aim"/> for this one call (the game puts it back next frame);
    /// the player's own ghost and the placement marker are put back; nothing is placed.
    /// </summary>
    [TestHook("chestsnapping.placementStatus")]
    private static string PlacementStatus(string prefab, string aim)
    {
        Player player = Player.m_localPlayer;
        if (player == null) throw new Exception("no local player (no world loaded?)");

        GameObject source = Prefab(prefab);
        GameObject ghost;
        ZNetView.m_forceDisableInit = true;
        try { ghost = UnityEngine.Object.Instantiate(source); }
        finally { ZNetView.m_forceDisableInit = false; }

        Transform cam = AimCameraAtPiece(player, aim);
        Vector3 camPos = cam.position;
        Quaternion camRot = cam.rotation;

        int ghostLayer = LayerMask.NameToLayer("ghost");
        foreach (Transform t in ghost.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = ghostLayer;

        Traverse player_ = Traverse.Create(player);
        GameObject previous = player_.Field("m_placementGhost").GetValue<GameObject>();
        try
        {
            player_.Field("m_placementGhost").SetValue(ghost);
            player_.Method("UpdatePlacementGhost", new[] { typeof(bool) }, new object[] { false }).GetValue();
            string status = player_.Field("m_placementStatus").GetValue().ToString();
            ModLog.Verbose("ModTestHooks: placementStatus " + prefab + " -> " + status);
            return status;
        }
        finally
        {
            cam.position = camPos;
            cam.rotation = camRot;
            player_.Field("m_placementGhost").SetValue(previous);
            GameObject marker = player_.Field("m_placementMarkerInstance").GetValue<GameObject>();
            if (marker != null) marker.SetActive(false);
            UnityEngine.Object.Destroy(ghost);
        }
    }

    /// <summary>Moves the game camera 1.5 m in front of and 1.2 m above the top of the nearest placed piece named <paramref name="aim"/> and looks at that top.</summary>
    private static Transform AimCameraAtPiece(Player player, string aim)
    {
        if (GameCamera.instance == null) throw new Exception("no GameCamera");

        Piece best = null;
        float bestDistance = float.MaxValue;
        foreach (Piece piece in UnityEngine.Object.FindObjectsByType<Piece>(FindObjectsSortMode.None))
        {
            if (piece.GetComponent<ZNetView>() == null || ChestSnapPoints.GetPrefabName(piece.gameObject) != aim) continue;
            float d = Vector3.Distance(piece.transform.position, player.transform.position);
            if (d < bestDistance) { best = piece; bestDistance = d; }
        }

        if (best == null) throw new Exception("no placed piece " + aim + " near the player");

        Bounds bounds;
        if (!ChestSnapPoints.TryGetLocalBounds(best.transform, out bounds)) throw new Exception("no bounds on " + aim);
        Vector3 top = best.transform.TransformPoint(new Vector3(bounds.center.x, bounds.max.y, bounds.center.z));
        Vector3 toPlayer = player.transform.position - top;
        toPlayer.y = 0f;
        Vector3 from = top + toPlayer.normalized * 1.5f + Vector3.up * 1.2f;

        Transform cam = GameCamera.instance.transform;
        cam.position = from;
        cam.rotation = Quaternion.LookRotation(top - from);
        ModLog.Verbose("ModTestHooks: camera aimed at the top of " + aim + " at " + top + " from " + from);
        return cam;
    }
}
