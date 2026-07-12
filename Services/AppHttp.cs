using System;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Reflection;
using System.Security.Authentication;

namespace Apeiron.Services;

public static class AppHttp
{
    public static string UserAgent { get; } = CreateUserAgent();

    public static HttpClient Client { get; } = CreateClient();

    private static string CreateUserAgent()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        var label = version != null ? $"{version.Major}.{version.Minor}.{version.Build}" : "1.0";
        return $"Apeiron/{label}";
    }

    private static HttpClient CreateClient()
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 15,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            ConnectTimeout = TimeSpan.FromSeconds(45),
            UseProxy = true,
            DefaultProxyCredentials = CredentialCache.DefaultCredentials,
            SslOptions = new SslClientAuthenticationOptions
            {
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13
            }
        };

        var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromMinutes(30)
        };
        client.DefaultRequestHeaders.Add("User-Agent", UserAgent);
        return client;
    }
}
