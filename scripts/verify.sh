#!/usr/bin/env bash
# Full local/CI quality gate: restore, formatting check, build (analyzers + warnings as errors), tests.
#
# Usage:
#   scripts/verify.sh                     # Release configuration
#   CONFIGURATION=Debug scripts/verify.sh
set -euo pipefail

cd "$(dirname "$0")/.."

solution="Dotnet10Template.slnx"
configuration="${CONFIGURATION:-Release}"

step() { printf '\n==> %s\n' "$1"; }

step "Restore"
dotnet restore "$solution"

step "Check formatting (run scripts/format.sh to fix)"
dotnet format "$solution" --verify-no-changes --no-restore

step "Build ($configuration)"
dotnet build "$solution" --no-restore --configuration "$configuration"

step "Test ($configuration)"
dotnet test --solution "$solution" --no-build --configuration "$configuration"

step "Verification passed"
