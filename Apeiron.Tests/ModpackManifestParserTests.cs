using Apeiron.Services;
using Xunit;

namespace Apeiron.Tests;

public class ModpackManifestParserTests
{
    [Fact]
    public void ParseMrpackIndex_reads_dependencies_and_files()
    {
        const string json = """
            {
              "formatVersion": 1,
              "game": "minecraft",
              "versionId": "abc",
              "name": "Cool Pack",
              "files": [
                {
                  "path": "mods/example.jar",
                  "hashes": { "sha1": "deadbeef" },
                  "downloads": [ "https://cdn.modrinth.com/data/example.jar" ],
                  "fileSize": 10,
                  "env": { "client": "required", "server": "required" }
                },
                {
                  "path": "mods/server-only.jar",
                  "downloads": [ "https://cdn.modrinth.com/data/server.jar" ],
                  "env": { "client": "unsupported", "server": "required" }
                }
              ],
              "dependencies": {
                "minecraft": "1.20.1",
                "fabric-loader": "0.15.0"
              }
            }
            """;

        var index = ModpackManifestParser.ParseMrpackIndex(json);

        Assert.Equal("Cool Pack", index.Metadata.Name);
        Assert.Equal("1.20.1", index.Metadata.MinecraftVersion);
        Assert.Equal("Fabric", index.Metadata.Loader);
        Assert.Equal("0.15.0", index.Metadata.LoaderVersion);
        Assert.Single(index.Files);
        Assert.EndsWith("example.jar", index.Files[0].Path);
        Assert.Equal("https://cdn.modrinth.com/data/example.jar", index.Files[0].Downloads[0]);
    }

    [Theory]
    [InlineData("forge-47.2.0", "Forge", "47.2.0")]
    [InlineData("fabric-0.14.21", "Fabric", "0.14.21")]
    [InlineData("neoforge-20.4.190", "NeoForge", "20.4.190")]
    [InlineData("quilt-0.20.0", "Quilt", "0.20.0")]
    public void ParseCurseForgeLoaderId_maps_known_loaders(string id, string loader, string version)
    {
        var (parsedLoader, parsedVersion) = ModpackManifestParser.ParseCurseForgeLoaderId(id);
        Assert.Equal(loader, parsedLoader);
        Assert.Equal(version, parsedVersion);
    }

    [Fact]
    public void ParseCurseForgeManifest_reads_mods_and_loader()
    {
        const string json = """
            {
              "minecraft": {
                "version": "1.19.2",
                "modLoaders": [
                  { "id": "forge-43.2.0", "primary": true }
                ]
              },
              "manifestType": "minecraftModpack",
              "manifestVersion": 1,
              "name": "All the Mods",
              "version": "1.0",
              "author": "ATM",
              "files": [
                { "projectID": 238222, "fileID": 3847103, "required": true },
                { "projectID": 1, "fileID": 2, "required": false }
              ],
              "overrides": "overrides"
            }
            """;

        var manifest = ModpackManifestParser.ParseCurseForgeManifest(json);

        Assert.Equal("All the Mods", manifest.Metadata.Name);
        Assert.Equal("1.19.2", manifest.Metadata.MinecraftVersion);
        Assert.Equal("Forge", manifest.Metadata.Loader);
        Assert.Equal("43.2.0", manifest.Metadata.LoaderVersion);
        Assert.Equal(2, manifest.Files.Count);
        Assert.Equal(238222, manifest.Files[0].ProjectId);
        Assert.Equal(3847103, manifest.Files[0].FileId);
        Assert.True(manifest.Files[0].Required);
        Assert.False(manifest.Files[1].Required);
        Assert.Equal("overrides", manifest.OverridesFolder);
    }

    [Fact]
    public void ParseMrpackIndex_prefers_quilt_over_fabric_when_present()
    {
        const string json = """
            {
              "name": "Quilt Pack",
              "files": [],
              "dependencies": {
                "minecraft": "1.20.4",
                "quilt-loader": "0.24.0"
              }
            }
            """;

        var index = ModpackManifestParser.ParseMrpackIndex(json);
        Assert.Equal("Quilt", index.Metadata.Loader);
        Assert.Equal("0.24.0", index.Metadata.LoaderVersion);
    }
}
