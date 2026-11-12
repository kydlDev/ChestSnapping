using System;
using System.Reflection;
using HarmonyLib;
using KydlCore.Common;
using UnityEngine;

namespace ChestSnapping.Testing;

/// <summary>Hooks that place chests for real (stack, side by side, rows) and measure the result. Part of <see cref="ModTestHooks"/>.</summary>
internal static partial class ModTestHooks
{
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

            var snappedTo = new System.Text.StringBuilder();
            if (ghostPointTransform != null)
            {
                var pts = new System.Collections.Generic.List<Transform>();
                foreach (Piece other in UnityEngine.Object.FindObjectsByType<Piece>(FindObjectsSortMode.None))
                {
                    if (other.gameObject == ghost || Vector3.Distance(other.transform.position, ghost.transform.position) > 6f) continue;
                    pts.Clear();
                    other.GetSnapPoints(pts);
                    for (int i = 0; i < pts.Count; i++)
                        if (Vector3.Distance(pts[i].position, ghostPointTransform.position) < 0.03f)
                            snappedTo.Append(other.name).Append("[").Append(i).Append("]y=").Append(pts[i].position.y.ToString("0.000")).Append(" ");
                }
            }

            var near = new System.Text.StringBuilder();
            if (ghostPointTransform != null)
            {
                var all = new System.Collections.Generic.List<Transform>();
                var ps = new System.Collections.Generic.List<Piece>();
                Piece.GetSnapPoints(ghost.transform.position, 10f, all, ps);
                all.Sort((x, y) => Vector3.Distance(x.position, ghostPointTransform.position).CompareTo(Vector3.Distance(y.position, ghostPointTransform.position)));
                for (int i = 0; i < Mathf.Min(3, all.Count); i++)
                    near.Append(all[i].GetComponentInParent<Piece>().name).Append(":").Append(all[i].name).Append(all[i].position.ToString("0.000")).Append(" d=").Append(Vector3.Distance(all[i].position, ghostPointTransform.position).ToString("0.000")).Append(" ; ");
            }

            string r = "nearest=" + near + " snappedTo=" + snappedTo + " status=" + status + " gap=" + gap.ToString("0.000") + " clip=" + worst.ToString("0.000") + " pairs=" + pairs
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
            if (wear == null || Traverse.Create(wear).Method("HaveSupport").GetValue<bool>()) supported++;   // no WearNTear: the support check cannot destroy it
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

    /// <summary>Every buildable container prefab of the game with the snap-point source and the measured box (colliders+visible meshes): "name=source size". Used to see which storage pieces ChestSnapping knows.</summary>
    [TestHook("chestsnapping.containerList")]
    private static string ContainerList()
    {
        var rows = new System.Collections.Generic.List<string>();
        foreach (GameObject prefab in ZNetScene.instance.m_prefabs)
        {
            if (prefab == null || prefab.GetComponent<Piece>() == null || prefab.GetComponent<Container>() == null) continue;
            Bounds b;
            string size = ChestSnapPoints.TryGetLocalBounds(prefab.transform, out b) ? b.size.ToString("0.00") : "no box";
            rows.Add(prefab.name + "=" + (ChestSnapPoints.Source(prefab) ?? "none") + " " + size + (ChestSnapPoints.IsPlainContainer(prefab) ? "" : " [ship/cart]"));
        }

        rows.Sort(StringComparer.OrdinalIgnoreCase);
        return string.Join(" ; ", rows.ToArray());
    }

    /// <summary>
    /// Checks that the chests placed by the hooks form the neat layout the player expects (#0 base, #1 #2 stacked on it, #3 #4 side by side, #5 front to front, #6 #7 a row behind):
    /// stacked ones above each other, side by side ones at the same height and depth, row ones at the same height and across, and every piece resting on something
    /// (a ray down from its underside meets a collider within 0.25 m). Returns "layout ok" or the list of faults.
    /// </summary>
    [TestHook("chestsnapping.placedLayout")]
    private static string PlacedLayout()
    {
        const float tol = 0.06f;
        if (Placed.Count < 8) return "need 8 placed chests, have " + Placed.Count;

        var faults = new System.Collections.Generic.List<string>();
        Vector3 b = Placed[0].transform.position;
        for (int i = 1; i <= 2; i++)
        {
            Vector3 d = Placed[i].transform.position - Placed[i - 1].transform.position;
            if (Mathf.Abs(d.x) > tol || Mathf.Abs(d.z) > tol || d.y < 0.1f) faults.Add("#" + i + " is not stacked straight on #" + (i - 1) + " (offset " + d.ToString("0.00") + ")");
        }

        foreach (int i in new[] { 3, 4, 5, 6, 7 })
            if (Mathf.Abs(Placed[i].transform.position.y - b.y) > tol) faults.Add("#" + i + " is " + (Placed[i].transform.position.y - b.y).ToString("0.00") + " m off the height of #0");
        foreach (int i in new[] { 3, 4 })
            if (Mathf.Abs(Placed[i].transform.position.z - b.z) > tol) faults.Add("#" + i + " is " + (Placed[i].transform.position.z - b.z).ToString("0.00") + " m out of line (depth) with #0");
        foreach (int i in new[] { 6, 7 })
            if (Mathf.Abs(Placed[i].transform.position.x - b.x) > tol) faults.Add("#" + i + " is " + (Placed[i].transform.position.x - b.x).ToString("0.00") + " m out of line (across) with #0");

        for (int i = 0; i < Placed.Count; i++)
        {
            Bounds box = default(Bounds);
            bool any = false;
            foreach (Collider c in Placed[i].GetComponentsInChildren<Collider>())
            {
                if (c.isTrigger) continue;
                if (!any) { box = c.bounds; any = true; } else box.Encapsulate(c.bounds);
            }

            if (!any) continue;
            Vector3 from = new Vector3(box.center.x, box.min.y + 0.05f, box.center.z);
            bool grounded = false;
            foreach (RaycastHit h in Physics.RaycastAll(from, Vector3.down, 0.3f))
                if (!h.collider.transform.IsChildOf(Placed[i].transform)) { grounded = true; break; }
            if (!grounded) faults.Add("#" + i + " floats: nothing under it within 0.25 m");
        }

        string r = faults.Count == 0 ? "layout ok" : "layout faults: " + string.Join("; ", faults.ToArray());
        ModLog.Verbose("ModTestHooks: placedLayout " + r);
        return r;
    }

    /// <summary>Snap points of a ghost (made like the game makes it: no ZNetView init) against a normally placed copy of the same prefab: "ghost p3 (..) / placed p3 (..) | boxes". They must be identical, or chests snap off by the difference.</summary>
    [TestHook("chestsnapping.pointCompare")]
    private static string PointCompare(string prefab)
    {
        GameObject source = Prefab(prefab);
        GameObject ghost;
        ZNetView.m_forceDisableInit = true;
        try { ghost = UnityEngine.Object.Instantiate(source, new Vector3(0f, -500f, 0f), Quaternion.identity); }
        finally { ZNetView.m_forceDisableInit = false; }
        GameObject placed = UnityEngine.Object.Instantiate(source, new Vector3(0f, -520f, 0f), Quaternion.identity);
        string r = prefab + ":";
        foreach (int i in new[] { 0, 3, 6, 9 })
        {
            Transform g = FindPoint(ghost.transform, i), p = FindPoint(placed.transform, i);
            r += " p" + i + " ghost " + (g == null ? "none" : g.localPosition.ToString("0.000")) + " placed " + (p == null ? "none" : p.localPosition.ToString("0.000")) + " |";
        }

        Bounds gb, pb;
        ChestSnapPoints.TryGetLocalBounds(ghost.transform, out gb);
        ChestSnapPoints.TryGetLocalBounds(placed.transform, out pb);
        r += " box ghost " + gb.center.ToString("0.000") + " " + gb.size.ToString("0.000") + " placed " + pb.center.ToString("0.000") + " " + pb.size.ToString("0.000");
        var list = new System.Collections.Generic.List<Transform>();
        ghost.GetComponent<Piece>().GetSnapPoints(list);
        r += " | list(" + list.Count + "):";
        for (int i = 0; i < list.Count; i++) r += " " + i + "=" + list[i].name + list[i].localPosition.ToString("0.00");
        r += " | colliders:";
        foreach (Collider c in placed.GetComponentsInChildren<Collider>(true)) r += " " + c.name + "/" + LayerMask.LayerToName(c.gameObject.layer) + (c.enabled ? "" : "(off)") + (c.isTrigger ? "(trig)" : "");
        r += " | root layer " + LayerMask.LayerToName(placed.layer);
        r += " | scale " + ghost.transform.localScale.ToString("0.000");
        UnityEngine.Object.Destroy(ghost);
        UnityEngine.Object.Destroy(placed);
        return r;
    }
}
