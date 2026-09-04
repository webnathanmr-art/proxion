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
    public void Poll_StillDetectsANewGame_EvenIfPurpleHasAlreadyExitedByTheTimeItAppears()
    {
        // Regression test for a real reported bug: PURPLE hands off to the game and
        // closes itself (a common launcher pattern) quickly enough that PURPLE's own
        // PID is never alive in the same poll as the game's. The old implementation
        // only ever looked for new descendants while the root was alive in that same
        // poll, so it never discovered the game at all, and then incorrectly reported
        // SessionEnded (since neither PURPLE nor - because it was never added -
        // the game were in the tracked-names set) right as the user started playing.
        var tracker = new SessionRuleTracker(rootPid: 100, rootProcessName: "PurpleLauncher.exe");

        // First poll: only PURPLE is running.
        Assert.Equal(SessionPollResult.Unchanged, tracker.Poll(new[]
        {
            new ProcessSnapshot(100, 1, "PurpleLauncher.exe"),
        }, out _));

        // Second poll: PURPLE has already exited, but the game it spawned (whose
        // recorded ParentPid still correctly points back to PURPLE's PID) is now up.
        var result = tracker.Poll(new[]
        {
            new ProcessSnapshot(300, 100, "Aion2.exe"),
        }, out var newNames);

        Assert.Equal(SessionPollResult.NewProcessesDetected, result);
        Assert.Equal(new[] { "Aion2.exe" }, newNames);
        Assert.Contains("Aion2.exe", tracker.TrackedNames);

        // And the session correctly stays alive afterwards, tracking the game.
        var stillRunning = new[] { new ProcessSnapshot(300, 1, "Aion2.exe") };
        Assert.Equal(SessionPollResult.Unchanged, tracker.Poll(stillRunning, out _));
    }

    [Fact]
    public void Poll_StillDetectsAGrandchildGame_EvenAfterTheIntermediateHelperHasExited()
    {
        // PURPLE -> helper -> game. The helper (pid 200) is seen once, alongside
        // PURPLE, so it's recorded into the family; by the next poll both PURPLE and
        // the helper have exited and only the game is left running, with its ParentPid
        // pointing at the (now dead) helper. The family must already contain 200 from
        // the earlier poll for this chain to be inferable at all - Proxion can only
        // ever learn a PID belongs to PURPLE's family while that PID's own row is still
        // visible in some snapshot.
        var tracker = new SessionRuleTracker(rootPid: 100, rootProcessName: "PurpleLauncher.exe");
        tracker.Poll(new[]
        {
            new ProcessSnapshot(100, 1, "PurpleLauncher.exe"),
            new ProcessSnapshot(200, 100, "PurpleHelper.exe"),
        }, out _);

        var result = tracker.Poll(new[]
        {
            new ProcessSnapshot(300, 200, "Aion2.exe"), // parent (200, the helper) is no longer in this snapshot
        }, out var newNames);

        Assert.Equal(SessionPollResult.NewProcessesDetected, result);
        Assert.Equal(new[] { "Aion2.exe" }, newNames);
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
