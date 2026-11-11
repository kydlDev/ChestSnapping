using ChestSnapping;

namespace ChestSnapping.Tests.Unit;

/// <summary>The yaw a chest ghost takes when it snaps to a chest: the pure rule in ChestAlignment.</summary>
[TestFixture]
public class ChestAlignmentTests
{
    [TestCase(0f, 0f, 0f)]
    [TestCase(22.5f, 0f, 0f)]        // a few degrees off: lines up
    [TestCase(337.5f, 0f, 0f)]       // 22.5 degrees the other way
    [TestCase(45f, 0f, 90f)]         // halfway rounds to the next quarter
    [TestCase(90f, 0f, 90f)]         // a quarter turn the player chose is kept
    [TestCase(180f, 0f, 180f)]       // so is a half turn
    [TestCase(270f, 0f, 270f)]
    [TestCase(10f, 30f, 30f)]        // target not on an axis: ghost takes the target's angle
    [TestCase(100f, 30f, 120f)]      // plus the quarter turn
    [TestCase(350f, 355f, 355f)]     // wrap around 360
    public void AlignedYaw_TakesTargetYawPlusNearestQuarterTurn(float ghost, float target, float want)
    {
        Assert.That(ChestAlignment.AlignedYaw(ghost, target), Is.EqualTo(want).Within(0.001f));
    }
}
