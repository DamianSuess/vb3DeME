param()

$ErrorActionPreference = 'Stop'
$buildExitCode = 1

Get-Command dotnet -ErrorAction Stop | Out-Null
Push-Location $PSScriptRoot
try {
  dotnet build DeME.sln --configuration Release --nologo --maxcpucount:1
  $buildExitCode = $LASTEXITCODE
  if ($buildExitCode -eq 0) {
    Write-Host "Release application output: $(Join-Path $PSScriptRoot 'publish')"
  }
}
finally {
  Pop-Location
}

exit $buildExitCode
