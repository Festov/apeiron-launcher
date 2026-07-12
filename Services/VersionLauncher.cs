using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ICSharpCode.SharpZipLib.Zip;
using Newtonsoft.Json.Linq;

namespace Apeiron.Services;

public class VersionLauncher
{
    private sealed class LaunchFeatures
    {
        public bool IsDemoUser { get; set; }
        public bool HasCustomResolution { get; set; }
        public bool HasQuickPlaysSupport { get; set; }
        public bool IsQuickPlaySingleplayer { get; set; }
        public bool IsQuickPlayMultiplayer { get; set; }
        public bool IsQuickPlayRealms { get; set; }
    }

    private readonly string _minecraftDir;
    private readonly string _versionsDir;
    private readonly string _librariesDir;
    private readonly string _assetsDir;

    public event Action<string>? Log;

    public VersionLauncher(string minecraftDir)
    {
        _minecraftDir = minecraftDir;
        _versionsDir = Path.Combine(_minecraftDir, "versions");
        _librariesDir = Path.Combine(_minecraftDir, "libraries");
        _assetsDir = Path.Combine(_minecraftDir, "assets");
    }

    public async Task<Process?> LaunchAsync(
        BuildInfo build,
        string username,
        string uuid,
        string accessToken,
        int ramGb,
        string? javaPath = null)
    {
        try
        {
            var versionId = build.GetVersionId();
            var gameDir = build.GetGameDir();
            Directory.CreateDirectory(gameDir);

            var merged = await LoadMergedVersionAsync(versionId);
            if (!HasRequiredLibraries(merged))
                await EnsureLibrariesAsync(merged);

            var nativesDir = Path.Combine(gameDir, "natives");
            if (Directory.Exists(nativesDir))
                Directory.Delete(nativesDir, true);
            Directory.CreateDirectory(nativesDir);

            var classpath = BuildClasspath(merged, versionId, nativesDir);
            if (string.IsNullOrEmpty(classpath))
            {
                Log?.Invoke(LocalizationService.T("log.launch.classpath_failed"));
                return null;
            }

            javaPath ??= new JavaService().ResolveJavaPath(build.MinecraftVersion);
            if (string.IsNullOrEmpty(javaPath))
            {
                Log?.Invoke(LocalizationService.T("log.launch.java_not_found"));
                return null;
            }

            var mainClass = merged["mainClass"]?.ToString();
            if (string.IsNullOrEmpty(mainClass))
            {
                Log?.Invoke(LocalizationService.T("log.launch.main_class_not_found"));
                return null;
            }

            var assetIndexId = merged["assetIndex"]?["id"]?.ToString() ?? build.MinecraftVersion;
            var ramMin = Math.Max(1, ramGb / 2);
            var hasResolution = build.ResolutionWidth > 0 && build.ResolutionHeight > 0;
            var features = new LaunchFeatures { HasCustomResolution = hasResolution };

            var substitutionVars = new Dictionary<string, string>
            {
                ["${natives_directory}"] = nativesDir,
                ["${launcher_name}"] = "Apeiron",
                ["${version_name}"] = versionId,
                ["${library_directory}"] = _librariesDir,
                ["${classpath}"] = classpath,
                ["${auth_player_name}"] = username,
                ["${auth_uuid}"] = FormatUuid(uuid),
                ["${auth_access_token}"] = accessToken,
                ["${version_type}"] = merged["type"]?.ToString() ?? "release",
                ["${game_directory}"] = gameDir,
                ["${assets_root}"] = _assetsDir,
                ["${assets_index_name}"] = assetIndexId,
                ["${user_type}"] = accessToken == "offline" ? "legacy" : "msa",
                ["${clientid}"] = "",
                ["${auth_xuid}"] = "",
                ["${resolution_width}"] = hasResolution ? build.ResolutionWidth.ToString() : "",
                ["${resolution_height}"] = hasResolution ? build.ResolutionHeight.ToString() : "",
                ["${quickPlayPath}"] = "",
                ["${quickPlaySingleplayer}"] = "",
                ["${quickPlayMultiplayer}"] = "",
                ["${quickPlayRealms}"] = ""
            };

            var processArgs = new List<string>
            {
                $"-Xmx{ramGb}G",
                $"-Xms{ramMin}G",
                $"-Djava.library.path={nativesDir}"
            };

            var arguments = merged["arguments"] as JObject;
            var jvmArgsToken = arguments?["jvm"] ?? arguments?["default-user-jvm"];
            AppendArguments(processArgs, jvmArgsToken, substitutionVars, features, skipMemoryFlags: true);
            AppendCustomJvmArgs(processArgs, build.JvmArgs);

            var javaMajor = JavaService.DetectJavaMajor(javaPath);
            FilterUnsupportedJvmArgs(processArgs, javaMajor);
            Log?.Invoke(LocalizationService.F("log.launch.using_java", javaPath, javaMajor));

            if (!processArgs.Contains("-cp"))
            {
                processArgs.Add("-cp");
                processArgs.Add(classpath);
            }

            processArgs.Add(mainClass);

            var hasGameArgs = HasGameArguments(arguments?["game"]);
            if (hasGameArgs)
            {
                AppendArguments(processArgs, arguments?["game"], substitutionVars, features);
            }
            else
            {
                processArgs.AddRange(new[]
                {
                    "--username", username,
                    "--version", versionId,
                    "--gameDir", gameDir,
                    "--assetsDir", _assetsDir,
                    "--assetIndex", assetIndexId,
                    "--uuid", FormatUuid(uuid),
                    "--accessToken", accessToken,
                    "--userType", accessToken == "offline" ? "legacy" : "msa"
                });
            }

            if (build.Fullscreen)
                processArgs.Add("--fullscreen");

            Log?.Invoke(LocalizationService.F("log.launch.starting", versionId));

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = javaPath,
                    WorkingDirectory = gameDir,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                }
            };

            if (javaMajor >= 9)
            {
                var argFile = WriteLaunchArgFile(processArgs);
                process.StartInfo.ArgumentList.Add("@" + argFile);
                ScheduleDeleteLaunchArgFile(argFile);
            }
            else
            {
                foreach (var arg in processArgs)
                    process.StartInfo.ArgumentList.Add(arg);
            }

            process.OutputDataReceived += (_, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data)) Log?.Invoke($"[MC] {e.Data}");
            };
            process.ErrorDataReceived += (_, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data)) Log?.Invoke($"[MC] {e.Data}");
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            Log?.Invoke(LocalizationService.F("log.launch.started", build.DisplayName));
            return process;
        }
        catch (Exception ex)
        {
            Log?.Invoke(LocalizationService.F("log.launch.error", ex.Message));
            return null;
        }
    }

    private static bool HasGameArguments(JToken? gameArgs)
    {
        if (gameArgs is JArray arr)
            return arr.Count > 0;
        return false;
    }

    private async Task<JObject> LoadMergedVersionAsync(string versionId)
    {
        var versionJsonPath = Path.Combine(_versionsDir, versionId, $"{versionId}.json");
        if (!File.Exists(versionJsonPath))
            throw new FileNotFoundException(LocalizationService.F("log.launch.version_profile_not_found", versionId));

        var current = JObject.Parse(await File.ReadAllTextAsync(versionJsonPath));
        var inheritsFrom = current["inheritsFrom"]?.ToString();

        if (string.IsNullOrEmpty(inheritsFrom))
            return current;

        var parent = await LoadMergedVersionAsync(inheritsFrom);
        return MergeVersionJson(parent, current);
    }

    private static JObject MergeVersionJson(JObject parent, JObject child)
    {
        var merged = (JObject)parent.DeepClone();

        foreach (var prop in child.Properties())
        {
            if (prop.Name == "libraries")
            {
                MergeArrays(merged, child, "libraries");
            }
            else if (prop.Name == "arguments" && prop.Value is JObject childArgs)
            {
                var parentArgs = merged["arguments"] as JObject ?? new JObject();
                foreach (var argProp in childArgs.Properties())
                {
                    if (parentArgs[argProp.Name] is JArray existing && argProp.Value is JArray incoming)
                    {
                        foreach (var item in incoming)
                            existing.Add(item.DeepClone());
                    }
                    else
                    {
                        parentArgs[argProp.Name] = argProp.Value.DeepClone();
                    }
                }
                merged["arguments"] = parentArgs;
            }
            else
            {
                merged[prop.Name] = prop.Value?.DeepClone();
            }
        }

        return merged;
    }

    private static void MergeArrays(JObject target, JObject source, string key)
    {
        var targetArr = target[key] as JArray ?? new JArray();
        var sourceArr = source[key] as JArray;
        if (sourceArr == null) return;

        foreach (var item in sourceArr)
            targetArr.Add(item.DeepClone());

        target[key] = targetArr;
    }

    private async Task EnsureLibrariesAsync(JObject versionData)
    {
        var libraries = versionData["libraries"] as JArray;
        if (libraries == null) return;

        foreach (var lib in libraries)
        {
            if (!ShouldIncludeLibrary(lib as JObject))
                continue;

            await LibraryHelper.DownloadFromVersionLibraryAsync(
                lib,
                _librariesDir,
                async (url, path, sha1, ct) =>
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    await DownloadFileAsync(url, path, ct);
                    if (!LibraryHelper.VerifySha1(path, sha1))
                    {
                        if (File.Exists(path))
                            File.Delete(path);
                        await DownloadFileAsync(url, path, ct);
                        if (!LibraryHelper.VerifySha1(path, sha1))
                            throw new IOException(LocalizationService.F("log.launch.library_verify_failed", Path.GetFileName(path)));
                    }
                });

            var name = lib?["name"]?.ToString();
            if (string.IsNullOrEmpty(name)) continue;

            var natives = lib?["natives"] as JObject;
            if (natives != null)
            {
                var classifiers = lib?["downloads"]?["classifiers"] as JObject;
                var nativeKey = natives["windows"]?.ToString()?.Replace("${arch}", "64");
                if (!string.IsNullOrEmpty(nativeKey) && classifiers?[nativeKey] != null)
                {
                    var nativeUrl = classifiers[nativeKey]?["url"]?.ToString();
                    var nativeSha1 = classifiers[nativeKey]?["sha1"]?.ToString();
                    var nativePath = GetNativeLibraryPath(name, nativeKey);
                    if (!string.IsNullOrEmpty(nativeUrl) && !File.Exists(nativePath))
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(nativePath)!);
                        await DownloadFileAsync(nativeUrl, nativePath);
                        if (!LibraryHelper.VerifySha1(nativePath, nativeSha1))
                            throw new IOException(LocalizationService.F("log.launch.library_verify_failed", Path.GetFileName(nativePath)));
                    }
                }
            }
        }
    }

    private bool HasRequiredLibraries(JObject versionData)
    {
        var libraries = versionData["libraries"] as JArray;
        if (libraries == null)
            return true;

        foreach (var lib in libraries)
        {
            if (!ShouldIncludeLibrary(lib as JObject))
                continue;

            var name = lib?["name"]?.ToString();
            if (string.IsNullOrEmpty(name))
                continue;

            var path = LibraryHelper.GetJarPath(name, _librariesDir);
            var sha1 = lib?["downloads"]?["artifact"]?["sha1"]?.ToString();
            if (!File.Exists(path) || !LibraryHelper.VerifySha1(path, sha1))
                return false;
        }

        return true;
    }

    private string BuildClasspath(JObject versionData, string versionId, string nativesDir)
    {
        var jars = new List<string>();
        var libraries = versionData["libraries"] as JArray;

        if (libraries != null)
        {
            foreach (var lib in libraries)
            {
                if (!ShouldIncludeLibrary(lib as JObject))
                    continue;

                var name = lib?["name"]?.ToString();
                if (string.IsNullOrEmpty(name)) continue;

                var libPath = LibraryHelper.GetJarPath(name, _librariesDir);
                if (File.Exists(libPath))
                    jars.Add(libPath);

                ExtractNatives(lib as JObject, nativesDir);
            }
        }

        var versionJar = Path.Combine(_versionsDir, versionId, $"{versionId}.jar");
        if (File.Exists(versionJar))
            jars.Add(versionJar);
        else
        {
            var inheritsFrom = versionData["inheritsFrom"]?.ToString();
            if (!string.IsNullOrEmpty(inheritsFrom))
            {
                var parentJar = Path.Combine(_versionsDir, inheritsFrom, $"{inheritsFrom}.jar");
                if (File.Exists(parentJar))
                    jars.Add(parentJar);
            }
        }

        return string.Join(";", AppendLwjglUnsafeFallback(jars).Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private static List<string> AppendLwjglUnsafeFallback(List<string> jars)
    {
        foreach (var jar in jars.ToList())
        {
            var fileName = Path.GetFileName(jar);
            if (!fileName.Equals("lwjgl-3.4.1.jar", StringComparison.OrdinalIgnoreCase))
                continue;

            var unsafeJar = Path.Combine(Path.GetDirectoryName(jar)!, "lwjgl-3.4.1-unsafe.jar");
            if (File.Exists(unsafeJar) &&
                !jars.Any(j => j.Equals(unsafeJar, StringComparison.OrdinalIgnoreCase)))
            {
                jars.Add(unsafeJar);
            }
        }

        return jars;
    }

    private void ExtractNatives(JObject? lib, string nativesDir)
    {
        if (lib == null) return;

        var natives = lib["natives"] as JObject;
        if (natives == null) return;

        var name = lib["name"]?.ToString();
        if (string.IsNullOrEmpty(name)) return;

        var nativeKey = natives["windows"]?.ToString()?.Replace("${arch}", "64");
        if (string.IsNullOrEmpty(nativeKey)) return;

        var nativePath = GetNativeLibraryPath(name, nativeKey);
        if (!File.Exists(nativePath)) return;

        try
        {
            using var zip = new ZipFile(nativePath);
            foreach (ZipEntry entry in zip)
            {
                if (!entry.IsFile) continue;
                var fileName = Path.GetFileName(entry.Name);
                if (!fileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) &&
                    !fileName.EndsWith(".sha1", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (fileName.EndsWith(".sha1")) continue;

                var outputPath = Path.Combine(nativesDir, fileName);
                using var zipStream = zip.GetInputStream(entry);
                using var fileStream = File.Create(outputPath);
                zipStream.CopyTo(fileStream);
            }
        }
        catch (Exception ex)
        {
            Log?.Invoke(LocalizationService.F("log.launch.natives_error", Path.GetFileName(nativePath), ex.Message));
        }
    }

    private static bool ShouldIncludeLibrary(JObject? lib)
    {
        if (lib == null) return false;
        var rules = lib["rules"] as JArray;
        if (rules == null) return true;
        return EvaluateRules(rules);
    }

    private static string GetLibraryPath(string mavenName, string librariesDir)
    {
        var parts = mavenName.Split(':');
        if (parts.Length < 3) return "";

        var group = parts[0].Replace('.', Path.DirectorySeparatorChar);
        var artifact = parts[1];
        var version = parts[2];
        var classifier = parts.Length > 3 ? $"-{parts[3]}" : "";

        return Path.Combine(
            librariesDir,
            group, artifact, version,
            $"{artifact}-{version}{classifier}.jar");
    }

    private string GetNativeLibraryPath(string mavenName, string classifier)
    {
        var parts = mavenName.Split(':');
        if (parts.Length < 3) return "";

        var group = parts[0].Replace('.', Path.DirectorySeparatorChar);
        var artifact = parts[1];
        var version = parts[2];

        return Path.Combine(
            _librariesDir,
            group, artifact, version,
            $"{artifact}-{version}-{classifier}.jar");
    }

    private void AppendArguments(
        List<string> target,
        JToken? argsToken,
        Dictionary<string, string> vars,
        LaunchFeatures? features = null,
        bool skipMemoryFlags = false)
    {
        if (argsToken is not JArray arr) return;
        features ??= new LaunchFeatures();

        foreach (var entry in arr)
        {
            if (entry is JObject ruleObj)
            {
                var rules = ruleObj["rules"] as JArray;
                if (rules != null && !EvaluateRules(rules, features))
                    continue;

                if (ruleObj["value"] is JArray values)
                    AppendArgumentValues(target, values, vars, skipMemoryFlags);
                else if (ruleObj["value"] is JValue single)
                    AppendArgumentValues(target, new JArray(single), vars, skipMemoryFlags);
            }
            else if (entry is JValue val)
            {
                AddResolvedArgument(target, Substitute(val.ToString() ?? "", vars), skipMemoryFlags);
            }
        }
    }

    private void AppendArgumentValues(
        List<string> target,
        JArray values,
        Dictionary<string, string> vars,
        bool skipMemoryFlags)
    {
        for (var i = 0; i < values.Count; i++)
        {
            var str = Substitute(values[i]?.ToString() ?? "", vars);
            if (!IsValidArgument(str))
                continue;

            if (skipMemoryFlags && IsMemoryFlag(str))
                continue;

            if (str.StartsWith("--") && i + 1 < values.Count)
            {
                var next = Substitute(values[i + 1]?.ToString() ?? "", vars);
                if (!IsValidArgument(next))
                {
                    i++;
                    continue;
                }
            }

            AddResolvedArgument(target, str, skipMemoryFlags);
        }
    }

    private static void AppendCustomJvmArgs(List<string> target, string? jvmArgs)
    {
        if (string.IsNullOrWhiteSpace(jvmArgs)) return;

        foreach (var part in jvmArgs.Split(new[] { '\r', '\n', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var arg = part.Trim();
            if (string.IsNullOrEmpty(arg)) continue;
            if (IsMemoryFlag(arg)) continue;
            target.Add(arg);
        }
    }

    private static void AddResolvedArgument(List<string> target, string str, bool skipMemoryFlags)
    {
        if (!IsValidArgument(str)) return;
        if (skipMemoryFlags && IsMemoryFlag(str)) return;
        target.Add(str);
    }

    private static bool IsValidArgument(string str)
    {
        if (string.IsNullOrWhiteSpace(str)) return false;
        if (str.Contains("${", StringComparison.Ordinal)) return false;
        return true;
    }

    private static bool IsMemoryFlag(string str) =>
        str.StartsWith("-Xmx", StringComparison.OrdinalIgnoreCase) ||
        str.StartsWith("-Xms", StringComparison.OrdinalIgnoreCase);

    private static void FilterUnsupportedJvmArgs(List<string> args, int javaMajor)
    {
        if (JavaVersionHelper.SupportsSunMiscUnsafeAccess(javaMajor))
            return;

        for (var i = args.Count - 1; i >= 0; i--)
        {
            if (args[i].StartsWith("--sun-misc-unsafe-memory-access", StringComparison.Ordinal))
                args.RemoveAt(i);
        }
    }

    private static bool EvaluateRules(JArray rules, LaunchFeatures? features = null)
    {
        features ??= new LaunchFeatures();
        var allow = false;

        foreach (var rule in rules)
        {
            if (!RuleMatches(rule as JObject, features))
                continue;

            var action = rule["action"]?.ToString() ?? "allow";
            allow = action == "allow";
        }

        return allow;
    }

    private static bool RuleMatches(JObject? rule, LaunchFeatures features)
    {
        if (rule == null) return true;

        var os = rule["os"] as JObject;
        if (os != null && !OsRuleMatches(os))
            return false;

        var featuresObj = rule["features"] as JObject;
        if (featuresObj != null)
        {
            foreach (var prop in featuresObj.Properties())
            {
                var required = prop.Value.Type == JTokenType.Boolean && prop.Value.Value<bool>();
                var actual = GetFeatureValue(features, prop.Name);
                if (actual != required)
                    return false;
            }
        }

        return true;
    }

    private static bool OsRuleMatches(JObject osRule)
    {
        if (!OperatingSystem.IsWindows())
            return false;

        var name = osRule["name"]?.ToString();
        if (name != null && !name.Equals("windows", StringComparison.OrdinalIgnoreCase))
            return false;

        var versionRange = osRule["versionRange"] as JObject;
        if (versionRange == null)
            return true;

        var build = Environment.OSVersion.Version.Build;
        if (versionRange["min"]?.ToString() is string minStr &&
            int.TryParse(minStr.Split('.').LastOrDefault(), out var minBuild) &&
            build < minBuild)
            return false;

        if (versionRange["max"]?.ToString() is string maxStr &&
            int.TryParse(maxStr.Split('.').LastOrDefault(), out var maxBuild) &&
            build > maxBuild)
            return false;

        return true;
    }

    private static bool GetFeatureValue(LaunchFeatures features, string name) => name switch
    {
        "is_demo_user" => features.IsDemoUser,
        "has_custom_resolution" => features.HasCustomResolution,
        "has_quick_plays_support" => features.HasQuickPlaysSupport,
        "is_quick_play_singleplayer" => features.IsQuickPlaySingleplayer,
        "is_quick_play_multiplayer" => features.IsQuickPlayMultiplayer,
        "is_quick_play_realms" => features.IsQuickPlayRealms,
        _ => false
    };

    private static string Substitute(string input, Dictionary<string, string> vars)
    {
        var result = input;
        foreach (var (key, value) in vars)
            result = result.Replace(key, value);
        return result;
    }

    private static string FormatUuid(string uuid)
    {
        if (uuid.Contains('-')) return uuid;
        if (uuid.Length != 32) return uuid;
        return $"{uuid[..8]}-{uuid[8..12]}-{uuid[12..16]}-{uuid[16..20]}-{uuid[20..]}";
    }

    private static async Task DownloadFileAsync(string url, string path, CancellationToken cancellationToken = default)
    {
        using var response = await AppHttp.Client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var file = File.Create(path);
        await stream.CopyToAsync(file, cancellationToken);
    }

    private static string WriteLaunchArgFile(IReadOnlyList<string> args)
    {
        var argFilePath = Path.Combine(Path.GetTempPath(), "apeiron-launch-" + Guid.NewGuid().ToString("N") + ".args");
        var lines = new List<string>(args.Count);

        foreach (var arg in args)
        {
            if (string.IsNullOrEmpty(arg)) continue;
            lines.Add(FormatArgFileValue(arg));
        }

        File.WriteAllText(argFilePath, string.Join(Environment.NewLine, lines), new UTF8Encoding(false));
        return argFilePath;
    }

    private static void ScheduleDeleteLaunchArgFile(string path)
    {
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromMinutes(2));
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        });
    }

    private static string FormatArgFileValue(string arg)
    {
        if (arg.IndexOfAny(new[] { ' ', '\t', '"' }) >= 0 || arg.Contains(';'))
            return "\"" + arg.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

        return arg;
    }
}
