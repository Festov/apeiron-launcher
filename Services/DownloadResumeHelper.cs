using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;

namespace Apeiron.Services;

public static class DownloadResumeHelper
{
    public static long GetExistingBytes(string path) =>
        File.Exists(path) ? new FileInfo(path).Length : 0;

    public static HttpRequestMessage CreateRequest(string url, long resumeFrom)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (resumeFrom > 0)
            request.Headers.Range = new RangeHeaderValue(resumeFrom, null);
        return request;
    }

    public static bool ShouldAppendToExistingFile(HttpStatusCode statusCode, long existingBytes) =>
        existingBytes > 0 && statusCode == HttpStatusCode.PartialContent;

    public static bool ShouldRestartDownload(HttpStatusCode statusCode, long existingBytes) =>
        existingBytes > 0 && statusCode == HttpStatusCode.OK;

    public static long? ParseContentRangeTotal(string? header)
    {
        if (string.IsNullOrWhiteSpace(header))
            return null;

        var slash = header.LastIndexOf('/');
        if (slash < 0 || slash >= header.Length - 1)
            return null;

        var totalPart = header[(slash + 1)..].Trim();
        if (totalPart == "*")
            return null;

        return long.TryParse(totalPart, out var total) ? total : null;
    }

    public static long ResolveTotalBytes(HttpResponseMessage response, long existingBytes, long downloadedInSession)
    {
        var rangeTotal = ParseContentRangeTotal(response.Content.Headers.ContentRange?.ToString());
        if (rangeTotal.HasValue)
            return rangeTotal.Value;

        var contentLength = response.Content.Headers.ContentLength;
        if (contentLength.HasValue)
        {
            if (ShouldAppendToExistingFile(response.StatusCode, existingBytes))
                return existingBytes + contentLength.Value;

            return contentLength.Value;
        }

        return existingBytes + downloadedInSession;
    }
}
