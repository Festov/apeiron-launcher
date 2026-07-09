using System;
using System.IO;
using System.Text.Json.Serialization;

namespace Apeiron;

public class BuildInfo
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "";
    public string MinecraftVersion { get; set; } = "";
    public string Loader { get; set; } = "";
    public string LoaderVersion { get; set; } = "";
    public string InstancePath { get; set; } = "";
    public bool InstallFabricApi { get; set; }
    public bool IsModded { get; set; }
    public bool ModsEnabled { get; set; } = true;
    public string JvmArgs { get; set; } = "";
    /// <summary>0 = use global RAM from settings.</summary>
    public int RamGb { get; set; }
    public int ResolutionWidth { get; set; }
    public int ResolutionHeight { get; set; }
    public bool Fullscreen { get; set; }

    [JsonIgnore]
    public bool IsPrimary { get; set; }

    public string ComboDisplay => IsPrimary ? $"★ {DisplayName}" : DisplayName;

    public static string GenerateDefaultName(string mcVersion, string loader, string loaderVersion, bool isModded)
    {
        if (!isModded)
            return mcVersion;

        var loaderPart = string.IsNullOrWhiteSpace(loaderVersion)
            ? loader
            : $"{loader} {loaderVersion}".Trim();

        return $"{mcVersion} - {loaderPart}";
    }

    public string DisplayName =>
        !string.IsNullOrWhiteSpace(Name)
            ? Name
            : GenerateDefaultName(MinecraftVersion, Loader, LoaderVersion, IsModded);

    public string LoaderIcon => GetLoaderIcon(Loader, IsModded);

    public static string GetLoaderIcon(string loader, bool isModded)
    {
        if (!isModded) return "🟩";
        return loader.ToLowerInvariant() switch
        {
            "fabric" => "🧵",
            "quilt" => "🪡",
            "forge" => "🔨",
            "neoforge" => "⚙️",
            _ => "📦"
        };
    }

    /// <summary>ID профиля в папке versions/ (vanilla id или fabric-loader-X-Y).</summary>
    public string GetVersionId()
    {
        if (!IsModded || string.IsNullOrEmpty(Loader))
            return MinecraftVersion;

        return Loader.ToLowerInvariant() switch
        {
            "fabric" => $"fabric-loader-{LoaderVersion}-{MinecraftVersion}",
            "quilt" => $"quilt-loader-{LoaderVersion}-{MinecraftVersion}",
            "forge" => $"{MinecraftVersion}-forge-{LoaderVersion}",
            "neoforge" => $"neoforge-{LoaderVersion}",
            _ => MinecraftVersion
        };
    }

    public string GetGameDir()
    {
        if (!string.IsNullOrEmpty(InstancePath))
            return InstancePath;

        var launcherDir = AppDomain.CurrentDomain.BaseDirectory;
        return Path.Combine(launcherDir, "instances", Id);
    }

    public string GetModsDir() => Path.Combine(GetGameDir(), "mods");

    public int ResolveRamGb(int globalRamGb) => RamGb > 0 ? RamGb : globalRamGb;

    public bool IsLoaderSupported()
    {
        if (!IsModded) return true;
        var loader = Loader.ToLowerInvariant();
        return loader is "fabric" or "quilt" or "forge" or "neoforge" or "";
    }

    public override string ToString() => DisplayName;

    public void EnsureInstanceFolders()
    {
        var gameDir = GetGameDir();
        Directory.CreateDirectory(gameDir);
        Directory.CreateDirectory(Path.Combine(gameDir, "mods"));
        Directory.CreateDirectory(Path.Combine(gameDir, "config"));
        Directory.CreateDirectory(Path.Combine(gameDir, "saves"));
        Directory.CreateDirectory(Path.Combine(gameDir, "resourcepacks"));
    }

    public BuildInfo CloneWithNewId(string name)
    {
        return new BuildInfo
        {
            Id = Guid.NewGuid().ToString(),
            Name = name,
            MinecraftVersion = MinecraftVersion,
            Loader = Loader,
            LoaderVersion = LoaderVersion,
            InstallFabricApi = InstallFabricApi,
            IsModded = IsModded,
            ModsEnabled = ModsEnabled,
            JvmArgs = JvmArgs,
            RamGb = RamGb,
            ResolutionWidth = ResolutionWidth,
            ResolutionHeight = ResolutionHeight,
            Fullscreen = Fullscreen
        };
    }
}
