using Proxion.Core;
using Xunit;

namespace Proxion.Core.Tests;

public class PurpleEcosystemTests
{
    [Fact]
    public void AlwaysRoutedProcessNames_IncludesKnownPersistentPurpleServices()
    {
        Assert.Contains("purple-agent.exe", PurpleEcosystem.AlwaysRoutedProcessNames, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("purpleonp.exe", PurpleEcosystem.AlwaysRoutedProcessNames, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void UnioningWithTrackedNames_AndBuildingAProfile_RoutesBothWithoutDuplicates()
    {
        // This is how TrayContext/PersonalAppContext actually use the list: unioned in
        // alongside whatever the dynamic tree detection found, right before writing the
        // .pbprofile - so confirm that combination produces the expected, deduplicated rule.
        var trackedNames = new[] { "PurpleLauncher.exe", "purple-agent.exe" }; // one already found dynamically
        var names = trackedNames.Concat(PurpleEcosystem.AlwaysRoutedProcessNames);

        var proxy = new ProxySettings { Host = "10.0.0.1", Port = 1080 };
        var json = PbProfileBuilder.Build(proxy, names);

        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var processName = doc.RootElement.GetProperty("ProxyRules")[0].GetProperty("ProcessName").GetString();

        Assert.Equal("PurpleLauncher.exe; purple-agent.exe; purpleonp.exe", processName);
    }
}
