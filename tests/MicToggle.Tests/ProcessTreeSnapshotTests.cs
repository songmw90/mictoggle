using System.Reflection;
using Xunit;

namespace MicToggle.Tests;

public sealed class ProcessTreeSnapshotTests
{
    private static readonly Type? SnapshotType =
        Type.GetType("MicToggle.ProcessTreeSnapshot, MicToggle", throwOnError: false);

    [Fact]
    public void Descendants_include_the_root_and_transitive_children_only()
    {
        var parents = new Dictionary<int, int>
        {
            [100] = 1,
            [110] = 100,
            [120] = 110,
            [130] = 100,
            [200] = 1,
            [210] = 200,
        };

        var descendants = FindDescendants(100, parents);

        Assert.Equal([100, 110, 120, 130], descendants.Order());
    }

    [Fact]
    public void Descendant_walk_terminates_when_the_snapshot_contains_a_cycle()
    {
        var parents = new Dictionary<int, int>
        {
            [100] = 120,
            [110] = 100,
            [120] = 110,
            [200] = 100,
        };

        var descendants = FindDescendants(100, parents);

        Assert.Equal([100, 110, 120, 200], descendants.Order());
    }

    private static HashSet<int> FindDescendants(
        int rootProcessId,
        IReadOnlyDictionary<int, int> parentByProcessId)
    {
        Assert.NotNull(SnapshotType);
        var method = SnapshotType!.GetMethod(
            "FindDescendants",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        return Assert.IsType<HashSet<int>>(method!.Invoke(
            null,
            [rootProcessId, parentByProcessId]));
    }
}
