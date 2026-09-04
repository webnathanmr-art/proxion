using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Proxion.Core;

namespace Proxion.App.Services;

/// <summary>What Proxion remembers between runs, so the setup window can pre-fill itself.</summary>
public sealed class StoredSettings
{
    public string PurpleLauncherPath { get; set; } = string.Empty;
    public string ProxyBridgeCliPath { get; set; } = string.Empty;
    public string ProxyType { get; set; } = "socks5";
    public string ProxyHost { get; set; } = string.Empty;
    public int ProxyPort { get; set; }
    public string ProxyUsername { get; set; } = string.Empty;

    /// <summary>DPAPI-protected (current user), base64-encoded. Never the plaintext password.</summary>
    public string? ProtectedPassword { get; set; }
}

/// <summary>Loads and saves <see cref="StoredSettings"/> under %AppData%\Proxion\settings.json.</summary>
public static class SettingsStore
{
    private static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Proxion", "settings.json");

    public static StoredSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return new StoredSettings();
            }
            var json = File.ReadAllText(SettingsPath);
            return JsonSerializer.Deserialize<StoredSettings>(json) ?? new StoredSettings();
        }
        catch (Exception)
        {
            return new StoredSettings();
        }
    }

    public static void Save(StoredSettings settings)
    {
        var dir = Path.GetDirectoryName(SettingsPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }
        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(SettingsPath, json);
    }

    /// <summary>Encrypts a password for storage, scoped to the current Windows user.</summary>
    public static string? ProtectPassword(string plain)
    {
        if (string.IsNullOrEmpty(plain))
        {
            return null;
        }
        var bytes = Encoding.UTF8.GetBytes(plain);
        var protectedBytes = ProtectedData.Protect(bytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(protectedBytes);
    }

    /// <summary>Reverses <see cref="ProtectPassword"/>. Returns an empty string if unprotection fails
    /// (e.g. the settings file was copied from a different machine or user account).</summary>
    public static string UnprotectPassword(string? protectedBase64)
    {
        if (string.IsNullOrEmpty(protectedBase64))
        {
            return string.Empty;
        }
        try
        {
            var bytes = Convert.FromBase64String(protectedBase64);
            var plainBytes = ProtectedData.Unprotect(bytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plainBytes);
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    public static StoredSettings FromProxySettings(string purplePath, string cliPath, ProxySettings proxy)
    {
        return new StoredSettings
        {
            PurpleLauncherPath = purplePath,
            ProxyBridgeCliPath = cliPath,
            ProxyType = proxy.TypeLabel,
            ProxyHost = proxy.Host,
            ProxyPort = proxy.Port,
            ProxyUsername = proxy.Username,
            ProtectedPassword = ProtectPassword(proxy.Password),
        };
    }

    public static ProxySettings ToProxySettings(this StoredSettings stored)
    {
        return new ProxySettings
        {
            Type = stored.ProxyType.Equals("http", StringComparison.OrdinalIgnoreCase) ? ProxyType.Http : ProxyType.Socks5,
            Host = stored.ProxyHost,
            Port = stored.ProxyPort,
            Username = stored.ProxyUsername,
            Password = UnprotectPassword(stored.ProtectedPassword),
        };
    }
}
