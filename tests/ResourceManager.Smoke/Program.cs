using ResourceManager.Core;
using ResourceManager.Infrastructure.Windows;
using System.Net;
using System.Net.Sockets;

if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Windows smoke test only.");

if (args.Contains("--network-check"))
{
    foreach (var address in new[] { IPAddress.Loopback, IPAddress.IPv6Loopback })
    {
        var listener = new TcpListener(address, 0);
        listener.Start();
        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            using var client = new TcpClient(address.AddressFamily);
            await client.ConnectAsync(address, port);
            using var peer = await listener.AcceptTcpClientAsync();
            using var udp = new UdpClient(new IPEndPoint(address, 0));
            var udpPort = ((IPEndPoint)udp.Client.LocalEndPoint!).Port;
            var collector = new WindowsNetworkCollector();
            var endpoints = await collector.CaptureAsync();
            if (collector.State != CollectorState.Active) throw new InvalidOperationException(collector.StatusMessage);
            foreach (var expected in new[] { (Protocol: "TCP", Port: port), (Protocol: "UDP", Port: udpPort) })
                if (!endpoints.Any(f => f.Pid == Environment.ProcessId && f.Protocol == expected.Protocol && f.LocalPort == expected.Port && f.LocalAddress == address.ToString()))
                    throw new InvalidOperationException($"Missing ownership for {address} {expected.Protocol}:{expected.Port}");
            Console.WriteLine($"OK {address.AddressFamily}: TCP connection and UDP bound port attributed to PID {Environment.ProcessId}");
        }
        finally { listener.Stop(); }
    }
    return;
}

var exportFixture = new ExportTable("fixture", ["value"], [["line1\nline2"], ["name,with\"quotes"], [" =1+1"]]);
var csv = SnapshotExport.Csv(exportFixture);
if (!csv.Contains("\"name,with\"\"quotes\"") || !csv.Contains("\"' =1+1\"") || !csv.Contains("\"line1\nline2\""))
    throw new InvalidOperationException("CSV escaping or formula neutralization failed.");
using (var json = System.Text.Json.JsonDocument.Parse(SnapshotExport.Json(exportFixture)))
    if (json.RootElement.GetProperty("rows").GetArrayLength() != 3) throw new InvalidOperationException("JSON export lost rows.");
var ipv6 = new NetworkFlow("TCP", "::1", 443, "fe80::1%12", 8080, "Established", 7, "fixture", DateTimeOffset.Now);
if (ipv6.LocalEndpoint != "[::1]:443" || ipv6.RemoteEndpoint != "[fe80::1%12]:8080")
    throw new InvalidOperationException("IPv6 endpoint formatting is ambiguous.");
Console.WriteLine("OK exports: CSV quotes/newlines/formula protection, JSON rows, IPv6 endpoint formatting");

var resources = await new WindowsResourceCollector().CaptureAsync();
var processCollector = new WindowsProcessCollector();
_ = await processCollector.CaptureAsync();
await Task.Delay(150);
var processes = await processCollector.CaptureAsync();
var flows = await new WindowsNetworkCollector().CaptureAsync();
var serviceCollector = new WindowsServiceManager();
var services = await serviceCollector.CaptureAsync();
var components = new LocalComponentManager().GetCatalog();
var diskEntries = 0;
await foreach (var entry in new WindowsDiskScanner().ScanAsync(AppContext.BaseDirectory))
{
    if (!entry.IsDirectory) diskEntries++;
    if (diskEntries >= 20) break;
}

if (resources.PhysicalMemoryTotal <= 0) throw new InvalidOperationException("Physical memory was not collected.");
if (processes.Count == 0) throw new InvalidOperationException("No processes were collected.");
if (!processes.Any(x => x.Pid == Environment.ProcessId)) throw new InvalidOperationException("The smoke process was not identified.");
if (services.Count == 0 && serviceCollector.State == CollectorState.Active)
    throw new InvalidOperationException("Service collector reported success but returned no services.");
if (components.Count != 7) throw new InvalidOperationException("The capability catalog is incomplete.");
if (diskEntries == 0) throw new InvalidOperationException("The disk scanner returned no files from its own folder.");

var current = processes.First(x => x.Pid == Environment.ProcessId);
var protectedResult = await new WindowsProcessController().TerminateAsync(current.Identity, false);
if (protectedResult.Succeeded) throw new InvalidOperationException("Own-process guard failed.");
var invalidService = await serviceCollector.ControlAsync("invalid\\service", "Restart");
var protectedService = await serviceCollector.ControlAsync("RpcSs", "Restart");
if (invalidService.Succeeded || protectedService.Succeeded)
    throw new InvalidOperationException("Service restart guards failed.");
var finding = await new LocalThreatAnalyzer().AnalyzeAsync(current, flows);
var deepReport = await new WindowsProcessDetailInspector().InspectAsync(current);
if (finding.Score is < 0 or > 100) throw new InvalidOperationException("Risk score is outside its contract.");
if (!deepReport.Contains("LOADED MODULES", StringComparison.Ordinal))
    throw new InvalidOperationException("On-demand process details were not produced.");

Console.WriteLine($"OK resources: CPU {resources.CpuPercent:N1}%, memory {resources.MemorySummary}");
Console.WriteLine($"OK processes: {processes.Count:N0}, current PID {current.Pid}");
Console.WriteLine($"OK network capture invocation: {flows.Count:N0} IPv4/IPv6 TCP/UDP endpoints (ownership requires --network-check)");
Console.WriteLine($"Services: {serviceCollector.State}, {services.Count:N0} services, {services.Count(x => x.ProcessId is not null):N0} with PIDs");
Console.WriteLine($"OK capabilities: {components.Count} cards, {components.Count(x => x.CanInstall)} installable here");
Console.WriteLine($"OK disk scan: at least {diskEntries} files found in the smoke test folder");
Console.WriteLine($"OK threat contract: {finding.Score}/100 {finding.Severity}, {finding.Evidence.Count} evidence items");
Console.WriteLine($"OK deep process inspector: {deepReport.Length:N0} report characters");
Console.WriteLine("OK control safeguards: self termination, invalid service and critical-service restart rejected");
