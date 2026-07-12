using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Apeiron.Services;

public static class StructuredLogWriter
{
    public const string FileName = "launcher-events.jsonl";

    public static void Append(string logsDir, string eventName, string? message = null, IReadOnlyDictionary<string, object?>? data = null)
    {
        if (string.IsNullOrWhiteSpace(logsDir) || string.IsNullOrWhiteSpace(eventName))
            return;

        try
        {
            Directory.CreateDirectory(logsDir);
            var entry = new Dictionary<string, object?>
            {
                ["ts"] = DateTime.UtcNow.ToString("o"),
                ["event"] = eventName
            };

            if (!string.IsNullOrWhiteSpace(message))
                entry["message"] = message;

            if (data != null)
            {
                foreach (var pair in data)
                    entry[pair.Key] = pair.Value;
            }

            var line = JsonSerializer.Serialize(entry);
            File.AppendAllText(Path.Combine(logsDir, FileName), line + Environment.NewLine);
        }
        catch
        {
            // Logging must not break launcher flow.
        }
    }
}
