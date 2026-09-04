using Proxion.Core;
using Xunit;

namespace Proxion.Core.Tests;

public class SessionRuleTrackerTests
{
    [Fact]
    public void StartsOutTrackingOnlyTheRootProcessName()
    {
        var tracker = new SessionRuleTracker(rootPid: 100, rootProcessName: "PurpleLauncher.exe");

        Assert.Equal(new[] { "PurpleLauncher.exe" }, tracker.TrackedNames);
    }

    [Fact]
    public void Poll_ReturnsRootNotYetSeen_BeforePurpleAppearsInAnySnapshot()
    {
        var tracker = new SessionRuleTracker(rootPid: 100, rootProcessName: "PurpleLauncher.exe");
        var snapshot = new[] { new ProcessSnapshot(1, 0, "System") };

        var result = tracker.Poll(snapshot, out var newNames);

        Assert.Equal(SessionPollResult.RootNotYetSeen, result);
        Assert.Empty(newNames);
    }

    [Fact]
    public void Poll_DetectsANewlyLaunchedGame_AndAddsItToTrackedNames()
    {
        var tracker = new SessionRuleTracker(rootPid: 100, rootProcessName: "PurpleLauncher.exe");

        // First poll: only PURPLE itself is running yet.
        var first = new[] { new ProcessSnapshot(100, 1, "PurpleLauncher.exe") };
        Assert.Equal(SessionPollResult.Unchanged, tracker.Poll(first, out _));

        // Second poll: PURPLE has launched the actual game as a child process.
        var second = new[]
        {
            new ProcessSnapshot(100, 1, "PurpleLauncher.exe"),
            new ProcessSnapshot(200, 100, "BnS.exe"),
        };
        var result = tracker.Poll(second, out var newNames);

        Assert.Equal(SessionPollResult.NewProcessesDetected, result);
        Assert.Equal(new[] { "BnS.exe" }, newNames);
        Assert.Equal(
            new[] { "BnS.exe", "PurpleLauncher.exe" },
            tracker.TrackedNames.OrderBy(n => n, StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public void Poll_DoesNotReRaiseTheSameProcessNameOnSubsequentPolls()
    {
        var tracker = new SessionRuleTracker(rootPid: 100, rootProcessName: "PurpleLauncher.exe");
        var withGame = new[]
        {
            new ProcessSnapshot(100, 1, "PurpleLauncher.exe"),
            new ProcessSnapshot(200, 100, "BnS.exe"),
        };

        Assert.Equal(SessionPollResult.NewProcessesDetected, tracker.Poll(withGame, out _));
        Assert.Equal(SessionPollResult.Unchanged, tracker.Poll(withGame, out var newNames));
        Assert.Empty(newNames);
    }

    [Fact]
    public void Poll_KeepsSessionAlive_AfterPurpleExits_WhileTheGameIsStillRunning()
    {
        var tracker = new SessionRuleTracker(rootPid: 100, rootProcessName: "PurpleLauncher.exe");
        tracker.Poll(new[]
        {
            new ProcessSnapshot(100, 1, "PurpleLauncher.exe"),
            new ProcessSnapshot(200, 100, "BnS.exe"),
        }, out _);

        // PURPLE has now closed, but the game is still open.
        var afterLauncherCloses = new[] { new ProcessSnapshot(200, 1, "BnS.exe") };

        var result = tracker.Poll(afterLauncherCloses, out var newNames);

        Assert.Equal(SessionPollResult.Unchanged, result);
        Assert.Empty(newNames);
    }

    [Fact]
    public void Poll_ReturnsSessionEnded_OnceNothingTrackedIsRunningAnymore()
    {
        var tracker = new SessionRuleTracker(rootPid: 100, rootProcessName: "PurpleLauncher.exe");
        tracker.Poll(new[] { new ProcessSnapshot(100, 1, "PurpleLauncher.exe") }, out _);

        var empty = new[] { new ProcessSnapshot(1, 0, "System") };

        Assert.Equal(SessionPollResult.SessionEnded, tracker.Poll(empty, out _));
    }

    [Fact]
    public void Poll_DoesNotReportSessionEnded_IfRootWasNeverSeenAlive()
    {
        // Distinguishes "PURPLE hasn't started yet" from "PURPLE started and then closed" -
        // the caller applies its own startup-timeout policy only in the former case.
        var tracker = new SessionRuleTracker(rootPid: 100, rootProcessName: "PurpleLauncher.exe");

        var result = tracker.Poll(new[] { new ProcessSnapshot(1, 0, "System") }, out _);

        Assert.Equal(SessionPollResult.RootNotYetSeen, result);
    }

    [Fact]
    public void UnrelatedProcessWithGamesName_DoesNotPreventSessionEnded_IfPurpleNeverLaunchedIt()
    {
        var tracker = new SessionRuleTracker(rootPid: 100, rootProcessName: "PurpleLauncher.exe");
        tracker.Poll(new[] { new ProcessSnapshot(100, 1, "PurpleLauncher.exe") }, out _);

        // Nothing PURPLE-related is running, so even though *some* process exists,
        // the session is over.
        var somethingElseEntirely = new[] { new ProcessSnapshot(9000, 1, "notepad.exe") };

        Assert.Equal(SessionPollResult.SessionEnded, tracker.Poll(somethingElseEntirely, out _));
    }
}
