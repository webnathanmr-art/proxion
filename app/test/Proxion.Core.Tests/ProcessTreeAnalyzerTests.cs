using Proxion.Core;
using Xunit;

namespace Proxion.Core.Tests;

public class ProcessTreeAnalyzerTests
{
    [Fact]
    public void GetDescendantPids_ReturnsEmpty_WhenRootNotInSnapshot()
    {
        var snapshot = new[]
        {
            new ProcessSnapshot(10, 1, "unrelated.exe"),
        };

        var result = ProcessTreeAnalyzer.GetDescendantPids(snapshot, rootPid: 999);

        Assert.Empty(result);
    }

    [Fact]
    public void GetDescendantPids_IncludesRootAndItsChildrenAndGrandchildren()
    {
        var snapshot = new[]
        {
            new ProcessSnapshot(100, 1, "PurpleLauncher.exe"),   // root
            new ProcessSnapshot(200, 100, "PurpleHelper.exe"),   // child
            new ProcessSnapshot(300, 200, "BnS.exe"),            // grandchild - the actual game
            new ProcessSnapshot(400, 1, "chrome.exe"),           // unrelated, unrelated parent
            new ProcessSnapshot(500, 999, "orphan.exe"),         // unrelated, parent not in snapshot
        };

        var result = ProcessTreeAnalyzer.GetDescendantPids(snapshot, rootPid: 100);

        Assert.Equal(new HashSet<int> { 100, 200, 300 }, result);
    }

    [Fact]
    public void GetDescendantPids_DoesNotIncludeSiblingsOfRoot()
    {
        var snapshot = new[]
        {
            new ProcessSnapshot(100, 1, "PurpleLauncher.exe"),
            new ProcessSnapshot(101, 1, "SomeOtherApp.exe"), // same parent (1) as root, but not a descendant
        };

        var result = ProcessTreeAnalyzer.GetDescendantPids(snapshot, rootPid: 100);

        Assert.Equal(new HashSet<int> { 100 }, result);
    }

    [Fact]
    public void GetDescendantPids_TerminatesOnCyclicData_InsteadOfLoopingForever()
    {
        // Malformed input that would never occur on a real process table
        // (a process cannot be its own ancestor), but the algorithm must not hang on it.
        var snapshot = new[]
        {
            new ProcessSnapshot(100, 200, "a.exe"),
            new ProcessSnapshot(200, 100, "b.exe"),
        };

        var result = ProcessTreeAnalyzer.GetDescendantPids(snapshot, rootPid: 100);

        Assert.Equal(new HashSet<int> { 100, 200 }, result);
    }

    [Fact]
    public void GetDescendantProcessNames_IsCaseInsensitiveAndDeduplicated()
    {
        var snapshot = new[]
        {
            new ProcessSnapshot(100, 1, "PurpleLauncher.exe"),
            new ProcessSnapshot(200, 100, "BNS.EXE"),
            new ProcessSnapshot(300, 100, "bns.exe"),
        };

        var names = ProcessTreeAnalyzer.GetDescendantProcessNames(snapshot, rootPid: 100);

        Assert.Equal(2, names.Count);
        Assert.Contains("PurpleLauncher.exe", names);
        Assert.Contains("bns.exe", names); // case-insensitive membership
    }

    [Fact]
    public void IsSessionAlive_TrueWhileRootPidIsRunning()
    {
        var snapshot = new[] { new ProcessSnapshot(100, 1, "PurpleLauncher.exe") };

        Assert.True(ProcessTreeAnalyzer.IsSessionAlive(snapshot, rootPid: 100, trackedNames: new[] { "PurpleLauncher.exe" }));
    }

    [Fact]
    public void IsSessionAlive_TrueAfterRootExits_IfATrackedGameIsStillRunning()
    {
        // PURPLE (pid 100) has closed, but the game it launched (BnS.exe) is still open.
        var snapshot = new[] { new ProcessSnapshot(300, 1, "BnS.exe") };

        Assert.True(ProcessTreeAnalyzer.IsSessionAlive(snapshot, rootPid: 100, trackedNames: new[] { "PurpleLauncher.exe", "BnS.exe" }));
    }

    [Fact]
    public void IsSessionAlive_FalseOnceNeitherRootNorAnyTrackedNameIsRunning()
    {
        var snapshot = new[] { new ProcessSnapshot(999, 1, "notepad.exe") };

        Assert.False(ProcessTreeAnalyzer.IsSessionAlive(snapshot, rootPid: 100, trackedNames: new[] { "PurpleLauncher.exe", "BnS.exe" }));
    }

    [Fact]
    public void UnrelatedProcessSharingAGameName_IsNotIncluded_WhenItIsNotADescendantOfPurple()
    {
        // A same-named process running elsewhere on the system, with no relation to PURPLE,
        // must not be pulled into scope just because ProxyBridge matches rules by name -
        // the tree analyzer itself must only report real descendants.
        var snapshot = new[]
        {
            new ProcessSnapshot(100, 1, "PurpleLauncher.exe"),
            new ProcessSnapshot(200, 100, "BnS.exe"),   // real game, child of PURPLE
            new ProcessSnapshot(9000, 1, "BnS.exe"),    // unrelated process, e.g. started by hand
        };

        var pids = ProcessTreeAnalyzer.GetDescendantPids(snapshot, rootPid: 100);

        Assert.DoesNotContain(9000, pids);
        Assert.Contains(200, pids);
    }
}
