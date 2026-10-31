# win-disk-health

Read-only Windows physical-disk health and operational-state review for support engineers and administrators. Quickly identify provider-reported Warning, Unhealthy, degraded, predictive-failure, or communication states. Text and JSON use the same rules; saved evidence supports offline analysis.

This is distinct from free-space/file-size tools: it queries `MSFT_PhysicalDisk` in the local Windows storage-management provider. It does not scan files or run a drive self-test.

## Requirements and installation

Windows 11 or Windows Server 2022/2025, x64, with Windows PowerShell 5.1, CIM and the Windows Storage provider available. Start as a standard user; some providers require an elevated terminal. Download and extract [v1.0.0](https://github.com/b69ca/win-disk-health/releases/tag/v1.0.0). The ZIP includes a standalone executable and its .NET runtime; no separate runtime installation is needed. Binaries are unsigned.

Verify the ZIP against the release's `SHA256SUMS.txt`, for example with `Get-FileHash .\win-disk-health-1.0.0-win-x64.zip -Algorithm SHA256`. Then:

```powershell
.\win-disk-health.exe
.\win-disk-health.exe --json > report.json
.\win-disk-health.exe --from report.json
.\win-disk-health.exe --from report.json --json
.\win-disk-health.exe --help
```

## Options and exits

| Option | Meaning |
| --- | --- |
| `--json` | Output a report with snapshot, findings, and coverage status. |
| `--from FILE` | Analyze a saved report or raw snapshot; never perform live collection. |
| `--help` | Show usage without collection; must be used alone. |

Unknown/duplicate flags, missing values, and malformed snapshots are rejected. Exit **0** means evidence observed without attention findings, **1** invalid arguments or unreadable/invalid input, **2** review findings, **3** partial coverage (findings may also exist), **4** live collection requested outside Windows. Consumers must inspect status and findings, not just whether JSON was produced. Errors go to stderr with fixed messages that omit input paths and contents.

## Interpretation

Health codes follow [Microsoft's physical-disk schema](https://learn.microsoft.com/en-us/windows-hardware/drivers/storage/msft-physicaldisk): 0 Healthy, 1 Warning, 2 Unhealthy, 5 Unknown. Warning/Unhealthy produce review findings. Missing, unknown, or unrecognized health yields partial coverage.

Operational codes follow [the storage fault-domain schema](https://learn.microsoft.com/en-us/windows-hardware/drivers/storage/msft-storagefaultdomain). OK (2), In Service (11), and Completed (17) are displayed as observations. Other known nonzero states produce review findings, including transitional states; they do not all imply broken hardware. Unknown (0), Other (1), missing/empty arrays, and unrecognized numeric codes make coverage partial. Unrecognized codes are retained for review. Failed Media, Split, Stale Metadata, IO Error, and Unrecognized Metadata extension codes are decoded. Mixed states retain every distinct attention state, including when health says Healthy.

An empty inventory, collection failure, or truncation is partial, never a clean bill of health. Findings remain present when partial coverage takes precedence. Verify backups and investigate with local storage and vendor diagnostics before taking corrective action. This tool never recommends automatic replacement, repair, or reset.

`index` is only a row in this capture and is not stable across runs. `providerDeviceId` is retained only when it contains 1–10 ASCII digits. Correlate it locally with `Get-PhysicalDisk | Select-Object DeviceId,HealthStatus,OperationalStatus`; it is a provider identifier, **not a guaranteed Windows disk number**. Non-numeric identifiers are omitted, and coverage is marked partial. Multiple providers can reuse identifiers, so confirm locally before acting.

## Privacy, bounds, and limitations

No telemetry, network probes, uploads, serial numbers, friendly names, models, sizes, locations, paths, usernames, hostnames, or vendor error messages are included. Only the capture time, row index, numeric provider identifier and numeric status fields are emitted. CIM internally creates provider objects; the collector projects only whitelisted fields before serialization. Review even these reduced reports before sharing.

The collector makes a read-only local CIM query, caps output at 256 disks, and reports truncation. Provider/native enumeration may materialize before the cap. A 30-second subprocess budget and 1 MiB limit per output stream bound collection; the subprocess is terminated on failure. No execution-policy bypass is used. Provider access or PowerShell policy may prevent collection.

Provider state can be stale, incomplete, virtualized, or masked by RAID/USB controllers. Visibility does not establish that every attached drive is represented. A Healthy status does not establish hardware integrity or predict future failure. The tool does not inspect SMART attributes, reliability counters, sectors, temperatures, filesystem health, volumes, redundancy, or event logs. Hosted Windows validation establishes the tested runner environment, not every controller or Windows edition.

Replay accepts schema version 1 for this tool only, required lowercase fields, at most 1 MiB and depth 16. Duplicate/unknown fields and invalid types are rejected. UTF-8 and BOM-marked UTF-16 LE/BE are supported with strict decoding, including Windows PowerShell 5.1 redirection. Capture timestamps must be UTC. Report findings/status are recomputed from embedded evidence; capture time is preserved. Synthetic `fixture` sources remain labeled as such.

## Build, test, and package

Use the .NET 10 SDK (CI pins 10.0.403). There are no third-party runtime or test packages. From the repository root:

```powershell
dotnet build WinDiskHealth.csproj -c Release
dotnet run --project tests/Checks.csproj -c Release
dotnet run --project WinDiskHealth.csproj -- --from tests/fixture.json --json
pwsh -File scripts/Validate.ps1
powershell -File scripts/Test-Collector.ps1
pwsh -File scripts/Package-Windows.ps1
powershell -File scripts/Smoke-Windows.ps1 -Executable ./artifacts/publish/win-disk-health.exe
```

The C# checks run cross-platform; `dotnet run`, rather than `dotnet test`, executes the dependency-free assertions. The fixture intentionally returns exit 2. PowerShell collector contracts test projection, null evidence, sanitization, failures and truncation using synthetic objects. CI runs Ubuntu/macOS/Windows behavior checks; Windows PowerShell 5.1 runs collector contracts and the standalone live executable. An independent CIM query checks each reported identifier and status, the field whitelist, inventory stability, and UTF-16 JSON/text replay. Live reports remain temporary and are deleted without logging/uploading their contents.

Pinned GitHub Actions use read permissions by default; only the trusted tag release job can publish the explicit ZIP/checksum pair. ZIP contents: `win-disk-health.exe`, `README.md`, `LICENSE`, `CHANGELOG.md`. SHA-256 is verified before release upload. CI artifacts provide an additional download and may expire.

## Troubleshooting

For exit 3, inspect `snapshot.issues` and unavailable/unknown fields. Confirm that the local Storage provider is present and try an elevated terminal if permitted. Policy restrictions are reported as unavailable; the tool does not relax policy. For exit 1, check the options, file size, encoding, and schema; arbitrary vendor JSON is not supported. For exit 4, use a saved snapshot or run on Windows. Preserve the original report when requesting support, and share only after privacy review.

## License

MIT. See [LICENSE](LICENSE).
