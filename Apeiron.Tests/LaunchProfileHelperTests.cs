using System.Collections.Generic;
using System.IO;
using Apeiron.Services;
using Xunit;

namespace Apeiron.Tests;

/// <summary>
/// Regression coverage for pack failures seen with StoneBlock (NeoForge) and RLCraft (Forge 1.12).
/// </summary>
public class LaunchProfileHelperTests
{
    [Fact]
    public void NeoForge_does_not_put_inherited_vanilla_jar_on_classpath()
    {
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Path.Combine("versions", "1.21.1", "1.21.1.jar")
        };

        var path = LaunchProfileHelper.ResolveClientJarPath(
            "versions",
            "neoforge-21.1.243",
            jarProperty: null,
            inheritsFrom: "1.21.1",
            mainClass: "cpw.mods.bootstraplauncher.BootstrapLauncher",
            fileExists: existing.Contains);

        Assert.Null(path);
    }

    [Fact]
    public void Legacy_Forge_uses_inherited_vanilla_jar_when_profile_jar_missing()
    {
        var parent = Path.Combine("versions", "1.12.2", "1.12.2.jar");
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { parent };

        var path = LaunchProfileHelper.ResolveClientJarPath(
            "versions",
            "1.12.2-forge-14.23.5.2860",
            jarProperty: null,
            inheritsFrom: "1.12.2",
            mainClass: "net.minecraft.launchwrapper.Launch",
            fileExists: existing.Contains);

        Assert.Equal(parent, path);
    }

    [Fact]
    public void Explicit_profile_jar_wins_when_present()
    {
        var jar = Path.Combine("versions", "custom", "custom.jar");
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { jar };

        var path = LaunchProfileHelper.ResolveClientJarPath(
            "versions",
            "neoforge-21.1.243",
            jarProperty: "custom",
            inheritsFrom: "1.21.1",
            mainClass: "cpw.mods.bootstraplauncher.BootstrapLauncher",
            fileExists: existing.Contains);

        Assert.Equal(jar, path);
    }

    [Fact]
    public void Legacy_minecraftArguments_include_FML_tweak_class()
    {
        const string raw =
            "--username ${auth_player_name} --version ${version_name} --gameDir ${game_directory} " +
            "--assetsDir ${assets_root} --assetIndex ${assets_index_name} --uuid ${auth_uuid} " +
            "--accessToken ${auth_access_token} --userType ${user_type} " +
            "--tweakClass net.minecraftforge.fml.common.launcher.FMLTweaker --versionType Forge";

        var vars = new Dictionary<string, string>
        {
            ["${auth_player_name}"] = "Player",
            ["${version_name}"] = "1.12.2-forge-14.23.5.2860",
            ["${game_directory}"] = @"D:\game",
            ["${assets_root}"] = @"D:\assets",
            ["${assets_index_name}"] = "1.12",
            ["${auth_uuid}"] = "00000000-0000-0000-0000-000000000000",
            ["${auth_access_token}"] = "offline",
            ["${user_type}"] = "legacy"
        };

        var args = LaunchProfileHelper.ParseLegacyMinecraftArguments(raw, vars);

        Assert.Contains("--tweakClass", args);
        Assert.Contains("net.minecraftforge.fml.common.launcher.FMLTweaker", args);
        Assert.DoesNotContain(args, a => LaunchProfileHelper.StillHasPlaceholders(a));
    }

    [Fact]
    public void Classpath_separator_substitution_keeps_module_path_arg()
    {
        var modulePath =
            "${library_directory}/a.jar${classpath_separator}${library_directory}/b.jar";
        var vars = new Dictionary<string, string>
        {
            ["${library_directory}"] = @"D:\libs",
            ["${classpath_separator}"] = Path.PathSeparator.ToString()
        };

        var resolved = LaunchProfileHelper.Substitute(modulePath, vars);
        Assert.False(LaunchProfileHelper.StillHasPlaceholders(resolved));
        Assert.Contains(Path.PathSeparator, resolved);
        Assert.Contains(@"D:\libs/a.jar", resolved);
        Assert.Contains(@"D:\libs/b.jar", resolved);
    }

    [Fact]
    public void DropOrphanValueFlags_removes_bare_module_path_flag()
    {
        var args = new List<string> { "-Xmx8G", "-p", "--add-modules", "ALL-MODULE-PATH" };
        LaunchProfileHelper.DropOrphanValueFlags(args);
        Assert.DoesNotContain("-p", args);
        Assert.Contains("--add-modules", args);
        Assert.Contains("ALL-MODULE-PATH", args);
    }

    [Fact]
    public void Required_substitution_keys_include_classpath_separator() =>
        Assert.Contains("${classpath_separator}", LaunchProfileHelper.RequiredSubstitutionKeys);

    [Theory]
    [InlineData("forge-14.23.5.2860", "Forge", "14.23.5.2860")]
    [InlineData("neoforge-21.1.243", "NeoForge", "21.1.243")]
    [InlineData("fabric-0.16.0", "Fabric", "0.16.0")]
    public void CurseForge_loader_ids_parse(string id, string loader, string version)
    {
        var (parsedLoader, parsedVersion) = ModpackManifestParser.ParseCurseForgeLoaderId(id);
        Assert.Equal(loader, parsedLoader);
        Assert.Equal(version, parsedVersion);
    }
}
