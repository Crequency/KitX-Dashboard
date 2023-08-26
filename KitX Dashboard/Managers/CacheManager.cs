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
        FilesCache = new();
        ReceivingFilesCache = new();
    }

    /// <summary>
    /// 异步获取 MD5 值
    /// </summary>
    /// <param name="bytes">要计算的数据</param>
    /// <param name="trans">是否转换格式</param>
    /// <returns>MD5 值</returns>
    private static async Task<string?> GetMD5(byte[] bytes, bool trans = false)
    {
        var md5 = MD5.Create();

        byte[]? result = null;

        await Task.Run(() =>
        {
            result = md5.ComputeHash(bytes);
        });

        if (result is null)
            return null;

        var sb = new StringBuilder();

        if (trans) foreach (var bt in result) sb.Append(bt.ToString("x2"));
        else sb.Append(Encoding.UTF8.GetString(result));

        md5.Dispose();

        return sb.ToString();
    }

    /// <summary>
    /// 异步加载本地文件到缓存
    /// </summary>
    /// <param name="fileLocation">文件路径</param>
    /// <param name="token">取消口令</param>
    /// <returns>缓存文件编号 (MD5), 如果 Hash 值已存在, 则替换原内容</returns>
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

    /// <summary>
    /// 异步接收文件到缓存
    /// </summary>
    /// <param name="bin">文件字节数组</param>
    /// <param name="token">取消口令</param>
    /// <returns>缓存文件编号 (MD5), 如果 Hash 值已存在, 则替换原内容</returns>
    public async Task<string?> ReceiveFileToCache(byte[] bin, CancellationToken token = default)
    {
        var id = await GetMD5(bin, true);

        if (token.IsCancellationRequested) return null;

        if (ReceivingFilesCache is null || id is null) return null;

        if (!ReceivingFilesCache.ContainsKey(id))
            ReceivingFilesCache.Add(id, bin);

        GC.Collect();

        return id;
    }

    /// <summary>
    /// 从接收到的文件缓存中获取文件
    /// </summary>
    /// <param name="id">缓存 ID</param>
    /// <returns>文件字节数组</returns>
    public byte[]? GetReceivedFileFromCache(string id)
    {
        if (ReceivingFilesCache is null) return null;

        if (ReceivingFilesCache.TryGetValue(id, out byte[]? bin))
            return bin;
        else return null;
    }

    /// <summary>
    /// 清除文件缓存
    /// </summary>
    /// <param name="id">指定 ID 清除</param>
    public bool? DisposeFileCache(string id)
    {
        if (FilesCache is null) return null;

        if (FilesCache.ContainsKey(id))
        {
            FilesCache.Remove(id);

            GC.Collect();

            return true;
        }

        return false;
    }

    private readonly Dictionary<string, byte[]>? FilesCache;

    private readonly Dictionary<string, byte[]>? ReceivingFilesCache;

}
