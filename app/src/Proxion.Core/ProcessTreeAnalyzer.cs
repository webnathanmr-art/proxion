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

    /// <summary>
    /// Expands a set of PIDs already known to belong to PURPLE's "family" to also
    /// include every currently-running process whose recorded parent PID is already a
    /// family member - transitively, so one call also catches grandchildren.
    ///
    /// Unlike <see cref="GetDescendantPids"/>, this does NOT require any family member
    /// (including the original root) to still be present in the snapshot: a process's
    /// ParentPid is recorded once, at creation, and never changes even after that
    /// parent has exited. So if PURPLE launches a game and then closes itself - a very
    /// common launcher pattern - the game's ParentPid still correctly points back to
    /// PURPLE's PID, and calling this every poll with the accumulated family from all
    /// previous polls keeps discovering new descendants long after the original root
    /// process is gone. <see cref="GetDescendantPids"/> alone cannot do this, since it
    /// recomputes from scratch from a single root PID that must currently be alive.
    ///
    /// One accepted tradeoff: PIDs are reused by Windows over time, so if PURPLE's old
    /// PID is later reassigned to an unrelated process within the same Proxion session,
    /// that process's own children would incorrectly be treated as PURPLE's family too.
    /// This is unlikely within a normal play session's timeframe and mirrors a similar
    /// tradeoff ProxyBridge itself already has (it matches rules by name, not PID).
    /// </summary>
    public static HashSet<int> GrowFamily(IReadOnlyCollection<ProcessSnapshot> snapshot, IReadOnlyCollection<int> knownFamilyPids)
    {
        var family = new HashSet<int>(knownFamilyPids);
        bool changed;
        do
        {
            changed = false;
            foreach (var p in snapshot)
            {
                if (family.Contains(p.ParentPid) && family.Add(p.Pid))
                {
                    changed = true;
                }
            }
        } while (changed);

        return family;
    }

    /// <summary>The distinct executable names of every process in the snapshot whose PID is in <paramref name="pids"/>.</summary>
    public static HashSet<string> GetProcessNames(IReadOnlyCollection<ProcessSnapshot> snapshot, IReadOnlyCollection<int> pids)
    {
        return snapshot
            .Where(p => pids.Contains(p.Pid))
            .Select(p => p.Name)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }
}
