using System;
using System.Linq;
using FsCheck;

namespace ChestSnapping.Tests.Unit;

/// <summary>Property tests (FsCheck): the yaw alignment and the prefab name list hold for any input, not only the hand-picked cases.</summary>
[TestFixture]
public class SnappingPropertyTests
{
    static readonly Gen<float> Yaw = Props.GameFloat(1000f);
    static readonly Gen<string> Name = Props.TextFrom("abcdefghij_XYZ019", 12);

    [Test]
    public void AlignedYaw_IsAlwaysInOneTurn()
    {
        Props.Check(Prop.ForAll(Yaw.ToArbitrary(), Yaw.ToArbitrary(), (g, t) =>
        {
            float y = ChestAlignment.AlignedYaw(g, t);
            return y >= 0f && y < 360.001f;
        }));
    }

    [Test]
    public void AlignedYaw_IsAQuarterTurnFromTheTarget()
    {
        Props.Check(Prop.ForAll(Yaw.ToArbitrary(), Yaw.ToArbitrary(), (g, t) =>
        {
            float off = Math.Abs(ChestAlignment.Delta(ChestAlignment.AlignedYaw(g, t), t));
            return Math.Abs(off % 90f) < 0.01f || Math.Abs(off % 90f - 90f) < 0.01f;
        }));
    }

    [Test]
    public void AlignedYaw_IsTheNearestQuarterTurnToTheGhost()
    {
        Props.Check(Prop.ForAll(Yaw.ToArbitrary(), Yaw.ToArbitrary(), (g, t) =>
            Math.Abs(ChestAlignment.Delta(g, ChestAlignment.AlignedYaw(g, t))) <= 45.01f));
    }

    [Test]
    public void Delta_StaysInHalfATurn_AndFlipsSignWhenSwapped()
    {
        Props.Check(Prop.ForAll(Yaw.ToArbitrary(), Yaw.ToArbitrary(), (a, b) =>
        {
            float d = ChestAlignment.Delta(a, b);
            float back = ChestAlignment.Delta(b, a);
            return d >= -180.001f && d <= 180.001f && Math.Abs(d + back) < 0.01f || Math.Abs(Math.Abs(d) - 180f) < 0.01f;
        }));
    }

    [Test]
    public void Append_ThenParse_ContainsTheName_AndRemoveTakesItOut()
    {
        Props.Check(Prop.ForAll(Name.ToArbitrary(), Name.ToArbitrary(), (existing, added) =>
        {
            string csv = PrefabNames.Append(existing, added);
            bool has = PrefabNames.Parse(csv).Contains(added);
            bool gone = !PrefabNames.Parse(PrefabNames.Remove(csv, added)).Contains(added);
            return has && gone;
        }));
    }

    [Test]
    public void Remove_KeepsEveryOtherName()
    {
        Props.Check(Prop.ForAll(Name.ToArbitrary(), Name.ToArbitrary(), (keep, drop) =>
        {
            if (string.Equals(keep, drop, StringComparison.OrdinalIgnoreCase)) return true;
            string csv = PrefabNames.Append(PrefabNames.Append("", keep), drop);
            return PrefabNames.Parse(PrefabNames.Remove(csv, drop)).Contains(keep);
        }));
    }

    [Test]
    public void StripClone_UndoesTheCloneSuffix_AndIsIdempotentOnPlainNames()
    {
        Props.Check(Prop.ForAll(Name.ToArbitrary(), n =>
            PrefabNames.StripClone(n + "(Clone)") == n && PrefabNames.StripClone(PrefabNames.StripClone(n)) == PrefabNames.StripClone(n)));
    }
}
