namespace Proxion.Core;

public enum SessionPollResult
{
    /// <summary>Nothing changed; keep routing the same set of process names.</summary>
    Unchanged,

    /// <summary>PURPLE spawned a process we hadn't seen before; the rule set grew.</summary>
    NewProcessesDetected,

    /// <summary>PURPLE hasn't shown up in the process table yet. Caller decides how long to wait.</summary>
    RootNotYetSeen,

    /// <summary>PURPLE and everything it launched have closed.</summary>
    SessionEnded,
}

/// <summary>
/// Owns the two pieces of state that matter for scoping the proxy correctly: the set of
/// PIDs believed to belong to PURPLE's "family" (itself plus everything it has spawned,
/// transitively), and the set of process names currently being routed. Both start out
/// containing only PURPLE's own PID/name, and grow whenever a poll's process-tree
/// snapshot shows a new process belonging to the family - so the rule set can never
/// include anything PURPLE didn't actually launch.
///
/// The family PID set only ever grows, and growing it does not require PURPLE's own
/// process to still be alive (see <see cref="ProcessTreeAnalyzer.GrowFamily"/>) - a
/// launcher handing off to a game and then closing itself is common, and this is what
/// lets Proxion keep discovering new descendants (or even the game itself, if it wasn't
/// caught in the one poll where both happened to be alive) after that happens.
/// </summary>
public sealed class SessionRuleTracker
{
    private readonly int _rootPid;
    private readonly HashSet<int> _familyPids;
    private readonly HashSet<string> _trackedNames;
    private bool _sawRootAlive;

    public SessionRuleTracker(int rootPid, string rootProcessName)
    {
        _rootPid = rootPid;
        _familyPids = new HashSet<int> { rootPid };
        _trackedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { rootProcessName };
    }

    /// <summary>The process names currently being routed through the proxy.</summary>
    public IReadOnlyCollection<string> TrackedNames => _trackedNames;

    public SessionPollResult Poll(IReadOnlyCollection<ProcessSnapshot> snapshot, out IReadOnlyList<string> newlyTrackedNames)
    {
        newlyTrackedNames = Array.Empty<string>();

        if (snapshot.Any(p => p.Pid == _rootPid))
        {
            _sawRootAlive = true;
        }

        // Grow the family from this snapshot regardless of whether the root (or any
        // previously-found family member) is still alive - see the class/method docs.
        foreach (var pid in ProcessTreeAnalyzer.GrowFamily(snapshot, _familyPids))
        {
            _familyPids.Add(pid);
        }

        var aliveFamilyNames = ProcessTreeAnalyzer.GetProcessNames(snapshot, _familyPids);
        var added = aliveFamilyNames.Where(n => !_trackedNames.Contains(n)).ToList();
        if (added.Count > 0)
        {
            foreach (var name in added)
            {
                _trackedNames.Add(name);
            }
            newlyTrackedNames = added;
            return SessionPollResult.NewProcessesDetected;
        }

        if (!_sawRootAlive)
        {
            return SessionPollResult.RootNotYetSeen;
        }

        var anyTrackedAlive = snapshot.Any(p => _trackedNames.Contains(p.Name));
        return anyTrackedAlive ? SessionPollResult.Unchanged : SessionPollResult.SessionEnded;
    }
}
