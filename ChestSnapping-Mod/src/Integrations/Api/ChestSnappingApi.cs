using System;
using System.Collections.Generic;
using UnityEngine;

namespace ChestSnapping;

/// <summary>
/// Public API for other mods: make their storage chests known to ChestSnapping, so they get snap points ([Prefabs] Enable) and can be
/// stacked on other chests ([Stacking] Enable). Both the user's [Prefabs] ExcludedPrefabs and the switches still win.
/// Soft dependency (no reference needed): call it by reflection, e.g.
///   Type.GetType("ChestSnapping.ChestSnappingApi, ChestSnapping")?.GetMethod("RegisterChest").Invoke(null, new object[] { "MyMod_Crate" });
/// Registering before ChestSnapping has loaded is fine (the names are kept). Call from the main thread (Awake / game start).
/// </summary>
public static class ChestSnappingApi
{
    /// <summary>Increases when a method is added or changes meaning, so a caller can check what it talks to.</summary>
    public const int ApiVersion = 1;

    private static readonly object Gate = new object();
    private static readonly HashSet<string> Registered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private static volatile bool _refreshPending;

    /// <summary>Makes a chest prefab known to ChestSnapping.</summary>
    /// <param name="prefabName">The prefab name of the chest piece (no "(Clone)").</param>
    /// <returns>True when the prefab is registered afterwards.</returns>
    public static bool RegisterChest(string prefabName)
    {
        if (string.IsNullOrWhiteSpace(prefabName)) return false;

        string name = prefabName.Trim();
        bool registered;
        lock (Gate)
        {
            if (Registered.Add(name)) _refreshPending = true;
            registered = Registered.Contains(name);
        }

        KydlCore.Common.ModLog.Verbose("ChestSnappingApi: RegisterChest " + name + " -> " + registered);
        return registered;
    }

    /// <summary>Takes a chest prefab back out (a prefab of the built-in list or the user's ExtraPrefabs stays).</summary>
    /// <param name="prefabName">The prefab name given to RegisterChest.</param>
    /// <returns>True when the prefab is no longer registered through the API.</returns>
    public static bool UnregisterChest(string prefabName)
    {
        if (string.IsNullOrWhiteSpace(prefabName)) return true;

        string name = prefabName.Trim();
        bool gone;
        lock (Gate)
        {
            if (Registered.Remove(name)) _refreshPending = true;
            gone = !Registered.Contains(name);
        }

        KydlCore.Common.ModLog.Verbose("ChestSnappingApi: UnregisterChest " + name + " -> " + gone);
        return gone;
    }

    /// <summary>Does ChestSnapping treat this object as a chest (built-in, ExtraPrefabs, API, auto-detected; not excluded)?</summary>
    /// <param name="chest">A chest piece or prefab.</param>
    /// <returns>True when it is a known chest.</returns>
    public static bool IsChest(GameObject chest)
    {
        return chest != null && ChestSnapPoints.IsChest(chest);
    }

    /// <summary>Is stacking chests on chests on right now ([General] Enable mod and [Stacking] Enable)?</summary>
    /// <returns>True when stacking is active.</returns>
    public static bool IsStackingOn()
    {
        return ModSettings.StackingOn;
    }

    internal static bool Contains(string prefabName)
    {
        lock (Gate) { return Registered.Contains(prefabName); }
    }

    internal static string[] Snapshot()
    {
        lock (Gate)
        {
            var list = new string[Registered.Count];
            Registered.CopyTo(list);
            return list;
        }
    }

    /// <summary>True once after the registrations changed; the plugin then refreshes the loaded chests on the main thread.</summary>
    internal static bool TakeRefreshRequest()
    {
        bool pending = _refreshPending;
        _refreshPending = false;
        return pending;
    }
}
