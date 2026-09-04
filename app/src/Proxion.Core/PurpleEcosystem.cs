namespace Proxion.Core;

/// <summary>
/// Executable names known to be part of NCSOFT PURPLE's own infrastructure but that may
/// run as persistent background services rather than being spawned fresh as a child of
/// the specific PurpleLauncher.exe process Proxion launches (e.g. started once at Windows
/// login and left running). The dynamic process-tree detection (see
/// <see cref="ProcessTreeAnalyzer"/> / <see cref="SessionRuleTracker"/>) can only ever find
/// processes that are actual descendants of the PURPLE instance Proxion itself started, so
/// it would miss one of these entirely if it was already running beforehand - meaning its
/// traffic would go direct, unproxied, while everything else correctly goes through the
/// proxy. That mismatch is exactly the kind of thing a login server's fraud/abuse detection
/// can flag as suspicious.
///
/// These names are always included in the routed rule in addition to (never instead of)
/// the dynamic detection, which remains how the actual game process and PURPLE's own
/// spawned subprocesses (helpers, embedded-browser renderer processes, etc.) get covered.
/// </summary>
public static class PurpleEcosystem
{
    public static readonly IReadOnlyList<string> AlwaysRoutedProcessNames = new[]
    {
        "purple-agent.exe",
        "purpleonp.exe",
    };
}
