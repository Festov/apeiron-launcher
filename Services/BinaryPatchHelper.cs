using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace Apeiron.Services;

/// <summary>
/// Simple binary delta format "APEIRON1" for launcher self-updates.
/// Commands: COPY from old file, or DATA insert raw bytes. Payload is zlib-compressed.
/// </summary>
public static class BinaryPatchHelper
{
    public static readonly byte[] Magic = Encoding.ASCII.GetBytes("APEIRON1");
    private const int BlockSize = 4096;

    public static void CreatePatch(string oldFile, string newFile, string patchFile)
    {
        var oldBytes = File.ReadAllBytes(oldFile);
        var newBytes = File.ReadAllBytes(newFile);
        var oldSha = SHA256.HashData(oldBytes);
        var newSha = SHA256.HashData(newBytes);

        using var raw = new MemoryStream();
        using (var writer = new BinaryWriter(raw, Encoding.UTF8, leaveOpen: true))
        {
            var oldIndex = IndexBlocks(oldBytes);
            var offset = 0;
            while (offset < newBytes.Length)
            {
                var len = Math.Min(BlockSize, newBytes.Length - offset);
                var slice = new byte[len];
                Buffer.BlockCopy(newBytes, offset, slice, 0, len);
                var key = Convert.ToHexString(SHA256.HashData(slice));

                if (oldIndex.TryGetValue(key, out var oldOff) && oldOff + len <= oldBytes.Length)
                {
                    writer.Write((byte)0); // COPY
                    writer.Write(oldOff);
                    writer.Write(len);
                }
                else
                {
                    writer.Write((byte)1); // DATA
                    writer.Write(len);
                    writer.Write(slice);
                }

                offset += len;
            }
        }

        var compressed = Compress(raw.ToArray());
        using var output = File.Create(patchFile);
        output.Write(Magic);
        output.Write(oldSha);
        output.Write(newSha);
        output.Write(compressed);
    }

    public static void ApplyPatch(string oldFile, string patchFile, string newFile)
    {
        var oldBytes = File.ReadAllBytes(oldFile);
        var patchBytes = File.ReadAllBytes(patchFile);
        if (patchBytes.Length < Magic.Length + 64)
            throw new InvalidDataException("Patch file is too small.");

        for (var i = 0; i < Magic.Length; i++)
        {
            if (patchBytes[i] != Magic[i])
                throw new InvalidDataException("Not an Apeiron patch file.");
        }

        var oldShaExpected = patchBytes.AsSpan(8, 32);
        var newShaExpected = patchBytes.AsSpan(40, 32);
        var oldSha = SHA256.HashData(oldBytes);
        if (!oldSha.AsSpan().SequenceEqual(oldShaExpected))
            throw new InvalidDataException("Patch does not match the current launcher executable.");

        var compressed = patchBytes.AsSpan(72).ToArray();
        var commands = Decompress(compressed);

        using var result = new MemoryStream();
        using var reader = new BinaryReader(new MemoryStream(commands));
        while (reader.BaseStream.Position < reader.BaseStream.Length)
        {
            var op = reader.ReadByte();
            if (op == 0)
            {
                var off = reader.ReadInt32();
                var len = reader.ReadInt32();
                if (off < 0 || len < 0 || off + len > oldBytes.Length)
                    throw new InvalidDataException("Invalid COPY in patch.");
                result.Write(oldBytes, off, len);
            }
            else if (op == 1)
            {
                var len = reader.ReadInt32();
                var data = reader.ReadBytes(len);
                if (data.Length != len)
                    throw new InvalidDataException("Truncated DATA in patch.");
                result.Write(data, 0, data.Length);
            }
            else
            {
                throw new InvalidDataException("Unknown patch opcode.");
            }
        }

        var newBytes = result.ToArray();
        var newSha = SHA256.HashData(newBytes);
        if (!newSha.AsSpan().SequenceEqual(newShaExpected))
            throw new InvalidDataException("Patched file failed integrity check.");

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(newFile))!);
        File.WriteAllBytes(newFile, newBytes);
    }

    private static Dictionary<string, int> IndexBlocks(byte[] data)
    {
        var map = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var offset = 0; offset < data.Length; offset += BlockSize)
        {
            var len = Math.Min(BlockSize, data.Length - offset);
            var slice = new byte[len];
            Buffer.BlockCopy(data, offset, slice, 0, len);
            var key = Convert.ToHexString(SHA256.HashData(slice));
            map.TryAdd(key, offset);
        }

        return map;
    }

    private static byte[] Compress(byte[] data)
    {
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
            zlib.Write(data, 0, data.Length);
        return output.ToArray();
    }

    private static byte[] Decompress(byte[] data)
    {
        using var input = new MemoryStream(data);
        using var zlib = new ZLibStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        zlib.CopyTo(output);
        return output.ToArray();
    }
}
