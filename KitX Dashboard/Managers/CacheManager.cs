using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace KitX.Dashboard.Managers;

internal class CacheManager
{
    internal CacheManager()
    {
        FilesCache = [];
        ReceivingFilesCache = [];
    }

    private static async Task<string?> GetMD5(byte[] bytes, bool trans = false)
    {
        byte[]? result = null;

        await Task.Run(() =>
        {
            result = MD5.HashData(bytes);
        });

        if (result is null)
            return null;

        var sb = new StringBuilder();

        if (trans) foreach (var bt in result) sb.Append(bt.ToString("x2"));
        else sb.Append(Encoding.UTF8.GetString(result));

        return sb.ToString();
    }

    public async Task<string?> LoadFileToCache(string fileLocation, CancellationToken token = default)
    {
        var fullPath = Path.GetFullPath(fileLocation);
        var bin = await File.ReadAllBytesAsync(fullPath, token);

        if (bin is null) return null;

        var id = await GetMD5(bin, true);

        if (token.IsCancellationRequested || FilesCache is null || id is null)
            return null;

        if (!FilesCache.ContainsKey(id))
            FilesCache?.Add(id, bin);

        GC.Collect();

        return id;
    }

    public async Task<string?> ReceiveFileToCache(byte[] bin, CancellationToken token = default)
    {
        var id = await GetMD5(bin, true);

        if (token.IsCancellationRequested) return null;

        if (ReceivingFilesCache is null || id is null) return null;

        _ = ReceivingFilesCache.TryAdd(id, bin);

        GC.Collect();

        return id;
    }

    public byte[]? GetReceivedFileFromCache(string id)
    {
        if (ReceivingFilesCache is null) return null;

        if (ReceivingFilesCache.TryGetValue(id, out byte[]? bin))
            return bin;
        else return null;
    }

    public bool? DisposeFileCache(string id)
    {
        if (FilesCache is null) return null;

        if (FilesCache.Remove(id))
        {
            GC.Collect();

            return true;
        }

        return false;
    }

    private readonly Dictionary<string, byte[]>? FilesCache;

    private readonly Dictionary<string, byte[]>? ReceivingFilesCache;

}
