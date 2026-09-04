using System.Management;
using Proxion.Core;

namespace Proxion.App.Services;

/// <summary>
/// Snapshots the whole process table (pid, parent pid, image name) via WMI, so
/// <see cref="ProcessTreeAnalyzer"/> can work out which processes PURPLE actually spawned.
/// </summary>
public static class WmiProcessSnapshotProvider
{
    public static IReadOnlyList<ProcessSnapshot> GetSnapshot()
    {
        var results = new List<ProcessSnapshot>();

        using var searcher = new ManagementObjectSearcher(
            "SELECT ProcessId, ParentProcessId, Name FROM Win32_Process");
        using var collection = searcher.Get();

        foreach (var item in collection)
        {
            using var process = (ManagementObject)item;
            try
            {
                var pid = Convert.ToInt32(process["ProcessId"]);
                var parentPid = Convert.ToInt32(process["ParentProcessId"]);
                var name = process["Name"] as string ?? string.Empty;
                results.Add(new ProcessSnapshot(pid, parentPid, name));
            }
            catch (Exception)
            {
                // A row we couldn't read cleanly (e.g. a process that exited mid-query);
                // skip it rather than fail the whole snapshot.
            }
        }

        return results;
    }
}
