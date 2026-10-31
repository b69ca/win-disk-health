# Embedded, read-only Windows PowerShell 5.1 collector. Never serialize CIM objects.
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$rows = @()
$issues = @()
try {
    $devices = @(Get-CimInstance -Namespace 'root/Microsoft/Windows/Storage' -ClassName MSFT_PhysicalDisk -Property DeviceId,HealthStatus,OperationalStatus -ErrorAction Stop | Select-Object -First 257)
    if ($devices.Count -gt 256) { $issues += 'truncated' }
    foreach ($device in ($devices | Select-Object -First 256)) {
        $id = $null
        if ([string]$device.DeviceId -cmatch '^[0-9]{1,10}$') { $id = [string]$device.DeviceId }
        $health = $null
        if ($null -ne $device.HealthStatus) { $health = [int]$device.HealthStatus }
        $operational = $null
        if ($null -ne $device.OperationalStatus) { $operational = @($device.OperationalStatus | ForEach-Object { [int]$_ }) }
        $rows += [pscustomobject]@{ index=$rows.Count+1; providerDeviceId=$id; healthStatus=$health; operationalStatus=$operational }
    }
} catch {
    # Provider errors may contain machine/device details; expose a fixed code only.
    $issues = @('collector-unavailable')
}
[pscustomobject]@{
    tool='win-disk-health'; schemaVersion=1; capturedAt=[DateTimeOffset]::UtcNow.ToString('o')
    source='windows'; disks=@($rows); issues=@($issues)
} | ConvertTo-Json -Depth 6 -Compress
