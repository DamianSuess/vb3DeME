param()

$ErrorActionPreference = 'Stop'
$buildExitCode = 1

Get-Command dotnet -ErrorAction Stop | Out-Null
Push-Location $PSScriptRoot
try {
  dotnet build DeME.sln --configuration Release --nologo --maxcpucount:1
  $buildExitCode = $LASTEXITCODE
}
finally {
  Pop-Location
}

exit $buildExitCode
