namespace ResourceManager.Core;

public interface IResourceCollector
{
    CollectorState State { get; }
    ValueTask<ResourceSample> CaptureAsync(CancellationToken cancellationToken = default);
}

public interface IProcessCollector
{
    CollectorState State { get; }
    ValueTask<IReadOnlyList<ProcessSnapshot>> CaptureAsync(CancellationToken cancellationToken = default);
}

public interface INetworkCollector
{
    CollectorState State { get; }
    string StatusMessage => State.ToString();
    ValueTask<IReadOnlyList<NetworkFlow>> CaptureAsync(CancellationToken cancellationToken = default);
}

public interface IServiceCollector
{
    CollectorState State { get; }
    ValueTask<IReadOnlyList<ServiceRecord>> CaptureAsync(CancellationToken cancellationToken = default);
}

public interface IServiceController
{
    ValueTask<ActionAudit> ControlAsync(string serviceName, string action, CancellationToken cancellationToken = default);
}

public interface IDiskScanner
{
    IAsyncEnumerable<DiskEntry> ScanAsync(string rootPath, CancellationToken cancellationToken = default);
}

public interface IFolderRelationshipScanner
{
    ValueTask<IReadOnlyList<FolderRelationship>> ScanAsync(string folderPath, CancellationToken cancellationToken = default);
}

public interface IThreatAnalyzer
{
    ValueTask<ThreatFinding> AnalyzeAsync(ProcessSnapshot process, IReadOnlyList<NetworkFlow> flows, CancellationToken cancellationToken = default);
}

public interface IProcessController
{
    ValueTask<ActionAudit> TerminateAsync(ProcessIdentity identity, bool processTree, CancellationToken cancellationToken = default);
    ValueTask<ActionAudit> RestartAsync(ProcessSnapshot process, CancellationToken cancellationToken = default);
}

public interface IProcessDetailInspector
{
    ValueTask<string> InspectAsync(ProcessSnapshot process, CancellationToken cancellationToken = default);
}

public interface IHistoryRepository : IAsyncDisposable
{
    ValueTask InitializeAsync(CancellationToken cancellationToken = default);
    ValueTask AppendAsync(ResourceSample sample, CancellationToken cancellationToken = default);
    ValueTask<IReadOnlyList<ResourceSample>> ReadRecentAsync(int limit, CancellationToken cancellationToken = default);
    ValueTask PruneAsync(TimeSpan retention, long maximumBytes, CancellationToken cancellationToken = default);
    ValueTask ClearAsync(CancellationToken cancellationToken = default);
}

public interface IComponentManager
{
    IReadOnlyList<ComponentManifest> GetCatalog();
    InstallPlan Preview(string componentId);
    ValueTask<InstallReceipt> InstallAsync(InstallPlan plan, CancellationToken cancellationToken = default);
    ValueTask<RollbackResult> UninstallAsync(string componentId, CancellationToken cancellationToken = default);
}
