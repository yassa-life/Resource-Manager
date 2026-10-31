using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using ResourceManager.Core;

namespace ResourceManager.Infrastructure.Windows;

/// <summary>Expensive file and module inspection, only run after the user requests it.</summary>
public sealed class WindowsProcessDetailInspector : IProcessDetailInspector
{
    public async ValueTask<string> InspectAsync(ProcessSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        var report = new StringBuilder();
        var moduleReport = await Task.Run(() => InspectModules(snapshot, cancellationToken), cancellationToken).ConfigureAwait(false);
        if (moduleReport.Contains("PID reuse detected", StringComparison.Ordinal)) return moduleReport;
        report.Append(moduleReport);
        report.AppendLine().AppendLine("EXECUTABLE FILE");
        var path = snapshot.ExecutablePath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        { report.AppendLine("Path unavailable or file no longer exists."); return report.ToString(); }

        try
        {
            var file = new FileInfo(path);
            var version = FileVersionInfo.GetVersionInfo(path);
            report.AppendLine($"Path                {path}");
            report.AppendLine($"Size                {ByteSize.Format(file.Length)}");
            report.AppendLine($"Modified            {file.LastWriteTime:yyyy-MM-dd HH:mm:ss}");
            report.AppendLine($"Company             {Text(version.CompanyName)}");
            report.AppendLine($"Product             {Text(version.ProductName)}");
            report.AppendLine($"File version        {Text(version.FileVersion)}");
            report.AppendLine("Signature trust     Not verified by this inspector");

            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
                128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
            report.AppendLine($"SHA-256             {Convert.ToHexString(hash)}");
            report.AppendLine("Hash is of the current file at this path, not necessarily the image loaded in memory.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { report.AppendLine($"File details limited: {ex.Message}"); }
        return report.ToString();
    }

    private static string InspectModules(ProcessSnapshot snapshot, CancellationToken token)
    {
        var report = new StringBuilder("LOADED MODULES\n");
        try
        {
            using var process = Process.GetProcessById(snapshot.Pid);
            if (Math.Abs((process.StartTime - snapshot.Identity.StartTime.LocalDateTime).TotalSeconds) > 1)
                return "PID reuse detected. Select the current process again before deep inspection.\n";
            try
            {
                var modules = process.Modules.Cast<ProcessModule>().ToArray();
                report.AppendLine($"Count               {modules.Length}");
                foreach (var module in modules.Take(60))
                {
                    token.ThrowIfCancellationRequested();
                    report.AppendLine($"{module.ModuleName}  {module.FileName}");
                }
                if (modules.Length > 60) report.AppendLine($"… {modules.Length - 60} more modules not shown");
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
            { report.AppendLine($"Unavailable: {ex.Message}"); }
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        { report.AppendLine($"Process is no longer inspectable: {ex.Message}"); }
        return report.ToString();
    }

    private static string Text(string? value) => string.IsNullOrWhiteSpace(value) ? "Not provided" : value;
}
