using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Apeiron.Services;

/// <summary>Parses Modrinth mrpack index and CurseForge pack manifests (testable, no I/O).</summary>
public static class ModpackManifestParser
{
    public sealed class MrpackFileEntry
    {
        public string Path { get; init; } = "";
        public IReadOnlyList<string> Downloads { get; init; } = Array.Empty<string>();
        public bool ClientRequired { get; init; } = true;
    }

    public sealed class CurseForgeFileEntry
    {
        public int ProjectId { get; init; }
        public int FileId { get; init; }
        public bool Required { get; init; } = true;
    }

    public sealed class MrpackIndex
    {
        public ParsedModpackMetadata Metadata { get; init; } = new();
        public IReadOnlyList<MrpackFileEntry> Files { get; init; } = Array.Empty<MrpackFileEntry>();
    }

    public sealed class CurseForgeManifest
    {
        public ParsedModpackMetadata Metadata { get; init; } = new();
        public IReadOnlyList<CurseForgeFileEntry> Files { get; init; } = Array.Empty<CurseForgeFileEntry>();
        public string OverridesFolder { get; init; } = "overrides";
    }

    public static MrpackIndex ParseMrpackIndex(string json)
    {
        var root = JObject.Parse(json);
        var deps = root["dependencies"] as JObject ?? new JObject();
        var (loader, loaderVersion) = ResolveLoaderFromDependencies(deps);

        var files = new List<MrpackFileEntry>();
        if (root["files"] is JArray fileArray)
        {
            foreach (var token in fileArray.OfType<JObject>())
            {
                var env = token["env"] as JObject;
                var client = env?["client"]?.ToString() ?? "required";
                if (string.Equals(client, "unsupported", StringComparison.OrdinalIgnoreCase))
                    continue;

                var path = token["path"]?.ToString() ?? "";
                var downloads = (token["downloads"] as JArray)?
                    .Select(d => d?.ToString())
                    .Where(d => !string.IsNullOrWhiteSpace(d))
                    .Cast<string>()
                    .ToList() ?? new List<string>();

                if (string.IsNullOrWhiteSpace(path) || downloads.Count == 0)
                    continue;

                files.Add(new MrpackFileEntry
                {
                    Path = path.Replace('/', System.IO.Path.DirectorySeparatorChar),
                    Downloads = downloads,
                    ClientRequired = !string.Equals(client, "optional", StringComparison.OrdinalIgnoreCase)
                });
            }
        }

        return new MrpackIndex
        {
            Metadata = new ParsedModpackMetadata
            {
                Name = root["name"]?.ToString() ?? "",
                MinecraftVersion = deps["minecraft"]?.ToString() ?? "",
                Loader = loader,
                LoaderVersion = loaderVersion
            },
            Files = files
        };
    }

    public static CurseForgeManifest ParseCurseForgeManifest(string json)
    {
        var root = JObject.Parse(json);
        var minecraft = root["minecraft"] as JObject ?? new JObject();
        var modLoaders = minecraft["modLoaders"] as JArray;
        var primary = modLoaders?
            .OfType<JObject>()
            .FirstOrDefault(m => m["primary"]?.Value<bool>() == true)
            ?? modLoaders?.OfType<JObject>().FirstOrDefault();

        var (loader, loaderVersion) = ParseCurseForgeLoaderId(primary?["id"]?.ToString() ?? "");

        var files = new List<CurseForgeFileEntry>();
        if (root["files"] is JArray fileArray)
        {
            foreach (var token in fileArray.OfType<JObject>())
            {
                var projectId = token["projectID"]?.Value<int>() ?? 0;
                var fileId = token["fileID"]?.Value<int>() ?? 0;
                if (projectId == 0 || fileId == 0)
                    continue;

                files.Add(new CurseForgeFileEntry
                {
                    ProjectId = projectId,
                    FileId = fileId,
                    Required = token["required"]?.Value<bool>() != false
                });
            }
        }

        return new CurseForgeManifest
        {
            Metadata = new ParsedModpackMetadata
            {
                Name = root["name"]?.ToString() ?? "",
                MinecraftVersion = minecraft["version"]?.ToString() ?? "",
                Loader = loader,
                LoaderVersion = loaderVersion
            },
            Files = files,
            OverridesFolder = root["overrides"]?.ToString() ?? "overrides"
        };
    }

    public static (string Loader, string LoaderVersion) ParseCurseForgeLoaderId(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return ("", "");

        var dash = id.IndexOf('-');
        if (dash <= 0 || dash >= id.Length - 1)
            return ("", id);

        var kind = id[..dash].ToLowerInvariant();
        var version = id[(dash + 1)..];

        return kind switch
        {
            "forge" => ("Forge", version),
            "fabric" => ("Fabric", version),
            "neoforge" => ("NeoForge", version),
            "quilt" => ("Quilt", version),
            _ => (kind, version)
        };
    }

    private static (string Loader, string LoaderVersion) ResolveLoaderFromDependencies(JObject deps)
    {
        if (deps["fabric-loader"] != null)
            return ("Fabric", deps["fabric-loader"]!.ToString());
        if (deps["quilt-loader"] != null)
            return ("Quilt", deps["quilt-loader"]!.ToString());
        if (deps["neoforge"] != null)
            return ("NeoForge", deps["neoforge"]!.ToString());
        if (deps["forge"] != null)
            return ("Forge", deps["forge"]!.ToString());
        return ("", "");
    }
}
