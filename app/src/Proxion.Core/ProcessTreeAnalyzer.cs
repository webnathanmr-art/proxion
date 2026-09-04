namespace Proxion.Core;

/// <summary>
/// Determines exactly which running processes are PURPLE itself or one of its
/// (transitive) children, from a snapshot of the whole process table. This is what
/// keeps Proxion's proxy rule scoped to "PURPLE and the games it actually launched" -
/// instead of a hand-maintained, easy-to-get-wrong list of game executable names.
/// </summary>
public static class ProcessTreeAnalyzer
{
    /// <summary>
    /// Returns the PID of <paramref name="rootPid"/> plus every PID reachable from it
    /// by following ParentPid links forward (children, grandchildren, ...). Returns an
    /// empty set if <paramref name="rootPid"/> isn't present in the snapshot at all.
    /// </summary>
    public static HashSet<int> GetDescendantPids(IReadOnlyCollection<ProcessSnapshot> snapshot, int rootPid)
    {
        var result = new HashSet<int>();
        if (!snapshot.Any(p => p.Pid == rootPid))
        {
            return result;
        }

        var byParent = snapshot
            .GroupBy(p => p.ParentPid)
            .ToDictionary(g => g.Key, g => g.Select(p => p.Pid).ToList());

        var queue = new Queue<int>();
        queue.Enqueue(rootPid);
        result.Add(rootPid);

        while (queue.Count > 0)
        {
            var pid = queue.Dequeue();
            if (!byParent.TryGetValue(pid, out var children))
            {
                continue;
            }

            foreach (var childPid in children)
            {
                // A process can never be its own ancestor in a real process table, but
                // guard against malformed/cyclic snapshot data anyway: HashSet.Add
                // returning false (already visited) is what stops the queue from growing
                // forever.
                if (result.Add(childPid))
                {
                    queue.Enqueue(childPid);
                }
            }
        }

        return result;
    }

    /// <summary>
    /// The distinct executable names (e.g. "PurpleLauncher.exe") of PURPLE and every
    /// process it has (transitively) spawned, as of this snapshot.
    /// </summary>
    public static HashSet<string> GetDescendantProcessNames(IReadOnlyCollection<ProcessSnapshot> snapshot, int rootPid)
    {
        var pids = GetDescendantPids(snapshot, rootPid);
        return snapshot
            .Where(p => pids.Contains(p.Pid))
            .Select(p => p.Name)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Whether the session is still "alive": either the root PID is still running, or
    /// some process matching one of the previously-tracked names still is. The second
    /// check matters once PURPLE itself has exited but a game it launched is still open.
    /// </summary>
    public static bool IsSessionAlive(IReadOnlyCollection<ProcessSnapshot> snapshot, int rootPid, IReadOnlyCollection<string> trackedNames)
    {
        if (snapshot.Any(p => p.Pid == rootPid))
        {
            return true;
        }
        return snapshot.Any(p => trackedNames.Contains(p.Name, StringComparer.OrdinalIgnoreCase));
    }
}
