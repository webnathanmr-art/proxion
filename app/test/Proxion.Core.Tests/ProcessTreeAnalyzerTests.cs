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

    [Fact]
    public void GrowFamily_FindsChildrenAndGrandchildren_InOneCall()
    {
        var snapshot = new[]
        {
            new ProcessSnapshot(100, 1, "PurpleLauncher.exe"),
            new ProcessSnapshot(200, 100, "PurpleHelper.exe"),
            new ProcessSnapshot(300, 200, "Aion2.exe"),
        };

        var family = ProcessTreeAnalyzer.GrowFamily(snapshot, knownFamilyPids: new[] { 100 });

        Assert.Equal(new HashSet<int> { 100, 200, 300 }, family);
    }

    [Fact]
    public void GrowFamily_DoesNotRequireTheRootToStillBeAlive()
    {
        // PURPLE (pid 100) has already exited by the time this snapshot is taken, but
        // the game it launched (pid 300) is still alive and its recorded ParentPid
        // still correctly points back to 100 - Windows never changes that field after
        // the parent exits. GrowFamily must be able to use that fact even though pid
        // 100 itself is absent from this snapshot.
        var snapshot = new[]
        {
            new ProcessSnapshot(300, 100, "Aion2.exe"),
        };

        var family = ProcessTreeAnalyzer.GrowFamily(snapshot, knownFamilyPids: new[] { 100 });

        Assert.Contains(300, family);
    }

    [Fact]
    public void GrowFamily_NeverForgetsPreviouslyKnownMembers_EvenIfTheyAreNowGone()
    {
        var snapshot = new[]
        {
            new ProcessSnapshot(300, 1, "Aion2.exe"), // pid 100 and 200 have both exited
        };

        var family = ProcessTreeAnalyzer.GrowFamily(snapshot, knownFamilyPids: new[] { 100, 200 });

        Assert.Equal(new HashSet<int> { 100, 200 }, family); // nothing new found, nothing lost either
    }

    [Fact]
    public void GetProcessNames_ReturnsOnlyNamesOfPidsInTheGivenSet_ExcludingAnyThatArentCurrentlyAlive()
    {
        // pid 100 (PURPLE) is in the requested set (it's part of the family) but has
        // already exited, so it's absent from this snapshot; pid 300 is alive.
        var snapshot = new[]
        {
            new ProcessSnapshot(300, 100, "Aion2.exe"),
            new ProcessSnapshot(9000, 1, "notepad.exe"),
        };

        var names = ProcessTreeAnalyzer.GetProcessNames(snapshot, pids: new[] { 100, 300 });

        Assert.Equal(new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Aion2.exe" }, names);
    }
}
