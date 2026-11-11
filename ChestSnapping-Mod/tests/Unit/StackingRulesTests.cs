using ChestSnapping;

namespace ChestSnapping.Tests.Unit;

/// <summary>When an aimed-at chest has to count as a building support: the pure rule in StackingRules.</summary>
[TestFixture]
public class StackingRulesTests
{
    [TestCase(true, true, false, true)]   // chest on a chest (chests do not support): override
    [TestCase(true, true, true, false)]   // the aimed-at piece already supports: nothing to do
    [TestCase(true, false, false, false)] // chest on something that is not a chest (rock, wall, bed): vanilla decides
    [TestCase(false, true, false, false)] // a non-chest ghost aimed at a chest: vanilla stays (a bed on a chest is still refused)
    [TestCase(false, false, false, false)]
    public void NeedsSupportOverride_OnlyChestOnNonSupportingChest(bool ghostChest, bool hitChest, bool hitSupports, bool want)
    {
        Assert.That(StackingRules.NeedsSupportOverride(ghostChest, hitChest, hitSupports), Is.EqualTo(want));
    }
}
