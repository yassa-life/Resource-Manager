namespace ResourceManager.Core;

public enum CollectorState { Active, PermissionLimited, Unsupported, Failed, Paused, EnhancedOnly }
public enum RiskSeverity { Informational, Low, Medium, High, Critical }
public enum ComponentState { Available, Installed, Unavailable, UpdateAvailable, Disabled, Failed, RestartRequired }
public enum FolderRelationshipKind { Executable, LoadedModule, OpenFile }

public readonly record struct ProcessIdentity(int Pid, DateTimeOffset StartTime);

public sealed record ProcessSnapshot(
    ProcessIdentity Identity,
    string Name,
    string? ExecutablePath,
    string? CommandLine,
    int? ParentPid,
    double CpuPercent,
    long WorkingSetBytes,
    long PrivateBytes,
    int ThreadCount,
    int HandleCount,
    string? User,
    string? Architecture,
    string? Publisher,
    bool? IsSigned,
    DateTimeOffset ObservedAt)
{
    public int Pid => Identity.Pid;
    public string CpuText => $"{CpuPercent:N1}%";
    public string MemoryText => ByteSize.Format(WorkingSetBytes);
}

public sealed record ResourceSample(
    DateTimeOffset Timestamp,
    double CpuPercent,
    long PhysicalMemoryTotal,
    long PhysicalMemoryUsed,
    IReadOnlyList<DriveSnapshot> Drives)
{
    public double MemoryPercent => PhysicalMemoryTotal == 0 ? 0 : PhysicalMemoryUsed * 100d / PhysicalMemoryTotal;
    public string MemorySummary => $"{ByteSize.Format(PhysicalMemoryUsed)} / {ByteSize.Format(PhysicalMemoryTotal)}";
}

public sealed record DriveSnapshot(string Name, string Format, long TotalBytes, long FreeBytes)
{
    public double UsedPercent => TotalBytes == 0 ? 0 : (TotalBytes - FreeBytes) * 100d / TotalBytes;
    public string Summary => $"{ByteSize.Format(TotalBytes - FreeBytes)} used of {ByteSize.Format(TotalBytes)}";
}

public sealed record NetworkFlow(
    string Protocol,
    string LocalAddress,
    int LocalPort,
    string RemoteAddress,
    int RemotePort,
    string State,
    int Pid,
    string ProcessName,
    DateTimeOffset ObservedAt)
{
    public string AddressFamily => LocalAddress.Contains(':') ? "IPv6" : "IPv4";
    public string LocalEndpoint => FormatEndpoint(LocalAddress, LocalPort);
    public string RemoteEndpoint => RemotePort > 0 ? FormatEndpoint(RemoteAddress, RemotePort) : RemoteAddress;
    private static string FormatEndpoint(string address, int port) => address.Contains(':') ? $"[{address}]:{port}" : $"{address}:{port}";
    public string Explanation => Protocol == "UDP"
        ? $"{ProcessName} is using UDP port {LocalPort}. UDP has no persistent connection state."
        : State == "Listen" ? $"{ProcessName} listens on {LocalEndpoint}; no remote peer is attached to this listener."
        : $"{ProcessName} has a {State.ToLowerInvariant()} {Protocol} connection from port {LocalPort} to {RemoteEndpoint}.";
}

public sealed record ServiceRecord(string Name, string DisplayName, string Status, int? ProcessId, string StartType);

public sealed record FolderRelationship(
    ProcessIdentity Process,
    string ProcessName,
    string Path,
    FolderRelationshipKind Kind,
    string Confidence);

public sealed record DiskEntry(
    string Path,
    string Name,
    bool IsDirectory,
    long LogicalBytes,
    long PhysicalBytes,
    string Extension,
    DateTimeOffset ModifiedAt,
    bool AccessDenied = false)
{
    public string SizeText => ByteSize.Format(LogicalBytes);
}

public sealed record ThreatEvidence(string Title, string Explanation, int Score, bool IsRisk);

public sealed record ThreatFinding(
    ProcessIdentity Process,
    string ProcessName,
    int Score,
    RiskSeverity Severity,
    double Confidence,
    IReadOnlyList<ThreatEvidence> Evidence,
    DateTimeOffset AssessedAt)
{
    public string ConfidenceText => $"{Confidence:P0}";
    public string Explanation => Evidence.Count == 0
        ? "No unusual local behavior was found in the available evidence."
        : string.Join(" ", Evidence.Take(2).Select(e => e.Explanation));
}

public sealed record CapabilityDescriptor(string Id, string Name, string Summary, bool RequiresElevation);

public sealed record ComponentManifest(
    string Id,
    string Name,
    string Version,
    string Publisher,
    string License,
    long PackageBytes,
    bool RequiresElevation,
    bool UsesNetwork,
    bool StartsAutomatically,
    IReadOnlyList<string> Capabilities,
    IReadOnlyList<string> SystemChanges,
    ComponentState State)
{
    public string? UnavailableReason { get; init; }
    public string PackageSize => ByteSize.Format(PackageBytes);
    public bool CanInstall => State == ComponentState.Available;
    public bool CanRemove => State == ComponentState.Installed && Id != "security-models";
    public bool CanChange => CanInstall || CanRemove;
    public string ActionText => State switch
    {
        ComponentState.Installed => "Remove",
        ComponentState.Available => "Install",
        _ => "Unavailable"
    };
    public string StatusText => State switch
    {
        ComponentState.UpdateAvailable => "Update available",
        ComponentState.RestartRequired => "Restart required",
        _ => State.ToString()
    };
}

public sealed record InstallPlan(string ComponentId, IReadOnlyList<string> Steps, bool RequiresElevation, bool RequiresRestart);
public sealed record InstallReceipt(string ComponentId, string Version, DateTimeOffset InstalledAt, IReadOnlyList<string> Changes);
public sealed record CompatibilityResult(bool IsCompatible, IReadOnlyList<string> Reasons);
public sealed record RollbackResult(bool Succeeded, IReadOnlyList<string> RevertedChanges, string? Error);
public sealed record ActionAudit(DateTimeOffset Timestamp, string Action, string Target, bool Elevated, bool Succeeded, string? Error);

public static class ByteSize
{
    private static readonly string[] Units = ["B", "KB", "MB", "GB", "TB", "PB"];

    public static string Format(long bytes)
    {
        var value = Math.Max(0, bytes);
        var unit = 0;
        var size = (double)value;
        while (size >= 1024 && unit < Units.Length - 1) { size /= 1024; unit++; }
        return $"{size:N1} {Units[unit]}";
    }
}
