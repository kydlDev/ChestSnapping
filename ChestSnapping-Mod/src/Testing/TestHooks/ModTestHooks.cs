using System;
using System.Reflection;
using ChestSnapping.Bootstrap;
using HarmonyLib;
using KydlCore.Common;
using UnityEngine;

namespace ChestSnapping.Testing;

/// <summary>KydlCore D-084: hooks for DrakesTestLab (tests/TestLab/suites/ChestSnapping). They read the real prefabs of the loaded world.</summary>
internal static class ModTestHooks
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

    private static GameObject _stacked;
    private static readonly System.Collections.Generic.List<GameObject> Placed = new System.Collections.Generic.List<GameObject>();

    /// <summary>
    /// Places a REAL chest of prefab <paramref name="prefab"/> on top of the <paramref name="index"/>-th nearest placed chest <paramref name="onPrefab"/> (0 = nearest),
    /// the way Player.PlacePiece does (Instantiate with a ZNetView), so the game's building support runs on it. Returns "placed" or the reason it was not.
    /// </summary>
    [TestHook("chestsnapping.stackNew")]
    private static string StackNew(string prefab, string onPrefab, int index, int onIndex = -1)
    {
        Player player = Player.m_localPlayer;
        if (player == null) throw new Exception("no local player");

        Piece lower;
        if (onIndex >= 0)
        {
            if (onIndex >= Placed.Count || Placed[onIndex] == null) return "placed chest #" + onIndex + " does not exist (placed " + Placed.Count + ")";
            lower = Placed[onIndex].GetComponent<Piece>();
        }
        else
        {
            var lowers = new System.Collections.Generic.List<Piece>();
            foreach (Piece piece in UnityEngine.Object.FindObjectsByType<Piece>(FindObjectsSortMode.None))
                if (piece.GetComponent<ZNetView>() != null && ChestSnapPoints.GetPrefabName(piece.gameObject) == onPrefab) lowers.Add(piece);
            lowers.Sort((a, b) => Vector3.Distance(a.transform.position, player.transform.position).CompareTo(Vector3.Distance(b.transform.position, player.transform.position)));
            if (index < 0 || index >= lowers.Count) return "no placed " + onPrefab + " with index " + index + " (found " + lowers.Count + ")";
            lower = lowers[index];
        }

        Bounds lowerBounds, newBounds;
        if (!ChestSnapPoints.TryGetLocalBounds(lower.transform, out lowerBounds)) return "no bounds on the lower chest";
        GameObject source = Prefab(prefab);
        if (!ChestSnapPoints.TryGetLocalBounds(source.transform, out newBounds)) return "no bounds on " + prefab;

        Vector3 top = lower.transform.TransformPoint(new Vector3(lowerBounds.center.x, lowerBounds.max.y, lowerBounds.center.z));
        Vector3 pos = top + Vector3.up * (-newBounds.min.y);
        pos.x = top.x; pos.z = top.z;
        PlaceReal(source, pos, ChestSnapPoints.IsChest(lower.gameObject) ? lower.transform.rotation : Quaternion.identity);
        ModLog.Verbose("ModTestHooks: stackNew " + prefab + " on " + onPrefab + " #" + index + " at " + pos);
        return "placed";
    }

    /// <summary>Does the chest placed by stackNew still exist (the game destroys a piece without support)?</summary>
    [TestHook("chestsnapping.stackedExists")]
    private static bool StackedExists() { return _stacked != null; }

    /// <summary>Does the stacked chest have building support (WearNTear.HaveSupport)? False when it is gone.</summary>
    [TestHook("chestsnapping.stackedSupported")]
    private static bool StackedSupported()
    {
        if (_stacked == null) return false;
        WearNTear wear = _stacked.GetComponent<WearNTear>();
        return wear != null && Traverse.Create(wear).Method("HaveSupport").GetValue<bool>();
    }

    /// <summary>
    /// Diagnostic for edge / face contact between two chests: a ghost of <paramref name="prefab"/> is put where the vanilla snap would put it
    /// (its snap point <paramref name="ghostPoint"/> on the snap point <paramref name="targetPoint"/> of the nearest placed <paramref name="targetPrefab"/>, same rotation)
    /// and the game's own clipping rule is applied (Player.TestGhostClipping with 0.2 m). Returns "clip=False|True worst=metres with=pairs".
    /// </summary>
    [TestHook("chestsnapping.edgeClip")]
    private static string EdgeClip(string prefab, string targetPrefab, int ghostPoint, int targetPoint)
    {
        Player player = Player.m_localPlayer;
        if (player == null) throw new Exception("no local player");

        Piece target = null;
        float best = float.MaxValue;
        foreach (Piece piece in UnityEngine.Object.FindObjectsByType<Piece>(FindObjectsSortMode.None))
        {
            if (piece.GetComponent<ZNetView>() == null || ChestSnapPoints.GetPrefabName(piece.gameObject) != targetPrefab) continue;
            float d = Vector3.Distance(piece.transform.position, player.transform.position);
            if (d < best) { target = piece; best = d; }
        }

        if (target == null) return "no placed " + targetPrefab;
        Transform targetPointTransform = FindPoint(target.transform, targetPoint);
        if (targetPointTransform == null) return "target has no snap point " + targetPoint;

        GameObject ghost;
        ZNetView.m_forceDisableInit = true;
        try { ghost = UnityEngine.Object.Instantiate(Prefab(prefab), target.transform.position + Vector3.up * 30f, target.transform.rotation); }
        finally { ZNetView.m_forceDisableInit = false; }

        try
        {
            int ghostLayer = LayerMask.NameToLayer("ghost");
            foreach (Transform t in ghost.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = ghostLayer;

            Transform ghostPointTransform = FindPoint(ghost.transform, ghostPoint);
            if (ghostPointTransform == null) return "ghost has no snap point " + ghostPoint;

            ghost.transform.position += targetPointTransform.position - ghostPointTransform.position;
            Physics.SyncTransforms();

            int mask = Traverse.Create(player).Field("m_placeRayMask").GetValue<int>();
            Collider[] others = Physics.OverlapSphere(ghost.transform.position, 10f, mask);
            float worst = 0f;
            var pairs = new System.Text.StringBuilder();
            foreach (Collider mine in ghost.GetComponentsInChildren<Collider>())
            {
                foreach (Collider other in others)
                {
                    Vector3 dir;
                    float distance;
                    if (!Physics.ComputePenetration(mine, mine.transform.position, mine.transform.rotation, other, other.transform.position, other.transform.rotation, out dir, out distance)) continue;
                    worst = Mathf.Max(worst, distance);
                    pairs.Append(mine.GetType().Name).Append(mine.isTrigger ? "(trigger)" : "").Append(':').Append(mine.name)
                        .Append(" x ").Append(other.GetType().Name).Append(other.isTrigger ? "(trigger)" : "").Append(':').Append(other.name)
                        .Append(" d=").Append(distance.ToString("0.000")).Append("; ");
                }
            }

            string r = "clip=" + (worst > 0.2f) + " worst=" + worst.ToString("0.000") + " with=" + pairs;
            ModLog.Verbose("ModTestHooks: edgeClip " + prefab + "#" + ghostPoint + " on " + targetPrefab + "#" + targetPoint + " -> " + r);
            return r;
        }
        finally
        {
            UnityEngine.Object.Destroy(ghost);
        }
    }

    private static Transform FindPoint(Transform root, int index)
    {
        string name = SnapLayout.Points[index].ObjectName;
        for (int i = 0; i < root.childCount; i++)
            if (root.GetChild(i).name == name) return root.GetChild(i);
        return null;
    }

    /// <summary>
    /// The vanilla placement pipeline with a forced snap point, the way a player does it: the camera is pointed at snap point <paramref name="targetPoint"/>
    /// of the nearest placed <paramref name="onPrefab"/>, the ghost of <paramref name="prefab"/> gets snap point <paramref name="ghostPoint"/> selected (Q / E)
    /// (rotation = the player's rotate steps of 22.5 degrees) and Player.UpdatePlacementGhost runs (with every patch). Returns "status=.. gap=metres clip=metres pairs=..": the placement status, how far the two
    /// snap points ended up from each other, and the deepest overlap the game's clipping rule sees (with what).
    /// </summary>
    [TestHook("chestsnapping.snapPlacement")]
    private static string SnapPlacement(string prefab, string onPrefab, int ghostPoint, int targetPoint, int rotation, int onIndex = -1, bool place = false)
    {
        Player player = Player.m_localPlayer;
        if (player == null) throw new Exception("no local player");
        if (GameCamera.instance == null) throw new Exception("no GameCamera");

        Piece target = null;
        float best = float.MaxValue;
        if (onIndex >= 0)
        {
            if (onIndex < Placed.Count && Placed[onIndex] != null) target = Placed[onIndex].GetComponent<Piece>();
        }
        else
        {
            foreach (Piece piece in UnityEngine.Object.FindObjectsByType<Piece>(FindObjectsSortMode.None))
            {
                if (piece.GetComponent<ZNetView>() == null || ChestSnapPoints.GetPrefabName(piece.gameObject) != onPrefab) continue;
                float d = Vector3.Distance(piece.transform.position, player.transform.position);
                if (d < best) { target = piece; best = d; }
            }
        }

        if (target == null) return "no placed " + onPrefab + (onIndex >= 0 ? " #" + onIndex : "");
        Transform targetPointTransform = FindPoint(target.transform, targetPoint);
        if (targetPointTransform == null) return "target has no snap point " + targetPoint;

        Vector3 aimAt = targetPointTransform.position;
        Vector3 outward = aimAt - target.transform.position;   // look at the point from outside the chest, so the ray never crosses the chest first
        outward.y = 0f;
        if (outward.sqrMagnitude < 0.0001f) outward = player.transform.position - aimAt;
        Vector3 from = aimAt + outward.normalized * 1.5f + Vector3.up * 1.2f;
        aimAt += -outward.normalized * 0.12f + (aimAt.y < target.transform.position.y + 0.3f ? Vector3.up * 0.15f : Vector3.zero);   // a little inside the chest, so the ray lands on the chest and not on the floor next to a bottom corner
        Transform cam = GameCamera.instance.transform;
        Vector3 camPos = cam.position;
        Quaternion camRot = cam.rotation;
        cam.position = from;
        cam.rotation = Quaternion.LookRotation(aimAt - from);

        GameObject ghost;
        ZNetView.m_forceDisableInit = true;
        try { ghost = UnityEngine.Object.Instantiate(Prefab(prefab)); }
        finally { ZNetView.m_forceDisableInit = false; }

        int ghostLayer = LayerMask.NameToLayer("ghost");
        foreach (Transform tr in ghost.GetComponentsInChildren<Transform>(true)) tr.gameObject.layer = ghostLayer;

        Traverse p = Traverse.Create(player);
        GameObject previous = p.Field("m_placementGhost").GetValue<GameObject>();
        int previousSnap = p.Field("m_manualSnapPoint").GetValue<int>();
        int previousRotation = p.Field("m_placeRotation").GetValue<int>();
        try
        {
            p.Field("m_placementGhost").SetValue(ghost);
            p.Field("m_manualSnapPoint").SetValue(ghostPoint);
            p.Field("m_placeRotation").SetValue(rotation);
            p.Method("UpdatePlacementGhost", new[] { typeof(bool) }, new object[] { false }).GetValue();
            Physics.SyncTransforms();

            string status = p.Field("m_placementStatus").GetValue().ToString();
            Transform ghostPointTransform = FindPoint(ghost.transform, ghostPoint);
            float gap = ghostPointTransform == null ? -1f : Vector3.Distance(ghostPointTransform.position, targetPointTransform.position);

            int mask = p.Field("m_placeRayMask").GetValue<int>();
            float worst = 0f;
            var pairs = new System.Text.StringBuilder();
            foreach (Collider mine in ghost.GetComponentsInChildren<Collider>())
            {
                foreach (Collider other in Physics.OverlapSphere(ghost.transform.position, 10f, mask))
                {
                    Vector3 dir;
                    float distance;
                    if (!Physics.ComputePenetration(mine, mine.transform.position, mine.transform.rotation, other, other.transform.position, other.transform.rotation, out dir, out distance)) continue;
                    worst = Mathf.Max(worst, distance);
                    pairs.Append(mine.name).Append(" x ").Append(other.name).Append("@").Append(other.GetComponentInParent<Piece>() != null ? other.GetComponentInParent<Piece>().name : "-")
                        .Append(" d=").Append(distance.ToString("0.000")).Append("; ");
                }
            }

            string r = "status=" + status + " gap=" + gap.ToString("0.000") + " clip=" + worst.ToString("0.000") + " pairs=" + pairs
                + " | cam " + from.ToString("0.00") + " aim " + aimAt.ToString("0.00") + " | ghost pos " + ghost.transform.position.ToString("0.000") + " yaw " + ghost.transform.eulerAngles.y.ToString("0.0")
                + " point " + (ghostPointTransform == null ? "-" : ghostPointTransform.position.ToString("0.000"))
                + " | target pos " + target.transform.position.ToString("0.000") + " yaw " + target.transform.eulerAngles.y.ToString("0.0")
                + " point " + targetPointTransform.position.ToString("0.000");
            if (place && status == "Valid")
            {
                PlaceReal(Prefab(prefab), ghost.transform.position, ghost.transform.rotation);
                r += " | PLACED as #" + (Placed.Count - 1);
            }

            ModLog.Verbose("ModTestHooks: snapPlacement " + prefab + "#" + ghostPoint + " on " + onPrefab + "#" + targetPoint + " -> " + r);
            return r;
        }
        finally
        {
            cam.position = camPos;
            cam.rotation = camRot;
            p.Field("m_placementGhost").SetValue(previous);
            p.Field("m_manualSnapPoint").SetValue(previousSnap);
            p.Field("m_placeRotation").SetValue(previousRotation);
            GameObject marker = p.Field("m_placementMarkerInstance").GetValue<GameObject>();
            if (marker != null) marker.SetActive(false);
            UnityEngine.Object.Destroy(ghost);
        }
    }

    /// <summary>Places a real piece like Player.PlacePiece (Instantiate with a ZNetView) and skips the game's 30 s support grace (WearNTear.ShouldUpdate); keeps it in <see cref="Placed"/>.</summary>
    private static GameObject PlaceReal(GameObject source, Vector3 pos, Quaternion rot)
    {
        GameObject go = UnityEngine.Object.Instantiate(source, pos, rot);
        WearNTear wear = go.GetComponent<WearNTear>();
        if (wear != null) Traverse.Create(wear).Field("m_createTime").SetValue(-1f);
        Placed.Add(go);
        _stacked = go;
        return go;
    }

    /// <summary>Counts the chests placed by the hooks: how many were placed, still exist and have building support. Example "6 placed, 6 alive, 6 supported".</summary>
    [TestHook("chestsnapping.placedStatus")]
    private static string PlacedStatus()
    {
        int alive = 0, supported = 0;
        foreach (GameObject go in Placed)
        {
            if (go == null) continue;
            alive++;
            WearNTear wear = go.GetComponent<WearNTear>();
            if (wear != null && Traverse.Create(wear).Method("HaveSupport").GetValue<bool>()) supported++;
        }

        string r = Placed.Count + " placed, " + alive + " alive, " + supported + " supported";
        ModLog.Verbose("ModTestHooks: placedStatus " + r);
        return r;
    }

    /// <summary>Removes every chest placed by the hooks. Returns how many were removed.</summary>
    [TestHook("chestsnapping.placedClear")]
    private static int PlacedClear()
    {
        int n = 0;
        foreach (GameObject go in Placed)
        {
            if (go == null) continue;
            ZNetScene.instance.Destroy(go);
            n++;
        }

        Placed.Clear();
        _stacked = null;
        return n;
    }

    /// <summary>Clears trees, bushes, pickables and rocks and levels the ground around the player with DrakesTestLab's own arena builder (the lab's "Flatten here only"). Returns "started"; wait until <c>chestsnapping.areaBusy</c> is false.</summary>
    [TestHook("chestsnapping.clearArea")]
    private static string ClearArea(float radius)
    {
        Player player = Player.m_localPlayer;
        if (player == null) return "no player";
        Type builder = Type.GetType("DrakesTestLab.Bootstrap.ArenaBuilder, DrakesTestLab");
        if (builder == null) return "DrakesTestLab ArenaBuilder not found";
        MethodInfo build = builder.GetMethod("BuildAt", BindingFlags.Public | BindingFlags.Static);
        if (build == null) return "ArenaBuilder.BuildAt not found";
        var routine = (System.Collections.IEnumerator)build.Invoke(null, new object[] { player.transform.position, false, false, radius, null, 0f });
        player.StartCoroutine(routine);
        ModLog.Verbose("ModTestHooks: clearArea radius " + radius + " around " + player.transform.position);
        return "started";
    }

    /// <summary>True while the lab's arena builder is still clearing and levelling.</summary>
    [TestHook("chestsnapping.areaBusy")]
    private static bool AreaBusy()
    {
        Type builder = Type.GetType("DrakesTestLab.Bootstrap.ArenaBuilder, DrakesTestLab");
        PropertyInfo busy = builder?.GetProperty("Busy", BindingFlags.Public | BindingFlags.Static);
        return busy != null && (bool)busy.GetValue(null, null);
    }

    /// <summary>Collider bounds vs. visible mesh (renderer) bounds of a chest prefab, in its local space: shows whether the snap box (colliders) is smaller than what you see.</summary>
    [TestHook("chestsnapping.boundsInfo")]
    private static string BoundsInfo(string prefab)
    {
        if (prefab.Contains(","))
        {
            string all = "";
            foreach (string one in prefab.Split(',')) all += BoundsInfo(one) + " || ";
            return all;
        }

        GameObject go = Prefab(prefab);
        Bounds col;
        string c = ChestSnapPoints.TryGetLocalBounds(go.transform, out col) ? col.center.ToString("0.000") + " size " + col.size.ToString("0.000") : "none";
        Bounds mesh = default(Bounds);
        bool any = false;
        foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
        {
            MeshFilter mf = r.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null || r.GetComponent<ParticleSystemRenderer>() != null) continue;
            Matrix4x4 m = go.transform.worldToLocalMatrix * mf.transform.localToWorldMatrix;
            Bounds b = mf.sharedMesh.bounds;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                Vector3 p = m.MultiplyPoint3x4(corner);
                if (!any) { mesh = new Bounds(p, Vector3.zero); any = true; } else mesh.Encapsulate(p);
            }
        }

        string parts = "";
        foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
        {
            MeshFilter mf = r.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) continue;
            Matrix4x4 m = go.transform.worldToLocalMatrix * mf.transform.localToWorldMatrix;
            Bounds b = mf.sharedMesh.bounds;
            Vector3 lo = m.MultiplyPoint3x4(b.min), hi = m.MultiplyPoint3x4(b.max);
            Vector3 mn = Vector3.Min(lo, hi), mx = Vector3.Max(lo, hi);
            parts += " [" + r.name + (r.enabled && r.gameObject.activeSelf ? "" : " OFF") + " " + mn.ToString("0.00") + ".." + mx.ToString("0.00") + "]";
        }

        return prefab + parts + " :: colliders center " + c + " | meshes center " + (any ? mesh.center.ToString("0.000") + " size " + mesh.size.ToString("0.000") : "none");
    }

    /// <summary>Largest overlap (m) between the VISIBLE closed meshes of the chests placed by the hooks (world boxes, so only exact for quarter turns). A chest inside another one shows as a big number. Example "max overlap 0.012 (#1 x #2)".</summary>
    [TestHook("chestsnapping.placedVisibleOverlap")]
    private static string PlacedVisibleOverlap()
    {
        var boxes = new System.Collections.Generic.List<Bounds>();
        foreach (GameObject go in Placed)
        {
            Bounds b = default(Bounds);
            bool any = false;
            if (go != null)
                foreach (MeshRenderer mr in go.GetComponentsInChildren<MeshRenderer>(false))
                {
                    if (!mr.enabled || mr.name.IndexOf("open", StringComparison.OrdinalIgnoreCase) >= 0 || mr.name.EndsWith("LOD")) continue;
                    if (!any) { b = mr.bounds; any = true; } else b.Encapsulate(mr.bounds);
                }

            boxes.Add(b);
        }

        float worst = 0f;
        string pair = "-";
        for (int i = 0; i < boxes.Count; i++)
            for (int j = i + 1; j < boxes.Count; j++)
            {
                Vector3 a = boxes[i].size, bsz = boxes[j].size;
                if (a == Vector3.zero || bsz == Vector3.zero) continue;
                Vector3 d = boxes[i].center - boxes[j].center;
                float ox = (a.x + bsz.x) * 0.5f - Mathf.Abs(d.x);
                float oy = (a.y + bsz.y) * 0.5f - Mathf.Abs(d.y);
                float oz = (a.z + bsz.z) * 0.5f - Mathf.Abs(d.z);
                if (ox <= 0f || oy <= 0f || oz <= 0f) continue;
                float depth = Mathf.Min(ox, Mathf.Min(oy, oz));
                if (depth > worst) { worst = depth; pair = "#" + i + " x #" + j; }
            }

        string r = "max overlap " + worst.ToString("0.000") + " (" + pair + ")";
        ModLog.Verbose("ModTestHooks: placedVisibleOverlap " + r);
        return r;
    }

    /// <summary>Puts every placed piece of a prefab within 25 m of the player at one common height: the highest of them plus <paramref name="above"/> metres (test floors that must be level and clear of terrain that is still settling). Returns "levelled N".</summary>
    [TestHook("chestsnapping.liftPieces")]
    private static string LiftPieces(string prefab, float above)
    {
        var pieces = new System.Collections.Generic.List<Piece>();
        float top = float.MinValue;
        foreach (Piece piece in UnityEngine.Object.FindObjectsByType<Piece>(FindObjectsSortMode.None))
        {
            if (piece.GetComponent<ZNetView>() == null || ChestSnapPoints.GetPrefabName(piece.gameObject) != prefab) continue;
            if (Player.m_localPlayer != null && Vector3.Distance(piece.transform.position, Player.m_localPlayer.transform.position) > 25f) continue;
            pieces.Add(piece);
            top = Mathf.Max(top, piece.transform.position.y);
        }

        foreach (Piece piece in pieces)
        {
            Vector3 pos = piece.transform.position;
            pos.y = top + above;
            piece.transform.position = pos;
        }

        Physics.SyncTransforms();
        return "levelled " + pieces.Count;
    }
}
