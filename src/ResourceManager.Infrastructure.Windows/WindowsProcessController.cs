using System.Diagnostics;
using ResourceManager.Core;

namespace ResourceManager.Infrastructure.Windows;

public sealed class WindowsProcessController : IProcessController
{
    private static readonly HashSet<string> ProtectedNames = new(StringComparer.OrdinalIgnoreCase)
    { "System", "Registry", "smss", "csrss", "wininit", "winlogon", "services", "lsass", "svchost", "dwm" };

    public ValueTask<ActionAudit> TerminateAsync(ProcessIdentity identity, bool processTree, CancellationToken cancellationToken = default)
    {
        if (identity.Pid is 0 or 4 || identity.Pid == Environment.ProcessId)
            return ValueTask.FromResult(Fail("Terminate", identity.Pid.ToString(), "Resource Manager protects this process."));
        try
        {
            using var process = Process.GetProcessById(identity.Pid);
            if (ProtectedNames.Contains(process.ProcessName))
                return ValueTask.FromResult(Fail("Terminate", process.ProcessName, "This Windows process is protected by Resource Manager."));
            if (Math.Abs((process.StartTime - identity.StartTime.LocalDateTime).TotalSeconds) > 1)
                return ValueTask.FromResult(Fail("Terminate", identity.Pid.ToString(), "The PID now belongs to a different process."));
            process.Kill(processTree);
            return ValueTask.FromResult(Ok(processTree ? "Terminate tree" : "Terminate", $"{process.ProcessName} ({identity.Pid})"));
        }
        catch (Exception ex) { return ValueTask.FromResult(Fail("Terminate", identity.Pid.ToString(), ex.Message)); }
    }

    public ValueTask<ActionAudit> RestartAsync(ProcessSnapshot process, CancellationToken cancellationToken = default)
    {
        if (process.Pid is 0 or 4 || process.Pid == Environment.ProcessId || ProtectedNames.Contains(process.Name))
            return ValueTask.FromResult(Fail("Restart", process.Name, "Resource Manager protects this process."));
        if (string.IsNullOrWhiteSpace(process.ExecutablePath))
            return ValueTask.FromResult(Fail("Restart", process.Name, "The executable path is unavailable."));
        try
        {
            using var running = Process.GetProcessById(process.Pid);
            if (Math.Abs((running.StartTime - process.Identity.StartTime.LocalDateTime).TotalSeconds) > 1)
                return ValueTask.FromResult(Fail("Restart", process.Name, "The PID now belongs to a different process."));
            running.Kill(true);
            running.WaitForExit(5000);
            Process.Start(new ProcessStartInfo(process.ExecutablePath) { UseShellExecute = true });
            return ValueTask.FromResult(Ok("Restart", process.Name));
        }
        catch (Exception ex) { return ValueTask.FromResult(Fail("Restart", process.Name, ex.Message)); }
    }

    private static ActionAudit Ok(string action, string target) => new(DateTimeOffset.Now, action, target, false, true, null);
    private static ActionAudit Fail(string action, string target, string error) => new(DateTimeOffset.Now, action, target, false, false, error);
}
