using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;
using ResourceManager.Core;

namespace ResourceManager.Infrastructure.Windows;

[SupportedOSPlatform("windows")]
public sealed class WindowsServiceManager : IServiceCollector, IServiceController
{
    public CollectorState State { get; private set; } = CollectorState.Active;

    public ValueTask<IReadOnlyList<ServiceRecord>> CaptureAsync(CancellationToken cancellationToken = default)
    {
        var handle = OpenSCManagerW(null, null, 0x0004);
        if (handle == IntPtr.Zero)
        {
            State = CollectorState.PermissionLimited;
            return ValueTask.FromResult<IReadOnlyList<ServiceRecord>>([]);
        }
        try
        {
            var size = 0;
            var resume = 0;
            _ = EnumServicesStatusExW(handle, 0, 0x30, 0x03, IntPtr.Zero, 0, out var needed, out _, ref resume, null);
            size = Math.Max(needed, 64 * 1024);
            var buffer = Marshal.AllocHGlobal(size);
            try
            {
                resume = 0;
                if (!EnumServicesStatusExW(handle, 0, 0x30, 0x03, buffer, size, out _, out var count, ref resume, null))
                {
                    State = CollectorState.Failed;
                    return ValueTask.FromResult<IReadOnlyList<ServiceRecord>>([]);
                }
                var results = new List<ServiceRecord>(count);
                var rowSize = Marshal.SizeOf<EnumServiceStatusProcess>();
                for (var i = 0; i < count; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var row = Marshal.PtrToStructure<EnumServiceStatusProcess>(buffer + i * rowSize);
                    var name = Marshal.PtrToStringUni(row.ServiceName) ?? string.Empty;
                    var display = Marshal.PtrToStringUni(row.DisplayName) ?? name;
                    results.Add(new ServiceRecord(name, display, FormatState(row.Status.CurrentState),
                        row.Status.ProcessId == 0 ? null : (int)row.Status.ProcessId, ReadStartType(name)));
                }
                State = CollectorState.Active;
                return ValueTask.FromResult<IReadOnlyList<ServiceRecord>>(results.OrderBy(x => x.DisplayName).ToArray());
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        finally { CloseServiceHandle(handle); }
    }

    public ValueTask<ActionAudit> ControlAsync(string serviceName, string action, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(serviceName) || serviceName.Contains('\\'))
            return ValueTask.FromResult(Fail(action, serviceName, "Invalid service name."));
        if (serviceName.Equals("EventLog", StringComparison.OrdinalIgnoreCase)
            || serviceName.Equals("RpcSs", StringComparison.OrdinalIgnoreCase))
            return ValueTask.FromResult(Fail(action, serviceName, "This critical service is protected by Resource Manager."));
        if (action == "Restart") return RestartAsync(serviceName, cancellationToken);
        var manager = OpenSCManagerW(null, null, 0x0001);
        if (manager == IntPtr.Zero) return ValueTask.FromResult(Fail(action, serviceName, new Win32Exception().Message));
        try
        {
            var access = action switch { "Start" => 0x0010, "Stop" => 0x0020, "Pause" or "Continue" => 0x0040, _ => 0 };
            if (access == 0) return ValueTask.FromResult(Fail(action, serviceName, "Unsupported service action."));
            var service = OpenServiceW(manager, serviceName, (uint)access);
            if (service == IntPtr.Zero) return ValueTask.FromResult(Fail(action, serviceName, new Win32Exception().Message));
            try
            {
                var success = action == "Start"
                    ? StartServiceW(service, 0, IntPtr.Zero)
                    : ControlService(service, action switch { "Stop" => 1u, "Pause" => 2u, _ => 3u }, out _);
                return ValueTask.FromResult(success
                    ? new ActionAudit(DateTimeOffset.Now, action, serviceName, false, true, null)
                    : Fail(action, serviceName, new Win32Exception().Message));
            }
            finally { CloseServiceHandle(service); }
        }
        finally { CloseServiceHandle(manager); }
    }

    private static async ValueTask<ActionAudit> RestartAsync(string name, CancellationToken token)
    {
        var manager = OpenSCManagerW(null, null, 0x0001);
        if (manager == IntPtr.Zero) return Fail("Restart", name, new Win32Exception().Message);
        try
        {
            var service = OpenServiceW(manager, name, 0x0034); // query, start and stop
            if (service == IntPtr.Zero) return Fail("Restart", name, new Win32Exception().Message);
            try
            {
                if (!QueryServiceStatus(service, out var status)) return Fail("Restart", name, new Win32Exception().Message);
                if (status.CurrentState != 1)
                {
                    if (status.CurrentState != 3 && !ControlService(service, 1, out _))
                        return Fail("Restart", name, new Win32Exception().Message);
                    if (!await WaitForStateAsync(service, 1, token))
                        return Fail("Restart", name, "Timed out waiting for the service to stop; no start was requested.");
                }
                token.ThrowIfCancellationRequested();
                if (!StartServiceW(service, 0, IntPtr.Zero)) return Fail("Restart", name, new Win32Exception().Message);
                if (!await WaitForStateAsync(service, 4, token))
                    return Fail("Restart", name, "Start was requested, but the service did not reach Running within 30 seconds.");
                return new ActionAudit(DateTimeOffset.Now, "Restart", name, false, true, null);
            }
            catch (OperationCanceledException) { return Fail("Restart", name, "Restart cancelled; inspect the current service state."); }
            catch (Win32Exception ex) { return Fail("Restart", name, ex.Message); }
            finally { CloseServiceHandle(service); }
        }
        finally { CloseServiceHandle(manager); }
    }

    private static async Task<bool> WaitForStateAsync(IntPtr service, uint target, CancellationToken token)
    {
        var deadline = Environment.TickCount64 + 30_000;
        do
        {
            token.ThrowIfCancellationRequested();
            if (!QueryServiceStatus(service, out var status)) throw new Win32Exception();
            if (status.CurrentState == target) return true;
            await Task.Delay(250, token).ConfigureAwait(false);
        } while (Environment.TickCount64 < deadline);
        return false;
    }

    private static string FormatState(uint state) => state switch
    {
        1 => "Stopped", 2 => "Starting", 3 => "Stopping", 4 => "Running", 5 => "Continuing", 6 => "Pausing", 7 => "Paused", _ => "Unknown"
    };

    private static string ReadStartType(string name)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\" + name);
            return key?.GetValue("Start") is int value ? value switch
            {
                0 => "Boot", 1 => "System", 2 => "Automatic", 3 => "Manual", 4 => "Disabled", _ => "Unknown"
            } : "Unknown";
        }
        catch { return "Unknown"; }
    }

    private static ActionAudit Fail(string action, string target, string error)
        => new(DateTimeOffset.Now, action, target, false, false, error);

    [StructLayout(LayoutKind.Sequential)]
    private struct ServiceStatusProcess
    {
        public uint ServiceType, CurrentState, ControlsAccepted, Win32ExitCode, ServiceSpecificExitCode,
            CheckPoint, WaitHint, ProcessId, ServiceFlags;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct EnumServiceStatusProcess
    {
        public IntPtr ServiceName, DisplayName;
        public ServiceStatusProcess Status;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct ServiceStatus
    {
        public uint ServiceType, CurrentState, ControlsAccepted, Win32ExitCode, ServiceSpecificExitCode, CheckPoint, WaitHint;
    }
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "OpenSCManagerW", SetLastError = true)]
    private static extern IntPtr OpenSCManagerW(string? machine, string? database, uint access);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "EnumServicesStatusExW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumServicesStatusExW(IntPtr manager, int infoLevel, uint serviceType, uint serviceState,
        IntPtr services, int bufferSize, out int bytesNeeded, out int servicesReturned, ref int resumeHandle, string? groupName);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "OpenServiceW", SetLastError = true)]
    private static extern IntPtr OpenServiceW(IntPtr manager, string serviceName, uint access);
    [DllImport("advapi32.dll", EntryPoint = "StartServiceW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool StartServiceW(IntPtr service, int argCount, IntPtr args);
    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ControlService(IntPtr service, uint control, out ServiceStatus status);
    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryServiceStatus(IntPtr service, out ServiceStatus status);
    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseServiceHandle(IntPtr handle);
}
