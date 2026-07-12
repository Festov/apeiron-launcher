using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Apeiron.Services;

public static class HttpRetryHelper
{
    public const int DefaultMaxRetries = 3;

    public static bool IsTransientStatusCode(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.RequestTimeout
            or HttpStatusCode.TooManyRequests
            or HttpStatusCode.InternalServerError
            or HttpStatusCode.BadGateway
            or HttpStatusCode.ServiceUnavailable
            or HttpStatusCode.GatewayTimeout;

    public static bool IsTransientException(Exception ex) =>
        ex is HttpRequestException or TaskCanceledException { CancellationToken.IsCancellationRequested: false };

    public static TimeSpan GetBackoff(int attempt) =>
        TimeSpan.FromMilliseconds(1000 * Math.Max(1, attempt));

    public static async Task<string> GetStringAsync(
        HttpClient client,
        string url,
        CancellationToken cancellationToken = default,
        int maxRetries = DefaultMaxRetries)
    {
        for (var attempt = 0; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                using var response = await client.GetAsync(url, cancellationToken);
                if (IsTransientStatusCode(response.StatusCode) && attempt < maxRetries)
                {
                    await Task.Delay(GetBackoff(attempt + 1), cancellationToken);
                    continue;
                }

                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsStringAsync(cancellationToken);
            }
            catch (Exception ex) when (attempt < maxRetries && ex is not OperationCanceledException && IsTransientException(ex))
            {
                await Task.Delay(GetBackoff(attempt + 1), cancellationToken);
            }
        }
    }
}
