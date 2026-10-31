using System.Diagnostics;
using ResourceManager.Core;

namespace ResourceManager.Infrastructure.Windows;

public sealed class WindowsProcessCollector : IProcessCollector
{
    private readonly Dictionary<ProcessIdentity, (TimeSpan Cpu, DateTimeOffset At)> _previous = [];
    private readonly Dictionary<ProcessIdentity, (string? Path, string? Architecture, int Threads, DateTimeOffset ThreadsAt)> _metadata = [];
    public CollectorState State { get; private set; } = CollectorState.Active;

    public ValueTask<IReadOnlyList<ProcessSnapshot>> CaptureAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.Now;
        var results = new List<ProcessSnapshot>();
        foreach (var process in Process.GetProcesses())
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var start = new DateTimeOffset(process.StartTime);
                var identity = new ProcessIdentity(process.Id, start);
                var totalCpu = process.TotalProcessorTime;
                var cpu = 0d;
                if (_previous.TryGetValue(identity, out var previous))
                {
                    var elapsed = (now - previous.At).TotalMilliseconds * Environment.ProcessorCount;
                    if (elapsed > 0) cpu = Math.Clamp((totalCpu - previous.Cpu).TotalMilliseconds * 100 / elapsed, 0, 100);
                }
                _previous[identity] = (totalCpu, now);

                if (!_metadata.TryGetValue(identity, out var metadata))
                {
                    string? path = null;
                    string? architecture = null;
                    var threads = 0;
                    try { path = process.MainModule?.FileName; } catch { }
                    try { architecture = Environment.Is64BitOperatingSystem && IsWow64Process(process.Handle, out var wow64) && wow64 ? "x86" : "x64"; } catch { }
                    try { threads = process.Threads.Count; } catch { }
                    metadata = (path, architecture, threads, now);
                }
                else if (now - metadata.ThreadsAt >= TimeSpan.FromSeconds(30))
                {
                    try { metadata.Threads = process.Threads.Count; } catch { }
                    metadata.ThreadsAt = now;
                }
                _metadata[identity] = metadata;

                results.Add(new ProcessSnapshot(identity, process.ProcessName, metadata.Path, null, null, cpu,
                    process.WorkingSet64, process.PrivateMemorySize64, metadata.Threads, process.HandleCount,
                    null, metadata.Architecture, null, null, now));
            }
            catch { /* The process exited or is protected between enumeration and inspection. */ }
            finally { process.Dispose(); }
        }

        var current = results.Select(x => x.Identity).ToHashSet();
        foreach (var stale in _previous.Keys.Where(k => !current.Contains(k)).ToArray()) _previous.Remove(stale);
        foreach (var stale in _metadata.Keys.Where(k => !current.Contains(k)).ToArray()) _metadata.Remove(stale);
        return ValueTask.FromResult<IReadOnlyList<ProcessSnapshot>>(results.OrderByDescending(p => p.CpuPercent).ToArray());
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool IsWow64Process(IntPtr process, out bool wow64Process);
}
