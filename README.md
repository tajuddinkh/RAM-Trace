# RAM Trace V2.0

RAM Trace is a lightweight Windows memory-monitoring and memory-analysis application. It presents live physical RAM use, process-private memory, Windows service context and a reconciled view of how live memory is distributed across process and system categories.

## Current release

V2.0 is the current production release.

## Download

Use the [latest GitHub Release](https://github.com/tajuddinkh/RAM-Trace/releases/latest) for the current Windows binaries.

Release assets:

- [RAM_Trace.exe](https://github.com/tajuddinkh/RAM-Trace/releases/download/v2.0.0/RAM_Trace.exe), standalone application executable.
- [RAM_Trace_V2_0_0_Portable.zip](https://github.com/tajuddinkh/RAM-Trace/releases/download/v2.0.0/RAM_Trace_V2_0_0_Portable.zip), portable package.
- [SHA256.txt](https://github.com/tajuddinkh/RAM-Trace/releases/download/v2.0.0/SHA256.txt), authoritative release checksums.

The complete production source is available directly under src/.

## Main features

- Live physical RAM use with daily average, peak and low values
- Per-process Private Working Set monitoring
- Familiar and technical process names
- Windows service mapping and grouped svchost presentation
- Memory Breakdown for Windows system, Microsoft applications, third-party applications and conservative Unclassified ownership
- Kernel / system / shared memory reconciliation
- Full LIVE RAM reconciliation
- Daily local history
- Process Information and embedded Process Guide
- Incremental process-grid updating
- Responsive native WinForms interface
- System-tray operation
- Optional local diagnostics
- Resource-aware CPU throttling

## Understanding the memory figures

RAM Trace uses Private Working Set for per-process accounting. Process-private RAM is classified by ownership where evidence is sufficiently reliable. When ownership cannot be determined confidently, the process remains Unclassified rather than being forced into a more specific category.

Kernel, system and shared memory are kept separate from process ownership. The Memory Breakdown reconciles process-private memory with those system components to the displayed LIVE RAM total.

## Platform

- Windows 10/11
- .NET Framework 4.8
- Native WinForms interface
- No installer required for the portable release

## Portable use

Extract RAM_Trace_V2_0_0_Portable.zip and run RAM_Trace.exe. RAM Trace stores its normal local state and history under the current Windows user profile.

## Windows security and integrity

RAM Trace is currently unsigned. If Windows displays a reputation or SmartScreen warning, verify the downloaded file against SHA256.txt.

## Build from source

The production source and resources are in the src directory.

Open src/RAMTrace.sln in Visual Studio and build the Release configuration, or run src/build.cmd from a compatible Windows development environment with .NET Framework 4.8 available.

## Offline operation and privacy

RAM Trace does not send telemetry, access the network, modify running processes, or require administrator privileges.

Diagnostics are OFF by default and, when enabled, are stored locally.

## Release integrity

Authoritative release hashes are recorded in SHA256.txt.

## Project licence

RAM Trace is released under the [MIT License](LICENSE).

## Author

Designed and developed by Tajud Din.
