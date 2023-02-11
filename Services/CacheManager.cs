using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace KitX_Dashboard.Services;

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

        var id = await GetMD5(bin);

        if (token.IsCancellationRequested || FilesCache is null || id is null)
            return null;

        if (FilesCache.ContainsKey(id))
            FilesCache[id] = bin;
        else
            FilesCache?.Add(id, bin);

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
        var id = await GetMD5(bin);

        if (token.IsCancellationRequested) return null;

        if (ReceivingFilesCache is null || id is null) return null;

        if (ReceivingFilesCache.ContainsKey(id))
            ReceivingFilesCache[id] = bin;
        else
            ReceivingFilesCache.Add(id, bin);

        return id;
    }

    private readonly Dictionary<string, byte[]>? FilesCache;

    private readonly Dictionary<string, byte[]>? ReceivingFilesCache;

}
