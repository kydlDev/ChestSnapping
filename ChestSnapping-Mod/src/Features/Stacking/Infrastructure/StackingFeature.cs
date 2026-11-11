using KydlCore.Common;
using KydlCore.Core;

namespace ChestSnapping;

/// <summary>Chests can be placed on other chests without planks or floors. Gate: EnableAll and [Stacking] Enable. The work is done by StackingPatch.</summary>
internal sealed class StackingFeature : IFeature
{
    internal const string FeatureId = "stacking";

    /// <summary>True between Start and Stop.</summary>
    internal static bool Running { get; private set; }

    public string Id { get { return FeatureId; } }

    public bool IsEnabled() { return ModSettings.StackingOn; }

    public bool Start()
    {
        Running = true;
        ModLog.Verbose("StackingFeature: started");
        return Running;
    }

    public bool Stop()
    {
        Running = false;
        ModLog.Verbose("StackingFeature: stopped");
        return !Running;
    }
}
