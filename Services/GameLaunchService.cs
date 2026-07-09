using System;
using System.Security.Cryptography;
using System.Text;

namespace Apeiron.Services;

public readonly record struct LaunchIdentity(string Username, string Uuid, string AccessToken, bool IsOffline);

public static class GameLaunchService
{
    public static LaunchIdentity CreateOfflineIdentity(string offlineUsername)
    {
        var username = OfflineUsernameHelper.Sanitize(offlineUsername);
        return new LaunchIdentity(username, GetOfflineUuid(username), "offline", true);
    }

    public static LaunchIdentity CreateOnlineIdentity(string username, string uuid, string accessToken) =>
        new LaunchIdentity(username, uuid, accessToken, false);

    public static string GetOfflineUuid(string username)
    {
        var bytes = MD5.HashData(Encoding.UTF8.GetBytes("OfflinePlayer:" + username));
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x30);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes).ToString();
    }
}
