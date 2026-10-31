using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using System.Globalization;
using System.Text;
using ResourceManager.Core;

namespace ResourceManager.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        SizeChanged += (_, _) => ApplyResponsiveLayout();
        MainTabs.SelectionChanged += (_, e) =>
        {
            if (e.Source == MainTabs) Dispatcher.UIThread.Post(ApplyResponsiveLayout);
        };
    }

    private void ApplyResponsiveLayout()
    {
        var narrow = Bounds.Width < 1350;
        OverviewLayout.ColumnDefinitions = new ColumnDefinitions(narrow ? "220,*" : "230,*,260");
        OverviewLayout.RowDefinitions = new RowDefinitions(narrow ? "Auto,Auto,Auto" : "Auto,*");
        Grid.SetRow(OverviewSide, narrow ? 2 : 1);
        Grid.SetColumn(OverviewSide, narrow ? 0 : 2);
        Grid.SetColumnSpan(OverviewSide, narrow ? 2 : 1);
        foreach (var grid in this.GetLogicalDescendants().OfType<Grid>().Where(g => g.Classes.Contains("inspector-layout")))
        {
            var stacked = Bounds.Width < 1200;
            grid.ColumnDefinitions = new ColumnDefinitions(stacked ? "*" : "*,360");
            grid.RowDefinitions = new RowDefinitions(stacked ? "*,190" : "*");
            if (grid.Children.Count < 2) continue;
            Grid.SetRow(grid.Children[1], stacked ? 1 : 0);
            Grid.SetColumn(grid.Children[1], stacked ? 0 : 1);
        }
    }
    private MainWindowViewModel ViewModel => (MainWindowViewModel)DataContext!;
    public string? InitialInspectPath { get; set; }

    protected override async void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        ApplyResponsiveLayout();
        if (InitialInspectPath is not { } path) return;
        InitialInspectPath = null;
        if (File.Exists(path)) path = Path.GetDirectoryName(path)!;
        if (!Directory.Exists(path)) return;
        MainTabs.SelectedIndex = 4;
        await ViewModel.InspectFolderAsync(path);
    }

    private async void ScanDisk_Click(object? sender, RoutedEventArgs e)
    {
        var path = await ChooseFolderAsync("Choose a folder to analyze");
        if (path is not null) await ViewModel.ScanDiskAsync(path);
    }

    private async void InspectFolder_Click(object? sender, RoutedEventArgs e)
    {
        var path = await ChooseFolderAsync("Choose a folder to inspect");
        if (path is not null) await ViewModel.InspectFolderAsync(path);
    }

    private void CancelScan_Click(object? sender, RoutedEventArgs e) => ViewModel.CancelScan();
    private void OpenCapabilities_Click(object? sender, RoutedEventArgs e) => MainTabs.SelectedIndex = 8;
    private void ProcessGrid_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    { if (sender is DataGrid { SelectedItem: ProcessSnapshot item }) ViewModel.SelectedProcess = item; }
    private void NetworkGrid_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    { if (sender is DataGrid { SelectedItem: NetworkFlow item }) ViewModel.SelectedNetworkFlow = item; }
    private void ServiceGrid_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    { if (sender is DataGrid { SelectedItem: ServiceRecord item }) ViewModel.SelectedService = item; }
    private void FolderGrid_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    { if (sender is DataGrid { SelectedItem: FolderRelationship item }) ViewModel.SelectedFolderRelationship = item; }
    private void DiskGrid_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    { if (sender is DataGrid { SelectedItem: DiskEntry item }) ViewModel.SelectedDiskEntry = item; }
    private void FindingGrid_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    { if (sender is DataGrid { SelectedItem: ThreatFinding item }) ViewModel.SelectedFinding = item; }
    private void HistoryGrid_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    { if (sender is DataGrid { SelectedItem: ResourceSample item }) ViewModel.SelectedResourceSample = item; }
    private async void DeepInspectProcess_Click(object? sender, RoutedEventArgs e)
        => await ViewModel.InspectSelectedProcessDeepAsync();

    private void OpenProcessPorts_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedProcess is not { } process) return;
        ViewModel.NetworkSearch = $"pid:{process.Pid}";
        ViewModel.SelectedNetworkFlow = ViewModel.VisibleNetworkFlows.FirstOrDefault();
        MainTabs.SelectedIndex = 2;
    }

    private void OpenNetworkOwner_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedNetworkFlow is not { } flow) return;
        ViewModel.ProcessSearch = flow.Pid.ToString();
        ViewModel.SelectedProcess = ViewModel.Processes.FirstOrDefault(p => p.Pid == flow.Pid);
        MainTabs.SelectedIndex = 1;
    }

    private void OpenServiceHost_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedService?.ProcessId is not { } pid) return;
        ViewModel.ProcessSearch = pid.ToString();
        ViewModel.SelectedProcess = ViewModel.Processes.FirstOrDefault(p => p.Pid == pid);
        MainTabs.SelectedIndex = 1;
    }

    private void OpenFindingProcess_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedFinding is not { } finding) return;
        ViewModel.ProcessSearch = finding.Process.Pid.ToString();
        ViewModel.SelectedProcess = ViewModel.Processes.FirstOrDefault(p => p.Identity == finding.Process);
        MainTabs.SelectedIndex = 1;
    }

    private async void InspectSelectedDiskFolder_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedDiskEntry is not { } entry) return;
        var path = entry.IsDirectory ? entry.Path : Path.GetDirectoryName(entry.Path);
        if (path is null) return;
        MainTabs.SelectedIndex = 4;
        await ViewModel.InspectFolderAsync(path);
    }

    private void DiskTreemap_EntrySelected(object? sender, DiskEntry entry)
    {
        ViewModel.SelectedDiskEntry = entry;
    }

    private async Task<string?> ChooseFolderAsync(string title)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = title, AllowMultiple = false });
        return folders.Count == 0 ? null : folders[0].Path.LocalPath;
    }

    private async void TerminateProcess_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedProcess is not { } process) { await Notice("Select a process", "Select a process before using a control action."); return; }
        if (process.Pid is 0 or 4 || process.Pid == Environment.ProcessId)
        { await Notice("Protected target", "Resource Manager will not terminate the System process or itself."); return; }

        var confirmed = await Confirm("Terminate process?",
            $"{process.Name} ({process.Pid}) will be stopped immediately. Unsaved data may be lost. This cannot be undone.", "Terminate");
        if (confirmed && await ViewModel.TerminateSelectedAsync(false) is { Succeeded: false } result)
            await Notice("Process action failed", result.Error ?? "Windows rejected the action.");
    }

    private async void RestartProcess_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedProcess is not { } process) { await Notice("Select a process", "Select a process before using a control action."); return; }
        var confirmed = await Confirm("Restart process?",
            $"{process.Name} ({process.Pid}) will be terminated and relaunched from its executable path. Original command-line arguments and working directory are not restored in this build. Unsaved data may be lost.", "Restart");
        if (confirmed && await ViewModel.RestartSelectedAsync() is { Succeeded: false } result)
            await Notice("Process action failed", result.Error ?? "Windows rejected the action.");
    }

    private async Task ControlServiceAsync(string action)
    {
        if (ViewModel.SelectedService is not { } service) { await Notice("Select a service", "Select a service first."); return; }
        if (!await Confirm($"{action} {service.DisplayName}?",
            $"Service: {service.Name}\nCurrent state: {service.Status}\nPID: {service.ProcessId?.ToString() ?? "none"}\n\nChanging a service can interrupt applications that depend on it.", action)) return;
        var result = await ViewModel.ControlSelectedServiceAsync(action);
        if (result is { Succeeded: false }) await Notice("Service action failed", result.Error ?? "Windows rejected the action.");
    }

    private async void ServiceStart_Click(object? sender, RoutedEventArgs e) => await ControlServiceAsync("Start");
    private async void ServiceStop_Click(object? sender, RoutedEventArgs e) => await ControlServiceAsync("Stop");
    private async void ServicePause_Click(object? sender, RoutedEventArgs e) => await ControlServiceAsync("Pause");
    private async void ServiceContinue_Click(object? sender, RoutedEventArgs e) => await ControlServiceAsync("Continue");
    private async void ServiceRestart_Click(object? sender, RoutedEventArgs e) => await ControlServiceAsync("Restart");

    private void NetworkMap_FlowSelected(object? sender, NetworkFlow flow)
    {
        ViewModel.SelectedNetworkFlow = flow;
        MainTabs.SelectedIndex = 2;
    }

    private async void TerminateServiceHost_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedService?.ProcessId is not { } pid)
        { await Notice("No running host", "Select a service with a running host process."); return; }
        var process = ViewModel.Processes.FirstOrDefault(p => p.Pid == pid);
        if (process is null) { await Notice("Host unavailable", "The host exited or is not accessible. Wait for the next capture."); return; }
        var hosted = ViewModel.Services.Where(s => s.ProcessId == pid).Select(s => s.DisplayName).ToArray();
        if (!await Confirm("Terminate service host?", $"{process.Name} · pid {pid}\n\nAll services in this process may stop:\n{string.Join("\n", hosted)}\n\nUnsaved work and dependent applications may be affected. Use Stop to target one service.", "Terminate host")) return;
        ViewModel.SelectedProcess = process;
        if (await ViewModel.TerminateSelectedAsync(false) is { Succeeded: false } result)
            await Notice("Host termination failed", result.Error ?? "Windows rejected the action.");
    }

    private async void ClearHistory_Click(object? sender, RoutedEventArgs e)
    {
        if (await Confirm("Clear local history?", "All stored resource samples will be removed from the portable database.", "Clear history"))
            await ViewModel.ClearHistoryAsync();
    }

    private async void ExportView_Click(object? sender, RoutedEventArgs e)
    {
        static string[] Row(params object?[] values) => values.Select(v => v is DateTimeOffset date
            ? date.ToString("O") : Convert.ToString(v, CultureInfo.InvariantCulture) ?? "").ToArray();
        var vm = ViewModel;
        ExportTable? table = MainTabs.SelectedIndex switch
        {
            0 or 7 => new("resource samples", ["timestamp", "cpu_percent", "memory_used_bytes", "memory_total_bytes"],
                vm.RecentSamples.Select(r => Row(r.Timestamp, r.CpuPercent, r.PhysicalMemoryUsed, r.PhysicalMemoryTotal)).ToArray()),
            1 => new("filtered processes", ["pid", "started", "name", "path", "cpu_percent", "working_set_bytes", "threads", "handles"],
                vm.VisibleProcesses.Select(p => Row(p.Pid, p.Identity.StartTime, p.Name, p.ExecutablePath, p.CpuPercent, p.WorkingSetBytes, p.ThreadCount, p.HandleCount)).ToArray()),
            2 => new("filtered network endpoints", ["observed", "pid", "process", "protocol", "ip_family", "local", "remote", "state"],
                vm.VisibleNetworkFlows.Select(f => Row(f.ObservedAt, f.Pid, f.ProcessName, f.Protocol, f.AddressFamily, f.LocalEndpoint, f.RemoteEndpoint, f.State)).ToArray()),
            3 => new("services", ["service", "display_name", "status", "host_pid", "startup", "host_endpoints"],
                vm.Services.Select(s => Row(s.Name, s.DisplayName, s.Status, s.ProcessId, s.StartType,
                    string.Join("; ", vm.NetworkFlows.Where(f => f.Pid == s.ProcessId).Select(f => $"{f.Protocol} {f.LocalEndpoint} -> {f.RemoteEndpoint}")))).ToArray()),
            4 => new("folder relationships", ["pid", "started", "process", "path", "relationship", "confidence"],
                vm.FolderRelationships.Select(r => Row(r.Process.Pid, r.Process.StartTime, r.ProcessName, r.Path, r.Kind, r.Confidence)).ToArray()),
            5 => new("visible disk files (up to 5000)", ["path", "logical_bytes", "physical_estimate_bytes", "modified"],
                vm.VisibleDiskEntries.Select(d => Row(d.Path, d.LogicalBytes, d.PhysicalBytes, d.ModifiedAt)).ToArray()),
            6 => new("security findings", ["pid", "process", "score", "severity", "confidence", "evidence"],
                vm.Findings.Select(f => Row(f.Process.Pid, f.ProcessName, f.Score, f.Severity, f.Confidence, f.Explanation)).ToArray()),
            _ => null
        };
        if (table is null) { await Notice("Select a data view", "Open processes, network, services, folders, disk, security or history to export its current rows."); return; }
        try
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = $"Export {table.Scope}", SuggestedFileName = $"resource-manager-{DateTime.Now:yyyyMMdd-HHmmss}.json",
                DefaultExtension = "json", ShowOverwritePrompt = true,
                FileTypeChoices = [new FilePickerFileType("JSON") { Patterns = ["*.json"] }, new FilePickerFileType("CSV") { Patterns = ["*.csv"] }]
            });
            if (file is null) return;
            var csv = file.Name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase);
            var content = await Task.Run(() => csv ? SnapshotExport.Csv(table) : SnapshotExport.Json(table));
            await using (var stream = await file.OpenWriteAsync())
            {
                stream.SetLength(0);
                await using var writer = new StreamWriter(stream, new UTF8Encoding(true));
                await writer.WriteAsync(content);
            }
            await Notice("Export complete", $"Saved {table.Rows.Length:N0} rows from {table.Scope}. Exports contain local paths and connection details; review them before sharing.");
        }
        catch (Exception ex) { await Notice("Export failed", ex.Message); }
    }

    private async void ReviewComponent_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: ComponentManifest component }) return;
        var changes = string.Join(Environment.NewLine, component.SystemChanges.Select(x => "• " + x));
        var privacy = $"Uses network: {(component.UsesNetwork ? "Yes" : "No")}\nStarts automatically: {(component.StartsAutomatically ? "Yes" : "No")}\nRequires administrator: {(component.RequiresElevation ? "Yes" : "No")}";
        var availability = component.State == ComponentState.Unavailable
            ? component.UnavailableReason ?? "This component has no installable package in this build."
            : component.State == ComponentState.Installed ? "This component is currently installed." : "This component can be installed here.";
        await Notice(component.Name, $"Version {component.Version} · {component.Publisher}\n\nSystem changes\n{changes}\n\n{privacy}\n\n{availability}");
    }

    private async void ChangeComponent_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: ComponentManifest component } || !component.CanChange) return;
        var action = component.CanInstall ? "Install" : "Remove";
        var changes = string.Join(Environment.NewLine, component.SystemChanges.Select(x => "• " + x));
        if (!await Confirm($"{action} {component.Name}?", $"This will make these changes for your Windows user account:\n\n{changes}", action)) return;
        await Notice(component.Name, await ViewModel.ChangeComponentAsync(component));
    }

    private Task Notice(string title, string message) => ShowDialog(title, message, null);
    private async Task<bool> Confirm(string title, string message, string action)
        => await ShowDialog(title, message, action) ?? false;

    private async Task<bool?> ShowDialog(string title, string message, string? action)
    {
        var dialog = new Window
        {
            Title = title, Width = 520, SizeToContent = SizeToContent.Height, CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        var close = new Button { Content = action is null ? "Close" : "Cancel" };
        close.Click += (_, _) => dialog.Close(false); buttons.Children.Add(close);
        if (action is not null)
        {
            var confirm = new Button { Content = action, Classes = { "danger" } };
            confirm.Click += (_, _) => dialog.Close(true); buttons.Children.Add(confirm);
        }
        dialog.Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(24), Spacing = 18,
            Children =
            {
                new TextBlock { Text = title, FontSize = 20, FontWeight = Avalonia.Media.FontWeight.SemiBold },
                new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap }, buttons
            }
        };
        return await dialog.ShowDialog<bool?>(this);
    }
}
