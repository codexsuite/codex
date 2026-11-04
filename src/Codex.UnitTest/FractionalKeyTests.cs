using Codex.Core;

namespace Codex.UnitTest;

[TestFixture]
public class FractionalKeyTests
{
    [Test]
    public void Between_WhenNeighboursAdjacent_ShouldSortStrictlyBetween()
    {
        var key = FractionalKey.Between("a0", "a1");

        string.CompareOrdinal("a0", key).ShouldBeLessThan(0);
        string.CompareOrdinal(key, "a1").ShouldBeLessThan(0);
    }

    [Test]
    public void Between_WhenNothingBefore_ShouldSortBeforeFirst()
    {
        var key = FractionalKey.Between(null, "a0");

        string.CompareOrdinal(key, "a0").ShouldBeLessThan(0);
    }

    [Test]
    public void Between_WhenNothingAfter_ShouldSortAfterLast()
    {
        var key = FractionalKey.Between("a0", null);

        string.CompareOrdinal(key, "a0").ShouldBeGreaterThan(0);
    }

    [Test]
    public void Between_WhenListEmpty_ShouldGiveAKey()
    {
        FractionalKey.Between(null, null).ShouldNotBeNullOrEmpty();
    }

    [Test]
    public void Between_WhenOrderReversed_ShouldThrow()
    {
        Should.Throw<ArgumentException>(() => FractionalKey.Between("a1", "a0"));
    }

    [Test]
    public void Between_WhenAlwaysInsertedAtTheSameSpot_ShouldKeepOrder()
    {
        var keys = new List<string> { "a0", "a1" };
        for (var i = 0; i < 60; i++)
        {
            keys.Insert(1, FractionalKey.Between(keys[0], keys[1]));
        }

        keys.ShouldBe(keys.Order(StringComparer.Ordinal).ToList());
        keys.Distinct().Count().ShouldBe(keys.Count);
    }

    [Test]
    public void Between_WhenInsertedAtRandomSpots_ShouldKeepOrder()
    {
        var random = new Random(7);
        var keys = new List<string> { FractionalKey.Between(null, null) };
        for (var i = 0; i < 300; i++)
        {
            var at = random.Next(keys.Count + 1);
            keys.Insert(at, FractionalKey.Between(at > 0 ? keys[at - 1] : null, at < keys.Count ? keys[at] : null));
        }

        keys.ShouldBe(keys.Order(StringComparer.Ordinal).ToList());
        keys.Distinct().Count().ShouldBe(keys.Count);
    }

    [Test]
    public void Between_WhenAlwaysInsertedFirst_ShouldKeepOrder()
    {
        var keys = new List<string> { "a0" };
        for (var i = 0; i < 80; i++)
        {
            keys.Insert(0, FractionalKey.Between(null, keys[0]));
        }

        keys.ShouldBe(keys.Order(StringComparer.Ordinal).ToList());
        keys.Distinct().Count().ShouldBe(keys.Count);
    }

    [Test]
    public void Between_WhenAlwaysAppended_ShouldKeepOrder()
    {
        var keys = new List<string> { "a0" };
        for (var i = 0; i < 80; i++)
        {
            keys.Add(FractionalKey.Between(keys[^1], null));
        }

        keys.ShouldBe(keys.Order(StringComparer.Ordinal).ToList());
        keys.Distinct().Count().ShouldBe(keys.Count);
    }
}
