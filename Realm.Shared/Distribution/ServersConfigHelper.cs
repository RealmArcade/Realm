using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Realm.Shared.Distribution;

public class ServersConfig
{
    public List<string> AdminPublicKeys { get; set; } = new();
    public List<string> AdminPublicKey { get => AdminPublicKeys; set => AdminPublicKeys = value; }
    public List<string> Servers { get; set; } = new();
}

public static class ServersConfigHelper
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };
    public const string DefaultServerUrl = "http://127.0.0.1:5000";
    public const string DefaultAdminPublicKey = "";

    public static ServersConfig Load(string? explicitPath = null)
    {
        var triedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in GetCandidatePaths(explicitPath))
        {
            if (string.IsNullOrWhiteSpace(path) || !triedPaths.Add(path))
            {
                continue;
            }

            var config = TryLoadConfig(path);
            if (config != null)
            {
                return config;
            }
        }

        return CreateFallbackConfig();
    }

    private static IEnumerable<string> GetBaseDirectories()
    {
        yield return Directory.GetCurrentDirectory();
        yield return AppDomain.CurrentDomain.BaseDirectory;
        yield return AppContext.BaseDirectory;

        string? processDir = !string.IsNullOrEmpty(Environment.ProcessPath) ? Path.GetDirectoryName(Environment.ProcessPath) : null;
        if (!string.IsNullOrEmpty(processDir))
        {
            yield return processDir;
        }
    }

    private static IEnumerable<string> GetCandidatePaths(string? explicitPath)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            yield return explicitPath;
        }

        var baseDirs = GetBaseDirectories().Distinct().ToList();
        string[] fileNames = { "servers.json", "servers.template.json" };

        foreach (var fileName in fileNames)
        {
            foreach (var dir in baseDirs)
            {
                yield return Path.Combine(dir, fileName);
                yield return Path.Combine(dir, "Realm.Godot", fileName);
                yield return Path.Combine(dir, "..", "Realm.Godot", fileName);
                yield return Path.Combine(dir, "..", fileName);
                yield return Path.Combine(dir, "..", "..", fileName);
                yield return Path.Combine(dir, "..", "..", "..", fileName);
                yield return Path.Combine(dir, "..", "..", "..", "..", fileName);
                yield return Path.Combine(dir, "..", "..", "..", "Realm.Godot", fileName);
                yield return Path.Combine(dir, "..", "..", "..", "..", "Realm.Godot", fileName);
            }
        }
    }

    private static ServersConfig? TryLoadConfig(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            string json = File.ReadAllText(path);
            var config = JsonSerializer.Deserialize<ServersConfig>(json, Options);
            if (config != null)
            {
                NormalizeConfig(config);
                return config;
            }
        }
        catch
        {
        }

        return null;
    }

    private static ServersConfig CreateFallbackConfig()
    {
        var fallback = new ServersConfig();
        fallback.Servers.Add(DefaultServerUrl);
        if (!string.IsNullOrWhiteSpace(DefaultAdminPublicKey))
        {
            fallback.AdminPublicKeys.Add(DefaultAdminPublicKey);
        }
        return fallback;
    }

    public static string GetDefaultServerUrl(string? explicitPath = null)
    {
        var config = Load(explicitPath);
        if (config.Servers.Count > 0 && !string.IsNullOrWhiteSpace(config.Servers[0]))
        {
            return config.Servers[0];
        }
        return DefaultServerUrl;
    }

    public static List<string> GetRegistryServers(string? explicitPath = null)
    {
        return Load(explicitPath).Servers
            .Where(u => !string.IsNullOrWhiteSpace(u))
            .ToList();
    }

    public static List<string> GetAdminPublicKeys(string? explicitPath = null)
    {
        return Load(explicitPath).AdminPublicKeys
            .Where(u => !string.IsNullOrWhiteSpace(u))
            .ToList();
    }

    public static void NormalizeConfig(ServersConfig config)
    {
        if (config.AdminPublicKeys == null) config.AdminPublicKeys = new();
        if (config.Servers == null) config.Servers = new();

        config.AdminPublicKeys = config.AdminPublicKeys
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (config.AdminPublicKeys.Count == 0 && !string.IsNullOrWhiteSpace(DefaultAdminPublicKey))
        {
            config.AdminPublicKeys.Add(DefaultAdminPublicKey);
        }

        config.Servers = config.Servers
            .Where(u => !string.IsNullOrWhiteSpace(u))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (config.Servers.Count == 0)
        {
            config.Servers.Add(DefaultServerUrl);
        }
    }
}
