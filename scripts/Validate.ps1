$ErrorActionPreference = 'Stop'
dotnet build ./WinDiskHealth.csproj -c Release
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
dotnet run --project tests/Checks.csproj -c Release
if ($LASTEXITCODE -ne 0) { throw 'Behavior checks failed' }
$dll = './bin/Release/net10.0/win-disk-health.dll'
dotnet $dll --help | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Help failed' }
dotnet $dll --from tests/fixture.json | Out-Null
if ($LASTEXITCODE -ne 2) { throw 'Fixture text exit mismatch' }
$temp = [IO.Path]::GetTempFileName()
try {
    dotnet $dll --from tests/fixture.json --json | Set-Content $temp -Encoding utf8
    if ($LASTEXITCODE -ne 2) { throw 'Fixture JSON exit mismatch' }
    dotnet $dll --from $temp --json | Out-Null
    if ($LASTEXITCODE -ne 2) { throw 'Report replay failed' }
} finally { Remove-Item $temp -Force }
exit 0
