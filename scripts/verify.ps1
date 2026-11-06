#!/usr/bin/env pwsh
# Full local/CI quality gate: restore, formatting check, build (analyzers + warnings as errors), tests.
#
# Usage:
#   ./scripts/verify.ps1                          # Release configuration
#   ./scripts/verify.ps1 -Configuration Debug
param(
    [string] $Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$solution = 'Dotnet10Template.slnx'

# Native commands don't throw on failure in Windows PowerShell 5.1, so check exit codes explicitly.
function Invoke-Step([string] $Name, [scriptblock] $Command) {
    Write-Host "`n==> $Name" -ForegroundColor Cyan
    & $Command
    if ($LASTEXITCODE -ne 0) {
        Write-Host "FAILED: $Name (exit code $LASTEXITCODE)" -ForegroundColor Red
        exit $LASTEXITCODE
    }
}

Push-Location (Join-Path $PSScriptRoot '..')
try {
    Invoke-Step 'Restore' { dotnet restore $solution }
    Invoke-Step 'Check formatting (run scripts/format.ps1 to fix)' { dotnet format $solution --verify-no-changes --no-restore }
    Invoke-Step "Build ($Configuration)" { dotnet build $solution --no-restore --configuration $Configuration }
    Invoke-Step "Test ($Configuration)" { dotnet test --solution $solution --no-build --configuration $Configuration }
    Write-Host "`n==> Verification passed" -ForegroundColor Green
}
finally {
    Pop-Location
}
