param()

$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent $PSScriptRoot
Push-Location $repository
try {
  dotnet restore DeME.sln -m:1
  if ($LASTEXITCODE -ne 0) { throw 'Restore failed.' }

  dotnet build DeME.sln -c Release --no-restore -m:1
  if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }

  dotnet run --project tests/DeME.Tests -c Release --no-build
  if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }

  dotnet format whitespace DeME.sln --no-restore --verify-no-changes
  if ($LASTEXITCODE -ne 0) { throw 'Formatting verification failed.' }
}
finally {
  Pop-Location
}
