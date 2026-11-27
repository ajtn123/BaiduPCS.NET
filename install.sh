#!/bin/sh
set -eu

fail() {
    printf '%s\n' "$*" >&2
    exit 1
}

install_dir=${BAIDUPCS_INSTALL_DIR:-$HOME/.local/bin}
force_self_contained=false
while [ "$#" -gt 0 ]; do
    case "$1" in
        --install-dir)
            [ "$#" -ge 2 ] && [ -n "$2" ] || fail 'Supply a directory after --install-dir.'
            install_dir=$2
            shift
            ;;
        --self-contained) force_self_contained=true ;;
        -h|--help)
            printf '%s\n' 'Usage: sh install.sh [--install-dir DIRECTORY] [--self-contained]'
            exit 0
            ;;
        *) fail "Unknown option: $1" ;;
    esac
    shift
done

case "$(uname -s)" in
    Darwin) platform=osx ;;
    Linux)
        platform=linux
        if { ldd --version 2>&1 || :; } | grep -qi musl; then
            platform=linux-musl
        else
            for loader in /lib/ld-musl-*.so.1; do
                if [ -e "$loader" ]; then platform=linux-musl; break; fi
            done
        fi
        ;;
    *) fail 'This installer supports macOS and Linux. On Windows, use install.ps1.' ;;
esac
machine=$(uname -m)
if [ "$platform" = osx ] && [ "$(sysctl -n hw.optional.arm64 2>/dev/null || :)" = 1 ]; then
    machine=arm64
fi
case "$machine" in
    x86_64|amd64) architecture=x64; arch_root=${DOTNET_ROOT_X64:-} ;;
    aarch64|arm64) architecture=arm64; arch_root=${DOTNET_ROOT_ARM64:-} ;;
    *) fail "Unsupported architecture: $machine." ;;
esac
rid="$platform-$architecture"

command -v curl >/dev/null 2>&1 || fail 'Install curl before running this installer.'
if command -v jq >/dev/null 2>&1; then
    json_tool=jq
elif command -v python3 >/dev/null 2>&1; then
    json_tool=python3
else
    fail 'Install jq or Python 3 to read the GitHub release metadata.'
fi
if command -v sha256sum >/dev/null 2>&1; then
    hash_tool=sha256sum
elif command -v shasum >/dev/null 2>&1; then
    hash_tool=shasum
else
    fail 'Install sha256sum or shasum to verify the executable.'
fi

has_compatible_runtime() {
    for dotnet in "$(command -v dotnet || :)" "${arch_root:+$arch_root/dotnet}" \
        "${DOTNET_ROOT:+$DOTNET_ROOT/dotnet}" "$HOME/.dotnet/dotnet" \
        /usr/share/dotnet/dotnet /usr/lib/dotnet/dotnet /usr/local/share/dotnet/dotnet; do
        [ -x "$dotnet" ] || continue
        runtimes=$("$dotnet" --list-runtimes --arch "$architecture" 2>/dev/null) ||
            runtimes=$("$dotnet" --list-runtimes 2>/dev/null) || continue
        if printf '%s\n' "$runtimes" | awk '
            $1 == "Microsoft.NETCore.App" && $2 ~ /^[0-9]+\.[0-9]+\.[0-9]+$/ {
                split($2, version, "."); if (version[1] >= 10) found = 1
            }
            END { exit !found }
        '; then return 0; fi
    done
    return 1
}

download() {
    curl -fsSL --retry 3 --proto '=https' --proto-redir '=https' \
        --user-agent 'BaiduPCS.NET installer' "$1" > "$2"
}

release_value() {
    if [ "$json_tool" = jq ]; then
        if [ "$#" -eq 0 ]; then
            jq -er 'select(.draft == false and .prerelease == false) | .tag_name | strings' "$metadata"
        else
            jq -er --arg name "$1" '[.assets[] | select(.name == $name)] |
                select(length == 1) | .[0].digest | strings' "$metadata"
        fi
    else
        python3 - "$metadata" "$@" <<'PY'
import json
import sys

with open(sys.argv[1], encoding="utf-8") as stream:
    release = json.load(stream)
if len(sys.argv) == 2:
    value = release.get("tag_name") if release.get("draft") is False and release.get("prerelease") is False else None
else:
    assets = [asset for asset in release["assets"] if asset["name"] == sys.argv[2]]
    value = assets[0].get("digest") if len(assets) == 1 else None
if not isinstance(value, str):
    sys.exit(1)
print(value)
PY
    fi
}

install_candidate() {
    name="BaiduPCS.NET-$tag-$rid$1"
    digest=$(release_value "$name") || fail "GitHub did not provide a SHA-256 digest for $name."
    printf '%s\n' "$digest" | grep -Eq '^sha256:[a-f0-9]{64}$' ||
        fail "GitHub did not provide a valid SHA-256 digest for $name."
    download "$repository/releases/download/$tag/$name" "$candidate" || fail "Could not download $name."
    if [ "$hash_tool" = sha256sum ]; then
        hash=$(sha256sum "$candidate") || fail "Could not hash $name."
    else
        hash=$(shasum -a 256 "$candidate") || fail "Could not hash $name."
    fi
    [ "${hash%% *}" = "${digest#sha256:}" ] || fail "SHA-256 verification failed for $name."
    chmod +x "$candidate" || fail "Could not make $name executable."
    # A runtime detected by dotnet may be unavailable to the release apphost.
    actual=$("$candidate" --version 2>/dev/null) && [ "$actual" = "${tag#v}" ]
}

mkdir -p -- "$install_dir"
install_dir=$(cd -- "$install_dir" && pwd -P)
scratch=$(mktemp -d "$install_dir/.baidupcs-install.XXXXXX")
metadata="$scratch/release.json"
candidate="$scratch/baidupcs"
trap 'rm -f -- "$metadata" "$candidate"; rmdir -- "$scratch"' EXIT
trap 'exit 1' HUP INT TERM

repository=https://github.com/ajtn123/BaiduPCS.NET
download https://api.github.com/repos/ajtn123/BaiduPCS.NET/releases/latest "$metadata" ||
    fail 'Could not retrieve the latest stable BaiduPCS.NET release.'
tag=$(release_value) || fail 'GitHub did not return a stable BaiduPCS.NET release.'
printf '%s\n' "$tag" | grep -Eq '^v[0-9]+\.[0-9]+\.[0-9]+$' ||
    fail 'GitHub did not return a valid BaiduPCS.NET release tag.'

variant=self-contained
if ! "$force_self_contained" && has_compatible_runtime; then
    if install_candidate -framework-dependent; then
        variant=framework-dependent
    else
        printf '%s\n' 'The framework-dependent executable could not use the detected runtime. Trying the self-contained build.' >&2
    fi
fi
if [ "$variant" = self-contained ] && ! install_candidate ''; then
    fail 'The self-contained executable failed to start or reported an unexpected version.'
fi

destination="$install_dir/baidupcs"
[ ! -d "$destination" ] || fail "$destination is a directory."
mv -f -- "$candidate" "$destination"
printf 'Installed BaiduPCS.NET %s (%s, %s) at %s\n' "$tag" "$variant" "$rid" "$destination"
case ":$PATH:" in
    *":$install_dir:"*) ;;
    *) printf 'Add %s to PATH to run baidupcs by name.\n' "$install_dir" ;;
esac
