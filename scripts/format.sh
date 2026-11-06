#!/usr/bin/env bash
# Applies formatting, code-style and analyzer fixes to the whole solution.
#
# Usage:
#   scripts/format.sh                        # fix everything that has an automatic fix
#   scripts/format.sh --verify-no-changes    # check only; non-zero exit if anything would change
set -euo pipefail

cd "$(dirname "$0")/.."

dotnet format Dotnet10Template.slnx "$@"
