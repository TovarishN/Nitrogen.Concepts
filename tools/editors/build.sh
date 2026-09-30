#!/usr/bin/env bash
# Builds installable editor plugins for .ncat records with the pinned Nitrogen's `nitrogen package`:
#   artifacts/catalog-<version>.vsix         VS Code (Extensions: Install from VSIX...)
#   artifacts/catalog-<version>-rider.zip    Rider (Settings | Plugins | Install Plugin from Disk)
# Both carry the catalog language and a portable Nitrogen server run with the .NET 10 runtime.
#
# Usage: tools/editors/build.sh [--vscode] [--rider]     (default: both)
# Requires the .NET 10 SDK; npm for VS Code; Gradle and JDK 25 (JAVA_HOME) for Rider.
set -euo pipefail

root="$(cd "$(dirname "$0")/../.." && pwd)"
nitrogen_src="$root/external/Nitrogen"
if [[ ! -f "$nitrogen_src/Nitrogen.slnx" ]]; then
    echo "error: external/Nitrogen is empty; run: git submodule update --init" >&2
    exit 1
fi

dotnet build "$nitrogen_src/Nitrogen.Cli/Nitrogen.Cli.csproj" -c Release -o "$root/artifacts/nitrogen"
dotnet "$root/artifacts/nitrogen/nitrogen.dll" package --config "$root/nitrogen.json" --output "$root/artifacts" "$@"
