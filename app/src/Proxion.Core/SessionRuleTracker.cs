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
/// Owns the one piece of state that matters for scoping the proxy correctly: the set
/// of process names currently being routed. Starts out containing only PURPLE's own
/// executable name, and grows only when a poll's process-tree snapshot shows a new
/// process descending from PURPLE's PID - so the rule set can never include anything
/// PURPLE didn't actually launch.
/// </summary>
public sealed class SessionRuleTracker
{
    private readonly int _rootPid;
    private readonly HashSet<string> _trackedNames;
    private bool _sawRootAlive;

    public SessionRuleTracker(int rootPid, string rootProcessName)
    {
        _rootPid = rootPid;
        _trackedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { rootProcessName };
    }

    /// <summary>The process names currently being routed through the proxy.</summary>
    public IReadOnlyCollection<string> TrackedNames => _trackedNames;

    public SessionPollResult Poll(IReadOnlyCollection<ProcessSnapshot> snapshot, out IReadOnlyList<string> newlyTrackedNames)
    {
        newlyTrackedNames = Array.Empty<string>();

        var rootAlive = snapshot.Any(p => p.Pid == _rootPid);
        if (rootAlive)
        {
            _sawRootAlive = true;

            var names = ProcessTreeAnalyzer.GetDescendantProcessNames(snapshot, _rootPid);
            var added = names.Where(n => !_trackedNames.Contains(n)).ToList();
            if (added.Count == 0)
            {
                return SessionPollResult.Unchanged;
            }

            foreach (var name in added)
            {
                _trackedNames.Add(name);
            }
            newlyTrackedNames = added;
            return SessionPollResult.NewProcessesDetected;
        }

        var anyTrackedAlive = snapshot.Any(p => _trackedNames.Contains(p.Name));
        if (anyTrackedAlive)
        {
            return SessionPollResult.Unchanged;
        }

        return _sawRootAlive ? SessionPollResult.SessionEnded : SessionPollResult.RootNotYetSeen;
    }
}
