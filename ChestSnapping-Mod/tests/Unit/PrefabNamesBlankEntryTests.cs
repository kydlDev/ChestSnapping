namespace ChestSnapping.Tests.Unit;

/// <summary>Blank list entries ("a, ,b") are dropped by Parse and Remove (found by mutation testing: a ">= 0" length check let an empty name through).</summary>
[TestFixture]
public class PrefabNamesBlankEntryTests
{
    [Test]
    public void Parse_BlankEntryBetweenNames_IsDropped()
    {
        var set = PrefabNames.Parse("a, ,b");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(set, Has.Count.EqualTo(2));
            Assert.That(set, Does.Not.Contain(string.Empty));
        }
    }

    [Test]
    public void Remove_BlankEntryBetweenNames_IsNotKept()
    {
        Assert.That(PrefabNames.Remove("a, ,b", "a"), Is.EqualTo("b"));
    }
}
