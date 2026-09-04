namespace Proxion.Core;

/// <summary>A point-in-time view of one running process, as much as we need to walk the process tree.</summary>
public readonly record struct ProcessSnapshot(int Pid, int ParentPid, string Name);
