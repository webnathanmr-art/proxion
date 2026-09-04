namespace Proxion.App.Services;

/// <summary>Best-effort auto-detection of NCSOFT PURPLE's install location.</summary>
public static class PurpleLocator
{
    public static string? TryAutoDetect()
    {
        var bases = new[]
        {
            Environment.GetEnvironmentVariable("ProgramFiles(x86)"),
            Environment.GetEnvironmentVariable("ProgramFiles"),
            Environment.GetEnvironmentVariable("ProgramData"),
        };

        foreach (var b in bases)
        {
            if (string.IsNullOrEmpty(b))
            {
                continue;
            }

            var ncBase = Path.Combine(b, "NCSOFT");
            if (!Directory.Exists(ncBase))
            {
                continue;
            }

            try
            {
                var found = Directory
                    .EnumerateFiles(ncBase, "PurpleLauncher.exe", SearchOption.AllDirectories)
                    .FirstOrDefault();
                if (found is not null)
                {
                    return found;
                }
            }
            catch (UnauthorizedAccessException)
            {
                // Some subfolder we can't read; keep looking elsewhere.
            }
            catch (IOException)
            {
            }
        }

        return null;
    }
}
