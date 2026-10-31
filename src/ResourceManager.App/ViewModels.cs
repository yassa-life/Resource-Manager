using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;
using Avalonia.Threading;
using ResourceManager.Core;

namespace ResourceManager.App;

public sealed class MainWindowViewModel : INotifyPropertyChanged, IAsyncDisposable
{
    private readonly IResourceCollector _resources;
    private readonly IProcessCollector _processes;
    private readonly INetworkCollector _network;
    private readonly IServiceCollector _services;
    private readonly IServiceController _serviceController;
    private readonly IDiskScanner _disk;
    private readonly IFolderRelationshipScanner _folders;
    private readonly IThreatAnalyzer _threats;
    private readonly IHistoryRepository _history;
    private readonly IProcessController _controller;
    private readonly IProcessDetailInspector _processDetails;
    private readonly IComponentManager _componentManager;
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _scan;
    private CancellationTokenSource? _deepInspect;
    private ResourceSample? _current;
    private ProcessSnapshot? _selectedProcess;
    private NetworkFlow? _selectedNetworkFlow;
    private ServiceRecord? _selectedService;
    private FolderRelationship? _selectedFolderRelationship;
    private ThreatFinding? _selectedFinding;
    private ResourceSample? _selectedResourceSample;
    private string _inspectorDetails = "Select a row to inspect its raw fields and relationships.";
    private string _deepProcessDetails = "Deep inspection is off by default to keep monitoring lightweight.";
    private string _status = "Starting local collectors…";
    private string _diskPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    private bool _privateSession;
    private bool _fastMonitoring;
    private bool _samplingPaused;
    private bool _showDiagrams = true;
    private bool _compactRows = true;
    private double _uiScale = 1;
    private bool _isDiskScanning, _isFolderScanning, _isDeepInspecting, _isServiceBusy, _isLoading = true;
    private string _preferencesStatus = "Preferences are saved locally when the app exits.";
    private string _processSearch = string.Empty;
    private string _networkSearch = string.Empty;
    private int _allProcessCount;
    private int _allEndpointCount;
    private DiskTreeNode? _selectedDiskFolder;
    private DiskExtensionSummary? _selectedExtension;
    private DiskEntry? _selectedDiskEntry;

    public event PropertyChangedEventHandler? PropertyChanged;
    public RangeObservableCollection<ProcessSnapshot> Processes { get; } = [];
    public RangeObservableCollection<ProcessSnapshot> VisibleProcesses { get; } = [];
    public RangeObservableCollection<ProcessSnapshot> OverviewProcesses { get; } = [];
    public RangeObservableCollection<NetworkFlow> NetworkFlows { get; } = [];
    public RangeObservableCollection<ServiceRecord> Services { get; } = [];
    public RangeObservableCollection<NetworkFlow> VisibleNetworkFlows { get; } = [];
    public RangeObservableCollection<NetworkFlow> OverviewNetworkFlows { get; } = [];
    public ObservableCollection<DiskEntry> DiskEntries { get; } = [];
    public ObservableCollection<DiskEntry> VisibleDiskEntries { get; } = [];
    public ObservableCollection<DiskTreeNode> DiskTree { get; } = [];
    public ObservableCollection<DiskExtensionSummary> DiskExtensions { get; } = [];
    public RangeObservableCollection<FolderRelationship> FolderRelationships { get; } = [];
    public RangeObservableCollection<ThreatFinding> Findings { get; } = [];
    public RangeObservableCollection<ComponentManifest> Components { get; } = [];
    public RangeObservableCollection<NetworkFlow> ServiceNetworkFlows { get; } = [];
    public RangeObservableCollection<NetworkFlow> ProcessNetworkFlows { get; } = [];
    public ObservableCollection<double> EndpointCounts { get; } = [];
    public ObservableCollection<ResourceSample> RecentSamples { get; } = [];

    public ResourceSample? Current { get => _current; private set => Set(ref _current, value); }
    public ProcessSnapshot? SelectedProcess { get => _selectedProcess; set { if (_selectedProcess?.Identity != value?.Identity) { _deepInspect?.Cancel(); DeepProcessDetails = "Deep inspection is off by default to keep monitoring lightweight."; } Set(ref _selectedProcess, value); OnPropertyChanged(nameof(ProcessTechnicalDetails)); RefreshRelatedFlows(); if (value is not null) InspectorDetails = ProcessTechnicalDetails; } }
    public NetworkFlow? SelectedNetworkFlow { get => _selectedNetworkFlow; set { Set(ref _selectedNetworkFlow, value); OnPropertyChanged(nameof(NetworkTechnicalDetails)); if (value is not null) InspectorDetails = NetworkTechnicalDetails; } }
    public ServiceRecord? SelectedService { get => _selectedService; set { Set(ref _selectedService, value); OnPropertyChanged(nameof(ServiceTechnicalDetails)); RefreshRelatedFlows(); if (value is not null) InspectorDetails = ServiceTechnicalDetails; } }
    public FolderRelationship? SelectedFolderRelationship { get => _selectedFolderRelationship; set { Set(ref _selectedFolderRelationship, value); OnPropertyChanged(nameof(FolderTechnicalDetails)); if (value is not null) InspectorDetails = FolderTechnicalDetails; } }
    public ThreatFinding? SelectedFinding { get => _selectedFinding; set { Set(ref _selectedFinding, value); OnPropertyChanged(nameof(FindingTechnicalDetails)); if (value is not null) InspectorDetails = FindingTechnicalDetails; } }
    public ResourceSample? SelectedResourceSample { get => _selectedResourceSample; set { Set(ref _selectedResourceSample, value); OnPropertyChanged(nameof(ResourceTechnicalDetails)); if (value is not null) InspectorDetails = ResourceTechnicalDetails; } }
    public string InspectorDetails { get => _inspectorDetails; private set => Set(ref _inspectorDetails, value); }
    public string DeepProcessDetails { get => _deepProcessDetails; private set => Set(ref _deepProcessDetails, value); }
    public string Status { get => _status; private set => Set(ref _status, value); }
    public string DiskPath { get => _diskPath; set => Set(ref _diskPath, value); }
    public bool PrivateSession { get => _privateSession; set => Set(ref _privateSession, value); }
    public bool ShowDiagrams { get => _showDiagrams; set => Set(ref _showDiagrams, value); }
    public bool CompactRows { get => _compactRows; set { Set(ref _compactRows, value); OnPropertyChanged(nameof(TableRowHeight)); } }
    public double TableRowHeight => CompactRows ? 28 : 36;
    public double UiScale
    {
        get => _uiScale;
        set
        {
            Set(ref _uiScale, Math.Clamp(value, 0.85, 1.25));
            if (Avalonia.Application.Current is { } app)
            {
                app.Resources["ConsoleBodySize"] = 12d * _uiScale;
                app.Resources["ConsoleSmallSize"] = 11d * _uiScale;
                app.Resources["ConsoleTitleSize"] = 17d * _uiScale;
            }
        }
    }
    public bool IsDiskScanning { get => _isDiskScanning; private set => Set(ref _isDiskScanning, value); }
    public bool IsFolderScanning { get => _isFolderScanning; private set => Set(ref _isFolderScanning, value); }
    public bool IsDeepInspecting { get => _isDeepInspecting; private set => Set(ref _isDeepInspecting, value); }
    public bool IsLoading { get => _isLoading; private set => Set(ref _isLoading, value); }
    public bool IsServiceBusy { get => _isServiceBusy; private set => Set(ref _isServiceBusy, value); }
    public string PreferencesStatus { get => _preferencesStatus; private set => Set(ref _preferencesStatus, value); }
    public string ServiceNetworkSummary => SelectedService?.ProcessId is not { } pid ? "select a running service to inspect its host ports"
        : $"pid {pid} · {ServiceNetworkFlows.Count} endpoints · {Services.Count(s => s.ProcessId == pid)} hosted services\nPorts belong to the host process; shared-host ownership cannot identify one specific service.";
    public string NetworkSummary => $"{NetworkFlows.Count(f => f.Protocol == "TCP")} tcp · {NetworkFlows.Count(f => f.Protocol == "UDP")} udp · {NetworkFlows.Count(f => f.State.Equals("Established", StringComparison.OrdinalIgnoreCase))} established";
    public bool FastMonitoring { get => _fastMonitoring; set => Set(ref _fastMonitoring, value); }
    public bool SamplingPaused { get => _samplingPaused; set => Set(ref _samplingPaused, value); }
    public string NetworkCollectorStatus => _network.StatusMessage;
    public string ProcessSearch { get => _processSearch; set { Set(ref _processSearch, value); ApplyProcessFilter(); } }
    public string NetworkSearch { get => _networkSearch; set { Set(ref _networkSearch, value); ApplyNetworkFilter(); } }
    public DiskTreeNode? SelectedDiskFolder { get => _selectedDiskFolder; set { Set(ref _selectedDiskFolder, value); ApplyDiskFilter(); } }
    public DiskExtensionSummary? SelectedExtension { get => _selectedExtension; set { Set(ref _selectedExtension, value); ApplyDiskFilter(); } }
    public DiskEntry? SelectedDiskEntry { get => _selectedDiskEntry; set { Set(ref _selectedDiskEntry, value); OnPropertyChanged(nameof(DiskTechnicalDetails)); if (value is not null) InspectorDetails = DiskTechnicalDetails; } }
    public int ProcessCount => _allProcessCount;
    public int ConnectionCount => _allEndpointCount;
    public string SelfImpactText => Processes.FirstOrDefault(p => p.Pid == Environment.ProcessId) is { } self
        ? $"APP IMPACT {self.CpuText} CPU · {self.MemoryText} RAM"
        : "APP IMPACT: COLLECTING";
    public int FindingCount => Findings.Count(f => f.Severity >= RiskSeverity.Medium);
    public string ServiceStatusText => _services.State == CollectorState.Active
        ? $"{Services.Count:N0} services detected"
        : $"Service list: {_services.State}. Windows may restrict service inspection in this session.";
    public string CpuText => $"{Current?.CpuPercent ?? 0:N0}%";
    public string MemoryText => Current?.MemorySummary ?? "Collecting…";
    public double MemoryPercent => Current?.MemoryPercent ?? 0;
    public string DiskTotal => ByteSize.Format(DiskEntries.Where(x => !x.IsDirectory).Sum(x => x.LogicalBytes));
    public string DiskCount => $"{DiskEntries.Count(x => !x.IsDirectory):N0} files";
    public string ProcessTechnicalDetails => SelectedProcess is not { } p ? "Select a process to inspect it." :
        $"IDENTITY\nPID                 {p.Pid}\nStarted             {p.Identity.StartTime:yyyy-MM-dd HH:mm:ss zzz}\nObserved            {p.ObservedAt:yyyy-MM-dd HH:mm:ss zzz}\nParent PID          {Field(p.ParentPid?.ToString())}\nUser                {Field(p.User)}\nArchitecture        {Field(p.Architecture)}\n\nEXECUTABLE\nName                {p.Name}\nPath                {Field(p.ExecutablePath)}\nCommand line        {Field(p.CommandLine)}\nPublisher           {Field(p.Publisher)}\nSignature           {(p.IsSigned is null ? "Not checked" : p.IsSigned.Value ? "Signed" : "Unsigned")}\n\nRESOURCES\nCPU                 {p.CpuPercent:N1}%\nWorking set         {ByteSize.Format(p.WorkingSetBytes)}\nPrivate memory      {ByteSize.Format(p.PrivateBytes)}\nThreads             {p.ThreadCount}\nHandles             {p.HandleCount}\n\nRELATIONSHIPS\nNetwork endpoints   {NetworkFlows.Count(f => f.Pid == p.Pid)}\nHosted services     {Services.Count(s => s.ProcessId == p.Pid)}\n\nLIMITS\nGPU and disk I/O are not collected. Use deep inspect for the executable hash and loaded modules.";
    public string NetworkTechnicalDetails => SelectedNetworkFlow is not { } f ? "Select an endpoint to inspect it." :
        $"OWNER\nProcess             {f.ProcessName}\nPID                 {f.Pid}\n\nENDPOINT\nProtocol            {f.Protocol}\nLocal               {f.LocalEndpoint}\nRemote              {f.RemoteEndpoint}\nState               {f.State}\nObserved            {f.ObservedAt:yyyy-MM-dd HH:mm:ss zzz}\n\nINTERPRETATION\n{f.Explanation}\n\nLIMITS\nAn endpoint is not an HTTP request. Bytes, packets, DNS history and encrypted content are not captured here.";
    public string ServiceTechnicalDetails => SelectedService is not { } s ? "Select a service to inspect it." :
        $"SERVICE\nDisplay name        {s.DisplayName}\nSystem name         {s.Name}\nStatus              {s.Status}\nStartup             {s.StartType}\nHosting PID         {Field(s.ProcessId?.ToString())}\nExecutable          {Field(Processes.FirstOrDefault(p => p.Pid == s.ProcessId)?.ExecutablePath)}\n\nA hosting PID can contain multiple services. Windows may require administrator rights for control actions.";
    public string FolderTechnicalDetails => SelectedFolderRelationship is not { } r ? "Select a relationship to inspect it." :
        $"RELATIONSHIP\nKind                {r.Kind}\nConfidence          {r.Confidence}\nProcess             {r.ProcessName}\nPID                 {r.Process.Pid}\nStarted             {r.Process.StartTime:yyyy-MM-dd HH:mm:ss zzz}\nMatched path        {r.Path}\n\nOpen-file handles are not enumerated in this build.";
    public string DiskTechnicalDetails => SelectedDiskEntry is not { } d ? "Select a file or treemap tile to inspect it." :
        $"FILE\nName                {d.Name}\nPath                {d.Path}\nType                {Field(d.Extension)}\nModified            {d.ModifiedAt:yyyy-MM-dd HH:mm:ss zzz}\nLogical size        {ByteSize.Format(d.LogicalBytes)}\nPhysical estimate   {ByteSize.Format(d.PhysicalBytes)}\nAccess denied       {d.AccessDenied}\n\nPhysical size is estimated; hard links, sparse files and compression need deeper accounting.";
    public string FindingTechnicalDetails => SelectedFinding is not { } f ? "Select a finding to inspect its evidence." :
        $"ASSESSMENT\nProcess             {f.ProcessName}\nPID                 {f.Process.Pid}\nScore               {f.Score}/100\nSeverity            {f.Severity}\nConfidence          {f.Confidence:P0}\nAssessed            {f.AssessedAt:yyyy-MM-dd HH:mm:ss zzz}\n\nEVIDENCE\n{(f.Evidence.Count == 0 ? "No unusual evidence in this limited analysis." : string.Join("\n", f.Evidence.Select(e => $"{(e.IsRisk ? "+" : "-")} {e.Title} ({e.Score:+#;-#;0})\n  {e.Explanation}")))}\n\nA score is not a malware verdict.";
    public string ResourceTechnicalDetails => SelectedResourceSample is not { } r ? "Select a sample to inspect it." :
        $"SAMPLE\nTimestamp           {r.Timestamp:yyyy-MM-dd HH:mm:ss zzz}\nCPU                 {r.CpuPercent:N1}%\nPhysical used       {ByteSize.Format(r.PhysicalMemoryUsed)}\nPhysical total      {ByteSize.Format(r.PhysicalMemoryTotal)}\nMemory load         {r.MemoryPercent:N1}%\n\nDRIVES\n{string.Join("\n", r.Drives.Select(d => $"{d.Name}  {d.Format}  {d.UsedPercent:N1}%  {d.Summary}"))}";

    private static string Field(string? value) => string.IsNullOrWhiteSpace(value) ? "Not collected" : value;

    public MainWindowViewModel(IResourceCollector resources, IProcessCollector processes, INetworkCollector network,
        IServiceCollector services, IServiceController serviceController,
        IDiskScanner disk, IFolderRelationshipScanner folders, IThreatAnalyzer threats,
        IHistoryRepository history, IProcessController controller, IProcessDetailInspector processDetails,
        IComponentManager componentManager)
    {
        _resources = resources; _processes = processes; _network = network; _services = services;
        _serviceController = serviceController; _disk = disk;
        _folders = folders; _threats = threats; _history = history; _controller = controller;
        _componentManager = componentManager; _processDetails = processDetails;
        LoadPreferences();
        RefreshComponents();
        _ = RunAsync(_lifetime.Token);
    }

    private async Task RunAsync(CancellationToken token)
    {
        try
        {
            var historyAvailable = true;
            try
            {
                await _history.InitializeAsync(token);
                foreach (var sample in (await _history.ReadRecentAsync(60, token)).OrderBy(s => s.Timestamp)) RecentSamples.Add(sample);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException)
            {
                historyAvailable = false;
                PrivateSession = true;
                Status = "History unavailable in this folder; monitoring continues in memory.";
            }
            IReadOnlyList<ProcessSnapshot> processList = [];
            IReadOnlyList<NetworkFlow> flowList = [];
            IReadOnlyList<ServiceRecord> serviceList = [];
            IReadOnlyList<ThreatFinding> findings = [];
            var nextProcess = DateTimeOffset.MinValue;
            var nextNetwork = DateTimeOffset.MinValue;
            var nextService = DateTimeOffset.MinValue;
            var nextSecurity = DateTimeOffset.MinValue;
            var nextHistory = DateTimeOffset.MinValue;
            var nextPrune = DateTimeOffset.MinValue;
            while (!token.IsCancellationRequested)
            {
                if (SamplingPaused)
                {
                    await Dispatcher.UIThread.InvokeAsync(() => Status = "sampling paused · displayed values are frozen");
                    await Task.Delay(1000, token).ConfigureAwait(false);
                    continue;
                }
                var resourceInterval = FastMonitoring ? 2 : 4;
                var activityInterval = FastMonitoring ? 5 : 10;
                var backgroundInterval = FastMonitoring ? 30 : 60;
                var historyInterval = FastMonitoring ? 10 : 20;
                var resource = await Task.Run(async () => await _resources.CaptureAsync(token), token).ConfigureAwait(false);
                var now = resource.Timestamp;
                var processDue = now >= nextProcess;
                var networkDue = now >= nextNetwork;
                var serviceDue = now >= nextService;
                var securityDue = now >= nextSecurity;
                if (processDue)
                {
                    processList = await Task.Run(async () => await _processes.CaptureAsync(token), token).ConfigureAwait(false);
                    nextProcess = now.AddSeconds(activityInterval);
                }
                if (networkDue)
                {
                    flowList = await Task.Run(async () => await _network.CaptureAsync(token), token).ConfigureAwait(false);
                    nextNetwork = now.AddSeconds(activityInterval);
                }
                if (serviceDue)
                {
                    serviceList = await Task.Run(async () => await _services.CaptureAsync(token), token).ConfigureAwait(false);
                    nextService = now.AddSeconds(backgroundInterval);
                }
                if (securityDue)
                {
                    var top = processList.OrderByDescending(p => p.CpuPercent + p.WorkingSetBytes / 100_000_000d).Take(12);
                    var assessed = new List<ThreatFinding>();
                    foreach (var process in top) assessed.Add(await _threats.AnalyzeAsync(process, flowList, token).ConfigureAwait(false));
                    findings = assessed.OrderByDescending(f => f.Score).ToArray();
                    nextSecurity = now.AddSeconds(backgroundInterval);
                }

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    Current = resource;
                    IsLoading = false;
                    if (processDue)
                    {
                        _allProcessCount = processList.Count;
                        Replace(Processes, processList);
                        Replace(OverviewProcesses, processList.Take(12));
                        ApplyProcessFilter();
                    }
                    if (networkDue)
                    {
                        var selected = SelectedNetworkFlow;
                        _allEndpointCount = flowList.Count;
                        Replace(NetworkFlows, flowList);
                        Replace(OverviewNetworkFlows, flowList.OrderBy(f => f.State == "Established" ? 0 : 1).Take(12));
                        ApplyNetworkFilter();
                        EndpointCounts.Add(flowList.Count);
                        while (EndpointCounts.Count > 60) EndpointCounts.RemoveAt(0);
                        RefreshRelatedFlows();
                        OnPropertyChanged(nameof(NetworkSummary));
                        OnPropertyChanged(nameof(NetworkCollectorStatus));
                        SelectedNetworkFlow = selected is null ? null : VisibleNetworkFlows.FirstOrDefault(f =>
                            f.Pid == selected.Pid && f.Protocol == selected.Protocol &&
                            f.LocalEndpoint == selected.LocalEndpoint && f.RemoteEndpoint == selected.RemoteEndpoint);
                    }
                    if (serviceDue)
                    {
                        var selectedName = SelectedService?.Name;
                        Replace(Services, serviceList);
                        SelectedService = Services.FirstOrDefault(x => x.Name == selectedName);
                        OnPropertyChanged(nameof(ServiceStatusText));
                        RefreshRelatedFlows();
                    }
                    if (securityDue)
                    {
                        var selectedIdentity = SelectedFinding?.Process;
                        Replace(Findings, findings);
                        SelectedFinding = Findings.FirstOrDefault(f => f.Process == selectedIdentity);
                    }
                    RecentSamples.Add(resource);
                    while (RecentSamples.Count > 120) RecentSamples.RemoveAt(0);
                    Status = $"LIVE · resource {resourceInterval}s · process/network {activityInterval}s · services/security {backgroundInterval}s · {now:T}";
                    NotifySummary();
                });
                if (historyAvailable && !PrivateSession && now >= nextHistory)
                {
                    await _history.AppendAsync(resource, token).ConfigureAwait(false);
                    nextHistory = now.AddSeconds(historyInterval);
                    if (now >= nextPrune)
                    {
                        await _history.PruneAsync(TimeSpan.FromDays(7), 1_073_741_824, token).ConfigureAwait(false);
                        nextPrune = now.AddMinutes(15);
                    }
                }
                await Task.Delay(TimeSpan.FromSeconds(resourceInterval), token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { await Dispatcher.UIThread.InvokeAsync(() => { IsLoading = false; Status = $"Monitoring stopped: {ex.Message}"; }); }
    }

    public async Task ScanDiskAsync(string path)
    {
        if (IsDiskScanning) return;
        _scan?.Cancel(); _scan?.Dispose(); _scan = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        var token = _scan.Token;
        DiskPath = path; DiskEntries.Clear(); VisibleDiskEntries.Clear(); DiskTree.Clear(); DiskExtensions.Clear();
        _selectedDiskFolder = null; _selectedExtension = null; IsDiskScanning = true; Status = $"Scanning {path}…";
        try
        {
            var batches = Channel.CreateBounded<DiskEntry[]>(4);
            var producer = Task.Run(async () =>
            {
                try
                {
                    var batch = new List<DiskEntry>(256);
                    await foreach (var entry in _disk.ScanAsync(path, token))
                    {
                        batch.Add(entry);
                        if (batch.Count < 256) continue;
                        await batches.Writer.WriteAsync(batch.ToArray(), token);
                        batch.Clear();
                    }
                    if (batch.Count > 0) await batches.Writer.WriteAsync(batch.ToArray(), token);
                    batches.Writer.TryComplete();
                }
                catch (Exception ex) { batches.Writer.TryComplete(ex); }
            }, token);
            await foreach (var batch in batches.Reader.ReadAllAsync(token))
            {
                foreach (var item in batch) DiskEntries.Add(item);
                Status = $"Scanning · {DiskCount} · {DiskTotal}";
                NotifyDisk();
            }
            await producer;
            BuildDiskTree(path);
            foreach (var group in DiskEntries.Where(x => !x.IsDirectory).GroupBy(x => string.IsNullOrEmpty(x.Extension) ? "(no extension)" : x.Extension)
                .Select(g => new DiskExtensionSummary(g.Key, g.Count(), g.Sum(x => x.LogicalBytes)))
                .OrderByDescending(x => x.TotalBytes).Take(40)) DiskExtensions.Add(group);
            Status = $"Disk scan complete · {DiskCount} · {DiskTotal}"; NotifyDisk();
            ApplyDiskFilter();
        }
        catch (OperationCanceledException) { Status = "Disk scan cancelled"; }
        catch (Exception ex) { Status = $"Disk scan failed: {ex.Message}"; }
        finally { IsDiskScanning = false; }
    }

    public void CancelScan() => _scan?.Cancel();

    public async Task InspectFolderAsync(string path)
    {
        IsFolderScanning = true;
        try
        {
            Status = $"Finding processes related to {path}…";
            var results = await Task.Run(async () => await _folders.ScanAsync(path, _lifetime.Token));
            Replace(FolderRelationships, results);
            Status = $"Found {FolderRelationships.Count} executable or loaded-module relationships";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Status = $"Folder inspection failed: {ex.Message}"; }
        finally { IsFolderScanning = false; }
    }

    public Task<ActionAudit?> TerminateSelectedAsync(bool tree) => SelectedProcess is null
        ? Task.FromResult<ActionAudit?>(null)
        : CompleteActionAsync(_controller.TerminateAsync(SelectedProcess.Identity, tree, _lifetime.Token));

    public Task<ActionAudit?> RestartSelectedAsync() => SelectedProcess is null
        ? Task.FromResult<ActionAudit?>(null)
        : CompleteActionAsync(_controller.RestartAsync(SelectedProcess, _lifetime.Token));

    public async Task InspectSelectedProcessDeepAsync()
    {
        if (SelectedProcess is not { } snapshot) return;
        _deepInspect?.Cancel();
        var source = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _deepInspect = source;
        IsDeepInspecting = true;
        DeepProcessDetails = "Inspecting modules and hashing the executable on demand…";
        try
        {
            var report = await _processDetails.InspectAsync(snapshot, source.Token);
            if (SelectedProcess?.Identity == snapshot.Identity) DeepProcessDetails = report;
        }
        catch (OperationCanceledException)
        { if (SelectedProcess?.Identity == snapshot.Identity) DeepProcessDetails = "Deep inspection cancelled."; }
        catch (Exception ex)
        { if (SelectedProcess?.Identity == snapshot.Identity) DeepProcessDetails = $"Deep inspection limited: {ex.Message}"; }
        finally
        {
            if (ReferenceEquals(_deepInspect, source)) { _deepInspect = null; IsDeepInspecting = false; }
            source.Dispose();
        }
    }

    public async Task<ActionAudit?> ControlSelectedServiceAsync(string action)
    {
        if (IsServiceBusy || SelectedService is not { } selected) return null;
        IsServiceBusy = true;
        try
        {
            var result = await CompleteActionAsync(_serviceController.ControlAsync(selected.Name, action, _lifetime.Token));
            var services = await Task.Run(async () => await _services.CaptureAsync(_lifetime.Token));
            Replace(Services, services);
            SelectedService = Services.FirstOrDefault(s => s.Name == selected.Name);
            return result;
        }
        finally { IsServiceBusy = false; }
    }

    private void RefreshRelatedFlows()
    {
        Replace(ServiceNetworkFlows, SelectedService?.ProcessId is { } pid
            ? NetworkFlows.Where(f => f.Pid == pid) : []);
        Replace(ProcessNetworkFlows, SelectedProcess is { } process
            ? NetworkFlows.Where(f => f.Pid == process.Pid) : []);
        OnPropertyChanged(nameof(ServiceNetworkSummary));
    }

    private string PreferencesPath => Path.Combine(AppContext.BaseDirectory, "Data", "preferences.json");
    private void LoadPreferences()
    {
        try
        {
            if (!File.Exists(PreferencesPath)) return;
            var prefs = JsonSerializer.Deserialize<UiPreferences>(File.ReadAllText(PreferencesPath));
            if (prefs is null) return;
            UiScale = prefs.Scale; ShowDiagrams = prefs.Diagrams;
            CompactRows = prefs.Compact; FastMonitoring = prefs.Fast;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        { PreferencesStatus = "Saved preferences could not be read; using defaults."; }
    }
    private void SavePreferences()
    {
        if (PrivateSession) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PreferencesPath)!);
            var temp = PreferencesPath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(new UiPreferences(UiScale, ShowDiagrams, CompactRows, FastMonitoring)));
            File.Move(temp, PreferencesPath, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { PreferencesStatus = "This folder is read-only; preferences apply only to this session."; }
    }
    private sealed record UiPreferences(double Scale, bool Diagrams, bool Compact, bool Fast);

    private async Task<ActionAudit?> CompleteActionAsync(ValueTask<ActionAudit> action)
    {
        var result = await action;
        Status = result.Succeeded ? $"{result.Action} completed for {result.Target}" : $"{result.Action} failed: {result.Error}";
        return result;
    }

    public async Task ClearHistoryAsync() { await _history.ClearAsync(_lifetime.Token); RecentSamples.Clear(); Status = "Local history cleared"; }

    public void RefreshComponents() => Replace(Components, _componentManager.GetCatalog());

    public async Task<string> ChangeComponentAsync(ComponentManifest component)
    {
        try
        {
            string message;
            if (component.CanInstall)
            {
                var receipt = await _componentManager.InstallAsync(_componentManager.Preview(component.Id), _lifetime.Token);
                message = $"Installed {component.Name} at {receipt.InstalledAt:t}.";
            }
            else if (component.CanRemove)
            {
                var result = await _componentManager.UninstallAsync(component.Id, _lifetime.Token);
                message = result.Succeeded ? $"Removed {component.Name}." : $"Could not remove {component.Name}: {result.Error}";
            }
            else message = $"{component.Name} has no installable package in this build.";
            RefreshComponents(); Status = message; return message;
        }
        catch (Exception ex) { Status = ex.Message; RefreshComponents(); return ex.Message; }
    }

    private void ApplyProcessFilter()
    {
        var query = ProcessSearch.Trim();
        var selected = SelectedProcess?.Identity;
        Replace(VisibleProcesses, Processes.Where(p => query.Length == 0 || p.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
            || p.Pid.ToString().Contains(query, StringComparison.OrdinalIgnoreCase)
            || (p.ExecutablePath?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)));
        SelectedProcess = VisibleProcesses.FirstOrDefault(p => p.Identity == selected);
    }

    private void ApplyNetworkFilter()
    {
        var query = NetworkSearch.Trim();
        if (query.StartsWith("pid:", StringComparison.OrdinalIgnoreCase) && int.TryParse(query[4..], out var pid))
        {
            Replace(VisibleNetworkFlows, NetworkFlows.Where(f => f.Pid == pid));
            return;
        }
        Replace(VisibleNetworkFlows, NetworkFlows.Where(f => query.Length == 0 || f.ProcessName.Contains(query, StringComparison.OrdinalIgnoreCase)
            || f.Pid.ToString().Contains(query, StringComparison.OrdinalIgnoreCase)
            || f.LocalEndpoint.Contains(query, StringComparison.OrdinalIgnoreCase)
            || f.RemoteEndpoint.Contains(query, StringComparison.OrdinalIgnoreCase)
            || f.Protocol.Contains(query, StringComparison.OrdinalIgnoreCase)));
    }

    private void ApplyDiskFilter()
    {
        var folder = SelectedDiskFolder?.Path;
        var extension = SelectedExtension?.Extension;
        Replace(VisibleDiskEntries, DiskEntries.Where(entry => !entry.IsDirectory
            && (folder is null || entry.Path.StartsWith(folder.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
            && (extension is null || string.Equals(string.IsNullOrEmpty(entry.Extension) ? "(no extension)" : entry.Extension,
                extension, StringComparison.OrdinalIgnoreCase))).OrderByDescending(entry => entry.LogicalBytes).Take(5000));
    }

    private void BuildDiskTree(string root)
    {
        var normalizedRoot = Path.GetFullPath(root);
        if (!string.Equals(normalizedRoot, Path.GetPathRoot(normalizedRoot), StringComparison.OrdinalIgnoreCase))
            normalizedRoot = normalizedRoot.TrimEnd(Path.DirectorySeparatorChar);
        var nodes = new Dictionary<string, DiskTreeNode>(StringComparer.OrdinalIgnoreCase);
        var rootNode = new DiskTreeNode(normalizedRoot, Path.GetFileName(normalizedRoot) is { Length: > 0 } name ? name : normalizedRoot);
        nodes[normalizedRoot] = rootNode;
        foreach (var entry in DiskEntries)
        {
            var folder = entry.IsDirectory ? entry.Path : Path.GetDirectoryName(entry.Path);
            if (folder is null) continue;
            var stack = new Stack<string>();
            while (!nodes.ContainsKey(folder) && folder.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
            {
                stack.Push(folder);
                var parent = Path.GetDirectoryName(folder);
                if (parent is null || parent == folder) break;
                folder = parent;
            }
            while (stack.TryPop(out var current))
            {
                var parent = Path.GetDirectoryName(current) ?? normalizedRoot;
                if (!nodes.TryGetValue(parent, out var parentNode)) parentNode = rootNode;
                var node = new DiskTreeNode(current, Path.GetFileName(current));
                nodes[current] = node;
                parentNode.Children.Add(node);
            }
            if (entry.IsDirectory) continue;
            var parentPath = Path.GetDirectoryName(entry.Path);
            while (parentPath is not null && nodes.TryGetValue(parentPath, out var node))
            {
                node.TotalBytes += entry.LogicalBytes;
                if (parentPath.Equals(normalizedRoot, StringComparison.OrdinalIgnoreCase)) break;
                parentPath = Path.GetDirectoryName(parentPath);
            }
        }
        DiskTree.Add(rootNode);
    }

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> source)
    {
        if (target is RangeObservableCollection<T> range) range.ReplaceWith(source);
        else { target.Clear(); foreach (var item in source) target.Add(item); }
    }
    private void NotifySummary()
    { OnPropertyChanged(nameof(ProcessCount)); OnPropertyChanged(nameof(ConnectionCount)); OnPropertyChanged(nameof(FindingCount)); OnPropertyChanged(nameof(CpuText)); OnPropertyChanged(nameof(MemoryText)); OnPropertyChanged(nameof(MemoryPercent)); OnPropertyChanged(nameof(SelfImpactText)); }
    private void NotifyDisk() { OnPropertyChanged(nameof(DiskTotal)); OnPropertyChanged(nameof(DiskCount)); }
    private void Set<T>(ref T field, T value, [CallerMemberName] string? property = null)
    { if (EqualityComparer<T>.Default.Equals(field, value)) return; field = value; OnPropertyChanged(property); }
    private void OnPropertyChanged([CallerMemberName] string? property = null) => PropertyChanged?.Invoke(this, new(property));

    public async ValueTask DisposeAsync()
    { SavePreferences(); _lifetime.Cancel(); _scan?.Cancel(); _deepInspect?.Cancel(); await _history.DisposeAsync(); _scan?.Dispose(); _lifetime.Dispose(); }
}

public sealed record DiskExtensionSummary(string Extension, int FileCount, long TotalBytes)
{
    public string TotalText => ByteSize.Format(TotalBytes);
}

public sealed class DiskTreeNode(string path, string name)
{
    public string Path { get; } = path;
    public string Name { get; } = name;
    public long TotalBytes { get; set; }
    public string Display => $"{Name}  ·  {ByteSize.Format(TotalBytes)}";
    public ObservableCollection<DiskTreeNode> Children { get; } = [];
}
