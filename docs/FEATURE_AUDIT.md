# Feature audit — 16 October 2026

This is a development build, not a completed version of the original specification. “Working” below means previously exercised on the local host; new features are distinguished from verified workflows below. It does not mean validated on all Windows versions and architectures.

| Area | Status | What this build actually does |
| --- | --- | --- |
| Portable launch | Working on Windows x64 | Self-contained ZIP launch requires no runtime or installer. ARM64 is publishable but not device-tested. Packages are unsigned. |
| Interface | Partial | Redesigned graphite/green console, left navigation with local vector icons, compact lowercase labels, CPU/memory chart, endpoint-count chart, clickable endpoint diagrams, disk treemap and detail panels. Saved UI scale (85–125%), row density, diagram visibility and fast refresh preferences. Full keyboard/screen-reader, high-contrast, 200% scaling, theme, and color-vision audits remain. |
| CPU/memory/drive monitoring | Working | Live system CPU, physical memory, and mounted-drive usage. No per-device disk rates, GPU, sensors, or hardware telemetry. |
| Processes | Partial | Live name, PID, CPU, working set, path/start time when accessible; terminate and guarded restart. On-demand SHA-256, version metadata and loaded-module inspection. Missing full command lines, verified signatures, tree, suspend/resume, graceful close, complete auditing, and elevated helper. Restart does not restore original arguments. |
| Services | Partial | Live status, startup type, hosting PID; user-confirmed start/stop/pause/continue plus restart that waits for stopped/running states. Service inspector lists its host PID and TCP/UDP endpoints with a connection diagram. Shared-host attribution is explicitly described; terminate-host previews all hosted services and uses process protection. Administrator-only actions report access denied. No full dependency preview or disposable-service integration test yet. |
| Network | Partial | Live IPv4 TCP/UDP endpoint-to-PID/process mapping and filtering, exact pid: filters, endpoint-count trend and a bounded six-row connection diagram. These are sampled endpoints, not throughput, packet counts or a geographical map. No IPv6, per-flow bytes, packets, short-lived capture, DNS timeline, HTTP request counts, WFP driver, firewall action, or connection close. |
| Folder inspector | Partial | Process executable and loaded-module relationships. No open-handle scan, mapped-file proof, or confidence-rich provenance. |
| Disk analyzer | Partial | Recursive file scan, directory tree, extension summary, largest-file treemap, selection/filtering, cancellation, scan progress indicator and bounded background scan batches. No complete hard-link/sparse/compression accounting or Recycle Bin cleanup. |
| Security | Partial | Local explainable heuristic risk findings for selected live processes. No trained AI model, Defender scanning/quarantine, persistence analysis, or signature-validation pipeline. Findings are not malware verdicts. |
| History | Partial | Local SQLite resource samples, seven-day retention and best-effort 1 GB pruning (not a strict total database/WAL size cap), memory-only private session, clear action. No network/process timelines, export, saved views, or comprehensive action audit. |
| Capabilities Center | Partial | Per-user desktop copy, Explorer menu, and start-with-Windows can be installed/removed in app. Capture driver, background monitoring service, automatic updater are unavailable because no signed tested packages exist. Installation is not yet transactional and has no production uninstaller. |
| Production security | Not implemented | No code signing, authenticated elevated broker, signed manifest validation, WFP driver, rollback receipts, or tamper testing. |

## Why a capability can say “Unavailable”

The status means this build contains no safe, verified installation payload for it. It does **not** mean Windows has a setting the user must turn on. A capture driver and background service need their own implementations, signed packages, privilege boundaries, installer/uninstaller, and Windows 10/11 testing. The app must not imply they were installed or monitoring when they were not.

## Verification scope — current changes

- App compilation completed with zero warnings/errors, and a self-contained Windows x64 build was published.
- The new build launched on the host. Its accessibility tree showed the new sidebar, lowercase labels, live resource data, 196 processes and 142 IPv4 endpoints. This confirms startup and collected data; it is not a complete visual or interaction acceptance test.
- The screenshot returned a locked Windows desktop. Further mouse/keyboard testing was paused. Final service/diagram clicks, preference persistence and visual scaling checks remain unverified.
- Direct smoke checks passed: resource/process collection, own-process identification, the seven-card capability catalog, disk enumeration, risk-score bounds, executable hash/module report, rejection of self termination and rejection of invalid/critical service restart.
- The smoke process runs inside a restricted command environment: it saw three processes, zero endpoints and permission-limited services. Those results do not validate live endpoint attribution or privileged service control.
- The xUnit test runner built the test project, but failed to connect to its testhost after 90 seconds. That test run was aborted, not passed.
- No real service was stopped/restarted or host terminated during this audit. Restart success, cancellation/timeout behavior and service dependencies need a disposable-service integration fixture.
- No installer, registry, driver, firewall or startup modifications were made by this audit. Component installation/removal still needs clean-machine testing.
- ARM64 packaging is separate from hardware verification. There is no ARM64 device result.
- A startup accessibility snapshot reported roughly 224 MB RAM and 6.9% CPU for this app while instrumentation was active. This is not an idle benchmark or evidence that the lightweight goal is met.
- The original goal is **not achieved**. No claim is made that every original acceptance test has passed.

## Changes intended to reduce monitoring overhead

Lean mode samples resources every 4 seconds, process/network tables every 10 seconds, and services/rules every 60 seconds. Capture time can extend the effective interval. Fast mode requests 2/5/30 seconds. History writes occur every 20 seconds by default. Process metadata and endpoint-owner names are cached; collection replacements emit one table notification; overview tables display 12 rows; endpoint history keeps 60 samples and diagrams draw six filtered endpoints without animation timers. File hashes/module details remain on demand. Disk enumeration runs in bounded background batches.

## Remaining release blockers

The signed elevated broker, production installer/uninstaller and rollback receipts, signature/hash-verified update pipeline, background collector, capture driver, IPv6 and per-process network rates, Defender integration, trained local model, process ancestry/user/command-line completeness, persistent action audit, exports, comprehensive disk allocation accounting and full accessibility testing are still outstanding. Certificates, driver signing and clean Windows 10/11 x64/ARM64 validation cannot be substituted by interface buttons.

