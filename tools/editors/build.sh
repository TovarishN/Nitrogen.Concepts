#!/usr/bin/env bash
# Builds installable editor plugins for .ncat records with the nitrogen tool pinned in .config/dotnet-tools.json:
#   artifacts/catalog-<version>.vsix         VS Code (Extensions: Install from VSIX...)
#   artifacts/catalog-<version>-rider.zip    Rider (Settings | Plugins | Install Plugin from Disk)
# Both carry the catalog language and a portable Nitrogen server run with the .NET 10 runtime.
#
# Usage: tools/editors/build.sh [--vscode] [--rider]     (default: both)
# Requires the .NET 10 SDK and NuGetPackageSourceCredentials_nitrogen for Nitrogen's packages (see nuget.config);
# npm for VS Code; Gradle and JDK 25 (JAVA_HOME) for Rider.
set -euo pipefail

root="$(cd "$(dirname "$0")/../.." && pwd)"
cd "$root"
dotnet tool restore
dotnet nitrogen package --config "$root/nitrogen.json" --output "$root/artifacts" "$@"
