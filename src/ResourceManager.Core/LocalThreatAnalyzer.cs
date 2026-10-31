namespace ResourceManager.Core;

public sealed class LocalThreatAnalyzer : IThreatAnalyzer
{
    private static readonly string[] UserWritableMarkers =
        ["\\temp\\", "\\downloads\\", "\\appdata\\local\\temp\\", "\\users\\public\\"];

    public ValueTask<ThreatFinding> AnalyzeAsync(
        ProcessSnapshot process,
        IReadOnlyList<NetworkFlow> flows,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var evidence = new List<ThreatEvidence>();
        var path = process.ExecutablePath?.ToLowerInvariant() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(process.ExecutablePath))
            evidence.Add(new("Executable unavailable", "Windows did not allow the executable path to be inspected.", 8, true));
        else if (UserWritableMarkers.Any(path.Contains))
            evidence.Add(new("Runs from a writable location", "The executable runs from a location commonly writable by ordinary applications.", 24, true));

        if (process.IsSigned == false)
            evidence.Add(new("Signature not trusted", "The executable does not currently have a verified publisher signature.", 22, true));
        else if (process.IsSigned == true)
            evidence.Add(new("Signed executable", "A publisher signature reduces risk but does not guarantee safety.", -12, false));

        var processFlows = flows.Where(f => f.Pid == process.Pid).ToArray();
        if (processFlows.Length > 25)
            evidence.Add(new("Many active endpoints", $"The process currently owns {processFlows.Length} network endpoints.", 12, true));

        if (process.CpuPercent > 80)
            evidence.Add(new("High CPU activity", "CPU use is unusually high in this sample.", 8, true));

        if (path.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.Windows).ToLowerInvariant()) && process.IsSigned != false)
            evidence.Add(new("Expected Windows location", "The process is running from the Windows directory.", -10, false));

        var score = Math.Clamp(10 + evidence.Sum(e => e.Score), 0, 100);
        var severity = score switch
        {
            >= 85 => RiskSeverity.Critical,
            >= 65 => RiskSeverity.High,
            >= 40 => RiskSeverity.Medium,
            >= 20 => RiskSeverity.Low,
            _ => RiskSeverity.Informational
        };
        var confidence = Math.Clamp(0.35 + evidence.Count * 0.09, 0.35, 0.9);
        return ValueTask.FromResult(new ThreatFinding(process.Identity, process.Name, score, severity, confidence, evidence, DateTimeOffset.Now));
    }
}
