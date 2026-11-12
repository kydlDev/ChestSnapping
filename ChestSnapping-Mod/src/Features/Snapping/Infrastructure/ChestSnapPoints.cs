using System;
using System.Collections.Generic;
using KydlCore.Common;
using UnityEngine;

namespace ChestSnapping;

internal static class ChestSnapPoints
{
    private const string SnapTag = "snappoint"; // tag vanilla's Piece.GetSnapPoints looks for (direct children only)
    private const string NamePrefix = "$chestsnap_";

    private static readonly HashSet<string> WarnedPrefabs = new HashSet<string>();

    // ---------------------------------------------------------------- public API

    /// <summary>
    /// Adds or removes snap points on this piece depending on the current config.
    /// With <paramref name="refresh"/> set, points that already exist are also moved to match the
    /// current adjustments (used after config changes).
    /// </summary>
    internal static void Apply(Piece piece, bool refresh = false)
    {
        if (piece == null) return;

        try
        {
            string prefabName = GetPrefabName(piece.gameObject);
            bool want = Wants(piece.gameObject);
            bool has = HasPoints(piece.transform);

            if (want && !has)
            {
                AddPoints(piece.transform, prefabName);
                ModLog.Verbose("ChestSnapPoints: added snap points to " + prefabName);
            }
            else if (want && refresh)
            {
                Reposition(piece.transform, prefabName);
            }
            else if (!want && has)
            {
                RemovePoints(piece.transform);
                ModLog.Verbose("ChestSnapPoints: removed snap points from " + prefabName);
            }

            if (want && SnapMarkers.Enabled) SnapMarkers.Sync(piece.transform);
        }
        catch (Exception e)
        {
            ModLog.Warn("ChestSnapPoints: could not update snap points on " + piece.name + ": " + e);
        }
    }

    /// <summary>Re-evaluates every loaded piece (used after config changes).</summary>
    internal static void RefreshAll()
    {
        foreach (Piece piece in UnityEngine.Object.FindObjectsByType<Piece>(FindObjectsSortMode.None))
            Apply(piece, true);
    }

    /// <summary>Should this object (prefab asset or instance) get snap points under the current config?</summary>
    internal static bool Wants(GameObject go)
    {
        return Source(go) != null;
    }

    /// <summary>"default", "extra", "auto" or null (= no snap points).</summary>
    internal static string Source(GameObject go)
    {
        return ModSettings.PrefabsOn ? Kind(go) : null;
    }

    /// <summary>Is this a chest the mod knows (built-in, extra list or auto-detected), whatever the feature switches say? Used by stacking.</summary>
    internal static bool IsChest(GameObject go)
    {
        return Kind(go) != null;
    }

    /// <summary>"default", "extra", "api" (registered by another mod), "auto" or null, from the prefab lists only (no feature switch).</summary>
    private static string Kind(GameObject go)
    {
        string name = GetPrefabName(go);

        if (PrefabSets.Excluded.Contains(name)) return null;

        if (PrefabSets.ExtraSet.Contains(name)) return "extra";
        if (PrefabSets.Included.Contains(name)) return "default";
        if (ChestSnappingApi.Contains(name)) return "api";

        if (ModSettings.AutoDetectContainers.Value && IsPlainContainer(go)) return "auto";

        return null;
    }

    /// <summary>A Container on the root that is not a ship/cart (those have containers too).</summary>
    internal static bool IsPlainContainer(GameObject go)
    {
        return go.GetComponent<Container>() != null
               && go.GetComponent<Ship>() == null
               && go.GetComponent<Vagon>() == null;
    }

    internal static string GetPrefabName(GameObject go)
    {
        return PrefabNames.StripClone(go.name);
    }

    // ---------------------------------------------------------------- add / move / remove

    internal static bool HasPoints(Transform root)
    {
        return CountPoints(root) > 0;
    }

    /// <summary>How many of our snap points hang directly under this root.</summary>
    internal static int CountPoints(Transform root)
    {
        int n = 0;
        for (int i = 0; i < root.childCount; i++)
            if (root.GetChild(i).name.StartsWith(NamePrefix, StringComparison.Ordinal)) n++;
        return n;
    }

    /// <summary>Index into <see cref="SnapLayout.Points"/> if this transform is one of our snap points, else -1.</summary>
    internal static int PointIndex(Transform t)
    {
        return t.name.StartsWith(NamePrefix, StringComparison.Ordinal)
            ? SnapLayout.IndexOfObjectName(t.name)
            : -1;
    }

    private static void AddPoints(Transform root, string prefabName)
    {
        EnsureVisibleBox(root, prefabName);

        Vector3[] positions;
        if (!TryComputePositions(root, prefabName, out positions)) return;

        for (int i = 0; i < SnapLayout.Points.Length; i++)
        {
            var go = new GameObject(SnapLayout.Points[i].ObjectName);
            go.tag = SnapTag;

            Transform t = go.transform;
            t.SetParent(root, false);
            t.localRotation = Quaternion.identity;
            t.localScale = Vector3.one;
            t.localPosition = positions[i];
        }
    }

    /// <summary>Moves the existing snap points of a chest to where the current adjustments say they belong.</summary>
    private static void Reposition(Transform root, string prefabName)
    {
        Vector3[] positions;
        if (!TryComputePositions(root, prefabName, out positions)) return;

        for (int i = 0; i < root.childCount; i++)
        {
            Transform child = root.GetChild(i);
            int index = PointIndex(child);
            if (index >= 0) child.localPosition = positions[index];
        }
    }

    private static void RemovePoints(Transform root)
    {
        for (int i = root.childCount - 1; i >= 0; i--)
        {
            Transform child = root.GetChild(i);
            if (child.name == VisibleBoxName)
            {
                UnityEngine.Object.Destroy(child.gameObject);
                ResetSupportColliders(root);
                continue;
            }

            if (!child.name.StartsWith(NamePrefix, StringComparison.Ordinal)) continue;

            // Destroy() is deferred to end of frame; un-tag first so Piece.GetSnapPoints ignores it immediately.
            child.gameObject.tag = "Untagged";
            UnityEngine.Object.Destroy(child.gameObject);
        }
    }

    // ---------------------------------------------------------------- positions

    private static bool TryComputePositions(Transform root, string prefabName, out Vector3[] positions)
    {
        positions = null;

        Bounds bounds;
        if (!TryGetLocalBounds(root, out bounds))
        {
            if (WarnedPrefabs.Add(prefabName))
                ModLog.Warn("ChestSnapPoints: no colliders or meshes found on '" + prefabName + "', cannot place snap points on it.");
            return false;
        }

        positions = ComputePositions(bounds, SnapAdjustments.Active(prefabName));
        return true;
    }

    /// <summary>
    /// Final local position of each of the 14 points: the box corner/face-center from the bounds,
    /// after the prefab's shift and grow, plus any extra offset for that single point.
    /// </summary>
    internal static Vector3[] ComputePositions(Bounds bounds, PrefabAdjust adj)
    {
        Vector3 center = bounds.center;
        Vector3 extents = bounds.extents;

        if (adj != null)
        {
            center += adj.Shift;
            extents = Vector3.Max(extents + adj.Grow, Vector3.zero);
        }

        var result = new Vector3[SnapLayout.Points.Length];
        for (int i = 0; i < result.Length; i++)
        {
            Vector3 p = center + Vector3.Scale(extents, SnapLayout.Points[i].Factor);

            Vector3 extra;
            if (adj != null && adj.Points.TryGetValue(i, out extra)) p += extra;

            result[i] = p;
        }

        return result;
    }

    // ---------------------------------------------------------------- bounds

    /// <summary>
    /// Axis-aligned bounds of the object's solid colliders, expressed in the ROOT's local space
    /// (so it is independent of where/how the chest is currently placed). Falls back to mesh
    /// bounds if the prefab has no non-trigger colliders.
    /// </summary>
    internal static bool TryGetLocalBounds(Transform root, out Bounds bounds)
    {
        bounds = default(Bounds);
        bool found = false;
        Matrix4x4 worldToRoot = root.worldToLocalMatrix;

        foreach (Collider col in root.GetComponentsInChildren<Collider>(true))
        {
            if (col == null || col.isTrigger) continue;

            Vector3 center, size;
            if (!TryGetColliderBox(col, out center, out size)) continue;

            Encapsulate(ref bounds, ref found, worldToRoot * col.transform.localToWorldMatrix, center, size);
        }

        if (found)
        {
            if (ModSettings.VisibleBounds != null && ModSettings.VisibleBounds.Value) EncapsulateVisibleMeshes(root, worldToRoot, ref bounds, ref found);
            return true;
        }

        foreach (MeshFilter mf in root.GetComponentsInChildren<MeshFilter>(true))
        {
            if (mf.gameObject.name == SnapMarkers.MarkerName) continue; // our own debug spheres

            Mesh mesh = mf.sharedMesh;
            if (mesh == null) continue;

            Encapsulate(ref bounds, ref found, worldToRoot * mf.transform.localToWorldMatrix,
                mesh.bounds.center, mesh.bounds.size);
        }

        return found;
    }

    /// <summary>
    /// Adds the meshes you actually see on a closed chest to the box: enabled, active (also every parent up to the root), not the "open" lid and not our debug markers.
    /// Some chests (the Grausten chest) have a collider far smaller than the visible chest, so chests snapped into each other.
    /// </summary>
    private static void EncapsulateVisibleMeshes(Transform root, Matrix4x4 worldToRoot, ref Bounds bounds, ref bool found)
    {
        foreach (MeshRenderer mr in root.GetComponentsInChildren<MeshRenderer>(true))
        {
            if (mr == null || !mr.enabled) continue;

            string name = mr.gameObject.name;
            if (name == SnapMarkers.MarkerName || name.IndexOf("open", StringComparison.OrdinalIgnoreCase) >= 0) continue;

            bool active = true;
            for (Transform t = mr.transform; t != null && t != root.parent; t = t.parent)
                if (!t.gameObject.activeSelf) { active = false; break; }
            if (!active) continue;

            MeshFilter mf = mr.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) continue;

            Encapsulate(ref bounds, ref found, worldToRoot * mf.transform.localToWorldMatrix, mf.sharedMesh.bounds.center, mf.sharedMesh.bounds.size);
        }
    }

    private const string VisibleBoxName = "ChestSnapVisibleBox";

    /// <summary>
    /// Some chests have a collider far smaller than the chest you see (the Grausten chest: the lid is not in it), so chests were placed into each other and a chest on top
    /// of it had no support within reach (the game destroyed it). When the visible closed meshes stick out of the colliders by more than 8 cm, one box collider
    /// that covers the visible chest is added (same layer as the chest's own colliders); it is removed again with the snap points.
    /// </summary>
    private static void EnsureVisibleBox(Transform root, string prefabName)
    {
        if (ModSettings.VisibleBounds == null || !ModSettings.VisibleBounds.Value) return;
        if (root.Find(VisibleBoxName) != null) return;

        Bounds solid = default(Bounds);
        bool found = false;
        int layer = root.gameObject.layer;
        int snapMask = LayerMask.GetMask("piece", "piece_nonsolid");
        bool onSnapLayer = false;
        Matrix4x4 worldToRoot = root.worldToLocalMatrix;
        foreach (Collider col in root.GetComponentsInChildren<Collider>(true))
        {
            if (col == null || col.isTrigger) continue;
            Vector3 center, size;
            if (!TryGetColliderBox(col, out center, out size)) continue;
            if (!found) layer = col.gameObject.layer;
            if ((snapMask & (1 << col.gameObject.layer)) != 0) onSnapLayer = true;
            Encapsulate(ref solid, ref found, worldToRoot * col.transform.localToWorldMatrix, center, size);
        }

        if (!found) return;

        Bounds all = solid;
        bool foundAll = true;
        EncapsulateVisibleMeshes(root, worldToRoot, ref all, ref foundAll);

        const float tolerance = 0.08f;
        bool sticksOut = all.max.x > solid.max.x + tolerance || all.max.y > solid.max.y + tolerance || all.max.z > solid.max.z + tolerance
            || all.min.x < solid.min.x - tolerance || all.min.y < solid.min.y - tolerance || all.min.z < solid.min.z - tolerance;
        if (!sticksOut && onSnapLayer) return;

        // The game only finds snap points of pieces whose colliders are on the piece layers (Piece.GetSnapPoints). Pots keep their colliders on the
        // default layer, so their points were never found and the ghost hung 0.2 m in the air; the box carries the piece layer for them.
        if (!onSnapLayer) layer = LayerMask.NameToLayer("piece");

        var go = new GameObject(VisibleBoxName);
        go.layer = layer;
        go.transform.SetParent(root, false);
        BoxCollider box = go.AddComponent<BoxCollider>();
        box.center = all.center;
        box.size = all.size;
        ResetSupportColliders(root);
        ModLog.Verbose("ChestSnapPoints: " + prefabName + " has a collider smaller than the chest you see - added a body box " + all.size.ToString("0.00"));
    }

    /// <summary>WearNTear caches its colliders: make it read them again so the new (or removed) box counts for the support check.</summary>
    private static void ResetSupportColliders(Transform root)
    {
        WearNTear wear = root.GetComponent<WearNTear>();
        if (wear != null) HarmonyLib.Traverse.Create(wear).Field("m_colliders").SetValue(null);
    }

    private static bool TryGetColliderBox(Collider col, out Vector3 center, out Vector3 size)
    {
        var box = col as BoxCollider;
        if (box != null)
        {
            center = box.center;
            size = box.size;
            return true;
        }

        var sphere = col as SphereCollider;
        if (sphere != null)
        {
            center = sphere.center;
            size = Vector3.one * (sphere.radius * 2f);
            return true;
        }

        var capsule = col as CapsuleCollider;
        if (capsule != null)
        {
            float d = capsule.radius * 2f;
            float h = Mathf.Max(capsule.height, d);
            center = capsule.center;
            size = capsule.direction == 0 ? new Vector3(h, d, d)
                 : capsule.direction == 1 ? new Vector3(d, h, d)
                 : new Vector3(d, d, h);
            return true;
        }

        var mc = col as MeshCollider;
        if (mc != null && mc.sharedMesh != null)
        {
            center = mc.sharedMesh.bounds.center;
            size = mc.sharedMesh.bounds.size;
            return true;
        }

        center = Vector3.zero;
        size = Vector3.zero;
        return false;
    }

    /// <summary>Transforms the 8 corners of a local-space box by <paramref name="m"/> and grows the bounds.</summary>
    private static void Encapsulate(ref Bounds bounds, ref bool found, Matrix4x4 m, Vector3 center, Vector3 size)
    {
        Vector3 h = size * 0.5f;

        for (int i = 0; i < 8; i++)
        {
            Vector3 corner = center + new Vector3(
                (i & 1) == 0 ? -h.x : h.x,
                (i & 2) == 0 ? -h.y : h.y,
                (i & 4) == 0 ? -h.z : h.z);

            Vector3 p = m.MultiplyPoint3x4(corner);

            if (!found)
            {
                bounds = new Bounds(p, Vector3.zero);
                found = true;
            }
            else
            {
                bounds.Encapsulate(p);
            }
        }
    }
}
