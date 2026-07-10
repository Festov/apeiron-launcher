using System;
using System.Runtime.InteropServices;

namespace Apeiron.Services;

public static class SystemMemoryHelper
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private class MemoryStatusEx
    {
        public uint dwLength = (uint)Marshal.SizeOf<MemoryStatusEx>();
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx([In, Out] MemoryStatusEx lpBuffer);

    public static int GetRecommendedMaxRamGb()
    {
        try
        {
            var status = new MemoryStatusEx();
            if (!GlobalMemoryStatusEx(status))
                return 64;

            var totalGb = status.ullTotalPhys / 1024d / 1024 / 1024;
            return Math.Max(1, Math.Min(64, (int)Math.Floor(totalGb * 0.75)));
        }
        catch
        {
            return 64;
        }
    }
}
