# Architecture

The solution separates platform-neutral records and contracts from Windows collection and the Avalonia presentation layer.

- `ResourceManager.Core`: immutable telemetry records, collector/control contracts, risk explanations, and component manifests.
- `ResourceManager.Infrastructure.Windows`: Windows resource, process, IPv4 network, folder, disk, process-control, and SQLite adapters.
- `ResourceManager.App`: portable Avalonia shell, design system, guarded user actions, treemap, and live orchestration.

Collector failures are expected operational states. A collector must report unavailable information instead of synthesizing zeros or claiming success.

## Privileged boundary

The future elevated broker is a separate signed executable. It must authenticate an ephemeral named pipe, accept only versioned allowlisted request records, revalidate process PID plus start time, and exit when the UI disconnects. The portable UI must never execute arbitrary commands through it.

## Installable components

The Capabilities Center is driven by signed manifests and installation receipts. Development builds disclose intended changes but cannot install placeholder binaries. Production enablement requires signed offline payloads, atomic staging, pre-elevation hash/signature checks, receipts, rollback, repair, and complete removal tests.
