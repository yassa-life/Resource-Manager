# Resource Manager

A portable, open-source Windows resource manager for exploring processes, services, network activity, disks, system resources, history, and explainable security findings—all from one nerd-friendly dashboard.

Resource Manager is currently a portable-first Windows diagnostics prototype. It has live process, resource, service, IPv4 TCP/UDP ownership, disk, folder relationship, local history, and explainable risk screens. It does **not** yet meet the full specification. See [the feature audit](docs/FEATURE_AUDIT.md).

## Run locally

```powershell
./.tools/dotnet/dotnet restore ResourceManager.slnx
./.tools/dotnet/dotnet run --project src/ResourceManager.App
```

The app creates `Data/history.db` beside the executable when that folder is writable. Enable **Private session** to avoid adding samples; unwritable folders fall back to memory-only monitoring.

The console uses a compact left sidebar. Select a process, endpoint, service, file or finding for raw details. Network includes a sampled endpoint-count trend and a clickable connection diagram; service details show ports owned by its hosting PID. Shared hosts cannot attribute a port to one individual service.

Settings offers interface scale, compact/comfortable rows, diagram visibility and faster monitoring. Preferences are saved to `Data/preferences.json` on exit except in a private session. Lean mode requests resources every 4 seconds, process/network captures every 10 seconds and service/security captures every 60 seconds. Deep file/module inspection is on demand.

## Build portable packages

```powershell
./scripts/publish-portable.ps1
```

Unsigned development packages are created under `artifacts/portable`. Production packages require signing for the app, helper, service, updater, and capture driver before installation capabilities can be enabled.

## Safety boundaries

- Runs without administrator access by default.
- Never silently installs a driver, service, certificate, startup entry, or firewall rule. The user can explicitly add per-user startup and Explorer integration from the Capabilities Center.
- Does not decrypt TLS or label connection counts as HTTP request counts.
- Never uploads files or automatically acts on a security score.
- Driver, monitoring-service, and updater cards are marked unavailable until verified packages exist.

## Current limitations

- Portable network ownership currently covers IPv4 TCP/UDP tables. IPv6 and ETW enrichment belong in the next Windows collector increment.
- Service controls are implemented through the Windows Service Control Manager, but protected actions do not yet offer on-demand elevation. Open-handle enumeration, Defender orchestration, suspend/resume, signed-publisher verification, and the elevated named-pipe broker are not active.
- The local risk engine is intentionally explainable and conservative; it is not a malware verdict.
