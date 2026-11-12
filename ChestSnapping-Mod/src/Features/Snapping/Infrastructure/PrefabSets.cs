using System;
using System.Collections.Generic;

namespace ChestSnapping;

/// <summary>The parsed prefab lists of the config: built-in chests, extra chests, excluded chests.</summary>
internal static class PrefabSets
{
    /// <summary>Buildable containers that ship with the base game (prefab names, case-insensitive).</summary>
    internal static readonly string[] DefaultPrefabs =
    {
        "piece_chest_wood",       // Wood chest
        "piece_chest",            // Reinforced chest
        "piece_chest_private",    // Private chest
        "piece_chest_blackmetal", // Blackmetal chest
        "piece_chest_grausten",   // Grausten chest
        "piece_chest_warderobe",  // Wardrobe
        "piece_chest_barrel",     // Barrel
        "piece_pot1",             // Pot 1 (and its cracked / red variants)
        "piece_pot1_cracked",
        "piece_pot1_red",
        "piece_pot2",             // Pot 2
        "piece_pot2_cracked",
        "piece_pot2_red",
        "piece_pot3",             // Pot 3
        "piece_pot3_cracked",
        "piece_pot3_red",
    };

    /// <summary>Default + extra prefabs.</summary>
    internal static HashSet<string> Included = new HashSet<string>(DefaultPrefabs, StringComparer.OrdinalIgnoreCase);

    /// <summary>The prefabs of the ExtraPrefabs config entry.</summary>
    internal static HashSet<string> ExtraSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>The prefabs of the ExcludedPrefabs config entry.</summary>
    internal static HashSet<string> Excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Re-reads the config text (and the saved adjustments). Call after every config change.</summary>
    internal static void Rebuild()
    {
        ExtraSet = PrefabNames.Parse(ModSettings.ExtraPrefabs.Value);
        Excluded = PrefabNames.Parse(ModSettings.ExcludedPrefabs.Value);

        var all = new HashSet<string>(DefaultPrefabs, StringComparer.OrdinalIgnoreCase);
        all.UnionWith(ExtraSet);
        Included = all;

        SnapAdjustments.Load(ModSettings.Adjustments.Value);
    }
}
