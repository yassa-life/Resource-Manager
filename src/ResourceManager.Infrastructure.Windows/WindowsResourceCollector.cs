using System.Runtime.InteropServices;
using ResourceManager.Core;

namespace ResourceManager.Infrastructure.Windows;

public sealed class WindowsResourceCollector : IResourceCollector
{
    private ulong _previousIdle;
    private ulong _previousKernel;
    private ulong _previousUser;
    public CollectorState State { get; private set; } = CollectorState.Active;

    public ValueTask<ResourceSample> CaptureAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var memory = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        if (!GlobalMemoryStatusEx(ref memory)) State = CollectorState.PermissionLimited;

        var cpu = CaptureCpu();
        var drives = DriveInfo.GetDrives()
            .Where(d => d.IsReady)
            .Select(d => new DriveSnapshot(d.Name, d.DriveFormat, d.TotalSize, d.AvailableFreeSpace))
            .ToArray();

        return ValueTask.FromResult(new ResourceSample(DateTimeOffset.Now, cpu,
            (long)memory.TotalPhysical, (long)(memory.TotalPhysical - memory.AvailablePhysical), drives));
    }

    private double CaptureCpu()
    {
        if (!GetSystemTimes(out var idle, out var kernel, out var user)) return 0;
        var currentIdle = ToUInt64(idle);
        var currentKernel = ToUInt64(kernel);
        var currentUser = ToUInt64(user);
        var total = currentKernel - _previousKernel + currentUser - _previousUser;
        var idleDelta = currentIdle - _previousIdle;
        _previousIdle = currentIdle;
        _previousKernel = currentKernel;
        _previousUser = currentUser;
        return total == 0 ? 0 : Math.Clamp((total - idleDelta) * 100d / total, 0, 100);
    }

    private static ulong ToUInt64(FileTime time) => ((ulong)time.High << 32) | time.Low;

    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime { public uint Low; public uint High; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MemoryStatus
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(out FileTime idle, out FileTime kernel, out FileTime user);
}
