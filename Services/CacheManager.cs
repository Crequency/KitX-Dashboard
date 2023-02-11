using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace KitX_Dashboard.Services;

internal class CacheManager
{
    internal CacheManager()
    {
        FilesCache = new();
        SendingFilesCache = new();
        ReceivingFilesCache = new();
    }

    /// <summary>
    /// 加载本地文件到缓存
    /// </summary>
    /// <param name="fileLocation">文件路径</param>
    /// <param name="token">取消口令</param>
    /// <returns>缓存编号, 如果 Hash 值已存在, 则替换原内容</returns>
    public async Task<int?> LoadFileToCache(string fileLocation, CancellationToken token = default)
    {
        var fullPath = Path.GetFullPath(fileLocation);
        var bin = await File.ReadAllBytesAsync(fullPath, token);

        if (bin is null)
            return null;

        var id = bin.GetHashCode();

        if (token.IsCancellationRequested || FilesCache is null)
            return null;

        if (FilesCache.ContainsKey(id))
            FilesCache[id] = bin;
        else
            FilesCache?.Add(id, bin);

        return id;
    }

    private readonly Dictionary<int, byte[]>? FilesCache;

    public readonly Dictionary<int, byte[]>? SendingFilesCache;

    public readonly Dictionary<int, byte[]>? ReceivingFilesCache;

}
