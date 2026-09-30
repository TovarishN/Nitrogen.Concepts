#!/usr/bin/env bash
# Builds editor support for .ncat records from the pinned Nitrogen submodule:
#   artifacts/nitrogen/                 the language server the VS Code launcher runs (tools/editors/nitrogen)
#   artifacts/nitrogen-*.vsix           the generic Nitrogen VS Code extension, which serves nitrogen.json languages
#   artifacts/catalog-rider.zip         a Rider plugin for the catalog language, bundling a server for this machine
#
# Usage: tools/editors/build.sh [--vscode] [--rider]     (default: both)
# Requires the .NET 10 SDK; Node.js with npm for VS Code; Gradle and JDK 25 (JAVA_HOME) for Rider.
set -euo pipefail

root="$(cd "$(dirname "$0")/../.." && pwd)"
nitrogen_src="$root/external/Nitrogen"
artifacts="$root/artifacts"
vscode=false
rider=false
while [[ $# -gt 0 ]]; do
    case "$1" in
        --vscode) vscode=true; shift ;;
        --rider) rider=true; shift ;;
        *) echo "usage: tools/editors/build.sh [--vscode] [--rider]" >&2; exit 2 ;;
    esac
done
if [[ "$vscode" == false && "$rider" == false ]]; then vscode=true; rider=true; fi

step() { printf '\n==> %s\n' "$*"; }

if [[ ! -f "$nitrogen_src/Nitrogen.slnx" ]]; then
    echo "error: external/Nitrogen is empty; run: git submodule update --init" >&2
    exit 1
fi

step "Build the Nitrogen language server"
dotnet build "$nitrogen_src/Nitrogen.Cli/Nitrogen.Cli.csproj" -c Release -o "$artifacts/nitrogen"

if [[ "$vscode" == true ]]; then
    step "Package the VS Code extension"
    rm -rf "$artifacts/vscode-src"
    mkdir -p "$artifacts/vscode-src"
    (cd "$nitrogen_src/editors/vscode" && tar --exclude node_modules --exclude out --exclude '*.vsix' -cf - .) | tar -xf - -C "$artifacts/vscode-src"
    (cd "$artifacts/vscode-src" && npm ci && npm run compile && npm run package)
    mv "$artifacts"/vscode-src/*.vsix "$artifacts/"
fi

if [[ "$rider" == true ]]; then
    # The Rider generator embeds Kotlin templates as C# raw strings, so a CRLF checkout (core.autocrlf or
    # core.eol=crlf) breaks it. Nitrogen commits LF; check out the submodule that way.
    if git -C "$nitrogen_src" ls-files --eol Nitrogen.Cli/Rider/RiderPluginRenderer.cs | grep -q 'w/crlf'; then
        cat >&2 <<'MSG'
error: external/Nitrogen is checked out with CRLF line endings, which the Rider generator cannot use.
Check it out with LF (the submodule must have no local changes):
  git -C external/Nitrogen config core.autocrlf false
  git -C external/Nitrogen config core.eol lf
  git -C external/Nitrogen ls-files -z | (cd external/Nitrogen && xargs -0 rm -f)
  git -C external/Nitrogen checkout -- .
MSG
        exit 1
    fi

    case "$(uname -s)-$(uname -m)" in
        Darwin-arm64) rid=osx-arm64; target=macos-aarch64 ;;
        Darwin-x86_64) rid=osx-x64; target=macos-x64 ;;
        Linux-x86_64) rid=linux-x64; target=linux-x64 ;;
        MINGW*-x86_64 | MSYS*-x86_64 | CYGWIN*-x86_64) rid=win-x64; target=windows-x64 ;;
        *) echo "error: no Rider bundle target for $(uname -s) $(uname -m)" >&2; exit 1 ;;
    esac

    step "Publish a single-file server for $target"
    dotnet publish "$nitrogen_src/Nitrogen.Cli/Nitrogen.Cli.csproj" -c Release -r "$rid" --self-contained \
        -p:PublishSingleFile=true -o "$artifacts/server/$target"
    server="$artifacts/server/$target/nitrogen"
    if [[ "$rid" == win-x64 ]]; then server="$server.exe"; fi

    step "Generate and build the catalog Rider plugin"
    rm -rf "$artifacts/rider-src"
    dotnet "$artifacts/nitrogen/nitrogen.dll" generate rider --config "$root/nitrogen.json" \
        --bundle "$target=$server" --output "$artifacts/rider-src"
    (cd "$artifacts/rider-src" && gradle buildPlugin --console=plain)
    cp "$artifacts"/rider-src/build/distributions/*.zip "$artifacts/catalog-rider.zip"
fi

step "Done"
find "$artifacts" -maxdepth 1 \( -name '*.vsix' -o -name '*.zip' \) -print
