using System.Diagnostics;
using ResourceManager.Core;

namespace ResourceManager.Infrastructure.Windows;

public sealed class WindowsFolderRelationshipScanner : IFolderRelationshipScanner
{
    public ValueTask<IReadOnlyList<FolderRelationship>> ScanAsync(string folderPath, CancellationToken cancellationToken = default)
    {
        var normalized = Path.GetFullPath(folderPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var results = new List<FolderRelationship>();
        foreach (var process in Process.GetProcesses())
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var identity = new ProcessIdentity(process.Id, process.StartTime);
                string? executable = null;
                try { executable = process.MainModule?.FileName; } catch { }
                if (IsWithin(executable, normalized))
                    results.Add(new(identity, process.ProcessName, executable!, FolderRelationshipKind.Executable, "High"));

                try
                {
                    foreach (ProcessModule module in process.Modules)
                        if (IsWithin(module.FileName, normalized) && !string.Equals(module.FileName, executable, StringComparison.OrdinalIgnoreCase))
                            results.Add(new(identity, process.ProcessName, module.FileName, FolderRelationshipKind.LoadedModule, "High"));
                }
                catch { }
            }
            catch { }
            finally { process.Dispose(); }
        }
        return ValueTask.FromResult<IReadOnlyList<FolderRelationship>>(results);
    }

    private static bool IsWithin(string? path, string root) => !string.IsNullOrWhiteSpace(path)
        && Path.GetFullPath(path).StartsWith(root, StringComparison.OrdinalIgnoreCase);
}
