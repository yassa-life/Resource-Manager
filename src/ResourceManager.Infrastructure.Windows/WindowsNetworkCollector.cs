using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using ResourceManager.Core;

namespace ResourceManager.Infrastructure.Windows;

public sealed class WindowsNetworkCollector : INetworkCollector
{
    public CollectorState State { get; private set; } = CollectorState.Active;
    public string StatusMessage { get; private set; } = "waiting for capture";
    public ValueTask<IReadOnlyList<NetworkFlow>> CaptureAsync(CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            State = CollectorState.Unsupported;
            StatusMessage = "Windows endpoint APIs are unavailable on this platform.";
            return ValueTask.FromResult<IReadOnlyList<NetworkFlow>>([]);
        }
        var now = DateTimeOffset.Now;
        var flows = new List<NetworkFlow>();
        var names = new Dictionary<int, string>();
        var errors = new List<(string Table, uint Code)>();
        Read<TcpRow>(GetExtendedTcpTable, 2, 5, "IPv4 TCP", row =>
            flows.Add(new("TCP", new IPAddress(row.LocalAddr).ToString(), Port(row.LocalPort),
                row.State == 2 ? "*" : new IPAddress(row.RemoteAddr).ToString(),
                row.State == 2 ? 0 : Port(row.RemotePort), ((TcpState)row.State).ToString(),
                (int)row.Pid, ProcessName((int)row.Pid, names), now)), errors, cancellationToken);
        Read<UdpRow>(GetExtendedUdpTable, 2, 1, "IPv4 UDP", row =>
            flows.Add(new("UDP", new IPAddress(row.LocalAddr).ToString(), Port(row.LocalPort), "*", 0,
                "Bound", (int)row.Pid, ProcessName((int)row.Pid, names), now)), errors, cancellationToken);
        Read<Tcp6Row>(GetExtendedTcpTable, 23, 5, "IPv6 TCP", row =>
            flows.Add(new("TCP", Ip6(row.LocalAddr, row.LocalScope), Port(row.LocalPort),
                row.State == 2 ? "*" : Ip6(row.RemoteAddr, row.RemoteScope),
                row.State == 2 ? 0 : Port(row.RemotePort), ((TcpState)row.State).ToString(),
                (int)row.Pid, ProcessName((int)row.Pid, names), now)), errors, cancellationToken);
        Read<Udp6Row>(GetExtendedUdpTable, 23, 1, "IPv6 UDP", row =>
            flows.Add(new("UDP", Ip6(row.LocalAddr, row.LocalScope), Port(row.LocalPort), "*", 0,
                "Bound", (int)row.Pid, ProcessName((int)row.Pid, names), now)), errors, cancellationToken);
        State = errors.Count == 0 ? CollectorState.Active :
            errors.Any(e => e.Code == 5) ? CollectorState.PermissionLimited : CollectorState.Failed;
        StatusMessage = errors.Count == 0 ? "IPv4 + IPv6 TCP/UDP capture active · sampled endpoints"
            : "partial capture: " + string.Join("; ", errors.Select(e => e.Table + ": " + new Win32Exception((int)e.Code).Message));
        return ValueTask.FromResult<IReadOnlyList<NetworkFlow>>(flows.OrderBy(f => f.ProcessName).ThenBy(f => f.LocalPort).ToArray());
    }

    private delegate uint TableReader(IntPtr table, ref int size, bool order, int family, int tableClass, uint reserved);
    private static void Read<T>(TableReader reader, int family, int tableClass, string label, Action<T> accept,
        List<(string Table, uint Code)> errors, CancellationToken token) where T : struct
    {
        var size = 0;
        var code = reader(IntPtr.Zero, ref size, true, family, tableClass, 0);
        if (code != 0 && code != 122) { errors.Add((label, code)); return; }
        for (var attempt = 0; attempt < 3; attempt++)
        {
            token.ThrowIfCancellationRequested();
            if (size < 4 || size > 64 * 1024 * 1024) { errors.Add((label, 13)); return; }
            var allocated = size;
            var buffer = Marshal.AllocHGlobal(allocated);
            try
            {
                code = reader(buffer, ref size, true, family, tableClass, 0);
                if (code == 122) continue; // The table grew between the size and data requests.
                if (code != 0) { errors.Add((label, code)); return; }
                var count = Marshal.ReadInt32(buffer);
                var rowSize = Marshal.SizeOf<T>();
                if (count < 0 || count > (allocated - 4) / rowSize) { errors.Add((label, 13)); return; }
                for (var i = 0; i < count; i++)
                {
                    token.ThrowIfCancellationRequested();
                    accept(Marshal.PtrToStructure<T>(buffer + 4 + i * rowSize));
                }
                return;
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        errors.Add((label, code));
    }
    private static string Ip6(byte[] bytes, uint scope)
        => new IPAddress(bytes, (uint)IPAddress.NetworkToHostOrder(unchecked((int)scope))).ToString();
    private static int Port(uint value) => (ushort)IPAddress.NetworkToHostOrder((short)(value & 0xffff));
    private static string ProcessName(int pid, Dictionary<int, string> names)
    {
        if (names.TryGetValue(pid, out var name)) return name;
        try { using var process = Process.GetProcessById(pid); name = process.ProcessName; }
        catch { name = pid == 0 ? "unattributed" : "unavailable"; }
        return names[pid] = name;
    }
    [StructLayout(LayoutKind.Sequential)] private struct TcpRow
    { public uint State, LocalAddr, LocalPort, RemoteAddr, RemotePort, Pid; }
    [StructLayout(LayoutKind.Sequential)] private struct UdpRow
    { public uint LocalAddr, LocalPort, Pid; }
    [StructLayout(LayoutKind.Sequential)] private struct Tcp6Row
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)] public byte[] LocalAddr;
        public uint LocalScope, LocalPort;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)] public byte[] RemoteAddr;
        public uint RemoteScope, RemotePort, State, Pid;
    }
    [StructLayout(LayoutKind.Sequential)] private struct Udp6Row
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)] public byte[] LocalAddr;
        public uint LocalScope, LocalPort, Pid;
    }
    private enum TcpState : uint
    { Closed = 1, Listen, SynSent, SynReceived, Established, FinWait1, FinWait2, CloseWait, Closing, LastAck, TimeWait, DeleteTcb }
    [DllImport("iphlpapi.dll")]
    private static extern uint GetExtendedTcpTable(IntPtr table, ref int size, bool order, int ipVersion, int tableClass, uint reserved);
    [DllImport("iphlpapi.dll")]
    private static extern uint GetExtendedUdpTable(IntPtr table, ref int size, bool order, int ipVersion, int tableClass, uint reserved);
}
