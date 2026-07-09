using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace Apeiron.Services;

public class FabricApiService
{
    public event Action<string>? Log;

    public async Task<bool> InstallAsync(BuildInfo build, CancellationToken cancellationToken = default)
    {
        if (!build.InstallFabricApi ||
            !build.Loader.Equals("Fabric", StringComparison.OrdinalIgnoreCase))
            return true;

        var modsDir = build.GetModsDir();
        Directory.CreateDirectory(modsDir);

        if (Directory.GetFiles(modsDir, "fabric-api*.jar").Length > 0 ||
            Directory.GetFiles(modsDir, "fabric-api*.jar.disabled").Length > 0)
        {
            Log?.Invoke(LocalizationService.T("log.fabric.already_installed"));
            return true;
        }

        try
        {
            Log?.Invoke(LocalizationService.T("log.fabric.downloading"));
            var url = $"https://api.modrinth.com/v2/project/fabric-api/version" +
                      $"?game_versions=[\"{build.MinecraftVersion}\"]&loaders=[\"fabric\"]";

            var json = JArray.Parse(await AppHttp.Client.GetStringAsync(url, cancellationToken));
            if (json.Count == 0)
            {
                Log?.Invoke(LocalizationService.T("log.fabric.not_found"));
                return true;
            }

            var version = json[0] as JObject;
            var files = version?["files"] as JArray;
            var primary = files?
                .Select(f => f as JObject)
                .FirstOrDefault(f => f?["primary"]?.Value<bool>() == true)
                ?? files?[0] as JObject;

            var downloadUrl = primary?["url"]?.ToString();
            var fileName = primary?["filename"]?.ToString();

            if (string.IsNullOrEmpty(downloadUrl) || string.IsNullOrEmpty(fileName))
            {
                Log?.Invoke(LocalizationService.T("log.fabric.url_not_found"));
                return true;
            }

            var dest = Path.Combine(modsDir, fileName);
            await using var stream = await AppHttp.Client.GetStreamAsync(downloadUrl, cancellationToken);
            await using var file = File.Create(dest);
            await stream.CopyToAsync(file);

            Log?.Invoke(LocalizationService.F("log.fabric.installed", fileName));
            return true;
        }
        catch (Exception ex)
        {
            Log?.Invoke(LocalizationService.F("log.fabric.error", ex.Message));
            return true;
        }
    }
}
