#!/usr/bin/env pwsh
# Applies formatting, code-style and analyzer fixes to the whole solution.
#
# Usage:
#   ./scripts/format.ps1                        # fix everything that has an automatic fix
#   ./scripts/format.ps1 --verify-no-changes    # check only; non-zero exit if anything would change
$ErrorActionPreference = 'Stop'

Push-Location (Join-Path $PSScriptRoot '..')
try {
    dotnet format Dotnet10Template.slnx @args
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
finally {
    Pop-Location
}
