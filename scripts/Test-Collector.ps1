$ErrorActionPreference = 'Stop'
# Exercise the actual collector projection without requiring storage changes.
function Get-CimInstance {
    param($Namespace,$ClassName,$Property,$ErrorAction)
    [pscustomobject]@{ DeviceId='0'; HealthStatus=0; OperationalStatus=@(2); SerialNumber='SYNTHETIC-DO-NOT-COLLECT'; FriendlyName='SYNTHETIC' }
    [pscustomobject]@{ DeviceId='non-numeric-private-name'; HealthStatus=$null; OperationalStatus=$null }
}
$data = (& ./Collector.ps1) | ConvertFrom-Json
if ($data.disks.Count -ne 2 -or $data.disks[0].providerDeviceId -ne '0' -or $data.disks[0].operationalStatus[0] -ne 2) { throw 'Projection failed' }
if ($null -ne $data.disks[1].providerDeviceId -or $null -ne $data.disks[1].healthStatus -or $null -ne $data.disks[1].operationalStatus) { throw 'Null/privacy projection failed' }
if (($data | ConvertTo-Json -Depth 8) -match 'SYNTHETIC|non-numeric-private-name') { throw 'Sensitive metadata leaked' }
function Get-CimInstance { throw 'SYNTHETIC-SENSITIVE-ERROR' }
$data = (& ./Collector.ps1) | ConvertFrom-Json
if ($data.issues[0] -ne 'collector-unavailable' -or $data.disks.Count -ne 0) { throw 'Failure contract failed' }
function Get-CimInstance { foreach ($i in 0..256) { [pscustomobject]@{DeviceId=[string]$i;HealthStatus=0;OperationalStatus=@(2)} } }
$data = (& ./Collector.ps1) | ConvertFrom-Json
if ($data.disks.Count -ne 256 -or $data.issues[0] -ne 'truncated') { throw 'Truncation contract failed' }
Write-Output 'Collector projection, missing evidence, sanitization, failure and truncation checks passed.'
exit 0
