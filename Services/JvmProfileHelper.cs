using System;
using System.Collections.Generic;

namespace Apeiron.Services;

public static class JvmProfileHelper
{
    public const string Default = "default";
    public const string Performance = "performance";
    public const string LowLatency = "low_latency";
    public const string Debug = "debug";

    public static IReadOnlyList<(string Id, string LocKey)> Profiles { get; } =
    [
        (Default, "jvm.profile.default"),
        (Performance, "jvm.profile.performance"),
        (LowLatency, "jvm.profile.low_latency"),
        (Debug, "jvm.profile.debug")
    ];

    public static string GetArgs(string? profileId) =>
        (profileId ?? Default).Trim().ToLowerInvariant() switch
        {
            Performance =>
                "-XX:+UseG1GC -XX:+ParallelRefProcEnabled -XX:MaxGCPauseMillis=200 -XX:+UnlockExperimentalVMOptions -XX:+DisableExplicitGC -XX:+AlwaysPreTouch -XX:G1NewSizePercent=30 -XX:G1MaxNewSizePercent=40 -XX:G1HeapRegionSize=8M -XX:G1ReservePercent=20 -XX:InitiatingHeapOccupancyPercent=15",
            LowLatency =>
                "-XX:+UseG1GC -XX:MaxGCPauseMillis=50 -XX:+UnlockExperimentalVMOptions -XX:+DisableExplicitGC -XX:G1HeapRegionSize=4M",
            Debug =>
                "-XX:+HeapDumpOnOutOfMemoryError -XX:HeapDumpPath=./heapdump.hprof -Xlog:gc*:file=gc.log:time,uptime:filecount=3,filesize=8M",
            _ => ""
        };

    public static string MergeWithCustom(string? profileId, string? customJvmArgs)
    {
        var profile = GetArgs(profileId);
        var custom = customJvmArgs?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(profile))
            return custom;
        if (string.IsNullOrWhiteSpace(custom))
            return profile;
        return $"{profile} {custom}";
    }
}
