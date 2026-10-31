param([Parameter(Mandatory=$true)][string]$Executable)
$ErrorActionPreference = 'Stop'
$tokens = $null; $errors = $null
$null = [System.Management.Automation.Language.Parser]::ParseFile((Join-Path $PWD 'Collector.ps1'), [ref]$tokens, [ref]$errors)
if ($errors.Count) { throw 'Collector syntax invalid' }
$before = @(Get-CimInstance -Namespace root/Microsoft/Windows/Storage -ClassName MSFT_PhysicalDisk -Property DeviceId,HealthStatus,OperationalStatus)
if ($before.Count -eq 0) { throw 'Live runner has no physical-disk evidence' }
$temp = [IO.Path]::GetTempFileName()
try {
    # Deliberately use Windows PowerShell 5.1 UTF-16 redirection for replay coverage.
    & $Executable --json > $temp
    $exit = $LASTEXITCODE
    if ($exit -notin @(0,2,3)) { throw 'Live executable failed' }
    $report = Get-Content $temp -Raw | ConvertFrom-Json
    if ($report.snapshot.source -ne 'windows' -or $report.snapshot.issues.Count -ne 0) { throw 'Live collector unavailable/truncated' }
    if (@($report.snapshot.disks).Count -ne $before.Count) { throw 'Independent inventory count mismatch' }
    foreach ($disk in $report.snapshot.disks) {
        $fields = @($disk.PSObject.Properties.Name | Sort-Object)
        if (($fields -join ',') -ne 'healthStatus,index,operationalStatus,providerDeviceId') { throw 'Unexpected metadata' }
        if ($null -eq $disk.providerDeviceId) { throw 'Runner identity unavailable' }
        $match = @($before | Where-Object { [string]$_.DeviceId -ceq $disk.providerDeviceId })
        if ($match.Count -ne 1) { throw 'Independent identity mismatch' }
        if ($disk.healthStatus -ne [int]$match[0].HealthStatus) { throw 'Independent health mismatch' }
        if ((@($disk.operationalStatus | Sort-Object) -join ',') -ne (@($match[0].OperationalStatus | Sort-Object) -join ',')) { throw 'Independent operational-state mismatch' }
    }
    & $Executable --from $temp --json | Out-Null
    if ($LASTEXITCODE -ne $exit) { throw 'JSON replay status mismatch' }
    & $Executable --from $temp | Out-Null
    if ($LASTEXITCODE -ne $exit) { throw 'Text replay status mismatch' }
    $after = @(Get-CimInstance -Namespace root/Microsoft/Windows/Storage -ClassName MSFT_PhysicalDisk -Property DeviceId)
    if ((@($before.DeviceId | Sort-Object) -join ',') -ne (@($after.DeviceId | Sort-Object) -join ',')) { throw 'Inventory changed during smoke test' }
    Write-Output 'Live executable, independent CIM comparison, metadata whitelist, UTF-16 JSON/text replay passed.'
} finally { Remove-Item $temp -Force }
exit 0
