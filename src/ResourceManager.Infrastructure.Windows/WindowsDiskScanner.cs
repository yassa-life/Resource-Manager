using ResourceManager.Core;

namespace ResourceManager.Infrastructure.Windows;

public sealed class WindowsDiskScanner : IDiskScanner
{
    public async IAsyncEnumerable<DiskEntry> ScanAsync(string rootPath,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var pending = new Stack<string>();
        pending.Push(Path.GetFullPath(rootPath));
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = pending.Pop();
            IEnumerable<string>? entries = null;
            var accessDenied = false;
            try { entries = Directory.EnumerateFileSystemEntries(directory).ToArray(); }
            catch (UnauthorizedAccessException) { accessDenied = true; }
            catch (IOException) { }
            if (accessDenied)
            {
                yield return new DiskEntry(directory, Path.GetFileName(directory), true, 0, 0, string.Empty, DateTimeOffset.MinValue, true);
                continue;
            }
            if (entries is null) continue;

            foreach (var entry in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                FileAttributes attributes;
                try { attributes = File.GetAttributes(entry); } catch { continue; }
                var isDirectory = attributes.HasFlag(FileAttributes.Directory);
                var isReparse = attributes.HasFlag(FileAttributes.ReparsePoint);
                if (isDirectory)
                {
                    DateTimeOffset modified;
                    try { modified = Directory.GetLastWriteTimeUtc(entry); } catch { modified = DateTimeOffset.MinValue; }
                    yield return new DiskEntry(entry, Path.GetFileName(entry), true, 0, 0, string.Empty, modified);
                    if (!isReparse) pending.Push(entry);
                }
                else
                {
                    FileInfo info;
                    try { info = new FileInfo(entry); } catch { continue; }
                    yield return new DiskEntry(entry, info.Name, false, info.Length, info.Length,
                        info.Extension.ToLowerInvariant(), info.LastWriteTimeUtc);
                }
                await Task.Yield();
            }
        }
    }
}
