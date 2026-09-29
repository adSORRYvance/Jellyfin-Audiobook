#!/usr/bin/env bash
# Builds a dev copy of the plugin stamped with the version you intend to tag, then prints a command to run on a relay
# machine that can SSH to both this machine and the test server. The relay streams the build into the Jellyfin
# container and restarts it, so this machine and the server never connect to each other over SSH.
# This machine only talks to the server over Jellyfin's HTTP API, to confirm the version loaded.
# Jellyfin keeps only the newest version of a plugin and deletes older folders on startup, so the dev build replaces
# whatever release is installed, and the later catalog release of the same version counts as already installed.
#
# Usage: scripts/deploy-test.sh <version> [--local-only]
#   <version>      four plain numbers, the version you plan to tag, e.g. 0.1.5.0
#   --local-only   build the plugin folder into dist/dev and stop
#
# Settings live in scripts/deploy-test.env, see deploy-test.env.example.
set -euo pipefail

repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo"

version="${1:-}"
local_only=false
if [[ "${2:-}" == "--local-only" ]]; then
    local_only=true
fi

if [[ ! "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
    echo "Usage: scripts/deploy-test.sh <version> [--local-only]   (version is four numbers, e.g. 0.1.5.0)" >&2
    exit 1
fi

# A published version already has a real build behind it, and a dev build with the same number would hide it
if [[ -n "$(git tag -l "v$version" "v$version-beta")" ]]; then
    echo "v$version is already tagged. Pick the version you're about to release, not one that's out." >&2
    exit 1
fi

# The system dotnet can be an older SDK that can't target net10.0, the user install in ~/.dotnet wins when it's there
dotnet_cmd="${DOTNET:-}"
if [[ -z "$dotnet_cmd" ]]; then
    if [[ -x "$HOME/.dotnet/dotnet" ]]; then
        dotnet_cmd="$HOME/.dotnet/dotnet"
    else
        dotnet_cmd="dotnet"
    fi
fi

commit="$(git rev-parse --short HEAD)"
dirty=""
if [[ -n "$(git status --porcelain)" ]]; then
    dirty=" with uncommitted changes"
fi

# Build

folder="Audiobook Library_$version"
stage="$repo/dist/dev"
publish="$(mktemp -d)"
trap 'rm -rf "$publish"' EXIT

echo "Building $version from $commit$dirty"
"$dotnet_cmd" publish Jellyfin.Plugin.AudiobookLibrary/Jellyfin.Plugin.AudiobookLibrary.csproj \
    -c Release --nologo -v quiet \
    -p:Version="$version" -p:AssemblyVersion="$version" -p:FileVersion="$version" \
    -o "$publish"

rm -rf "$stage"
mkdir -p "$stage/$folder"
cp "$publish/Jellyfin.Plugin.AudiobookLibrary.dll" "$publish/Jellyfin.Plugin.AudiobookLibrary.pdb" "$stage/$folder/"

# Without meta.json Jellyfin invents an id from the folder name and treats this as a different plugin
# autoUpdate is off so Jellyfin's update task can't swap the dev build for a catalog version mid-test
python3 - "$version" "$commit$dirty" "$stage/$folder/meta.json" <<'EOF'
import datetime, json, sys, yaml

version, source, out = sys.argv[1:4]
with open("build.yaml") as f:
    cfg = yaml.safe_load(f)

meta = {
    "guid": cfg["guid"],
    "name": cfg["name"],
    "description": cfg["description"],
    "overview": cfg["overview"],
    "owner": cfg["owner"],
    "category": cfg["category"],
    "version": version,
    "changelog": f"Dev build of {source}.",
    "targetAbi": cfg["targetAbi"],
    "timestamp": datetime.datetime.now(datetime.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
    "status": "Active",
    "autoUpdate": False,
}
with open(out, "w") as f:
    json.dump(meta, f, indent=2)
EOF

echo "Built dist/dev/$folder"
if $local_only; then
    exit 0
fi

# Hand-over

env_file="$repo/scripts/deploy-test.env"
if [[ ! -f "$env_file" ]]; then
    echo "Missing scripts/deploy-test.env. Copy deploy-test.env.example and fill it in." >&2
    exit 1
fi
# shellcheck source=/dev/null
source "$env_file"
: "${RELAY_WORKSPACE_HOST:?set RELAY_WORKSPACE_HOST in scripts/deploy-test.env}"
: "${RELAY_SERVER_HOST:?set RELAY_SERVER_HOST in scripts/deploy-test.env}"
: "${WORKSPACE_REPO:?set WORKSPACE_REPO in scripts/deploy-test.env}"
: "${TEST_CONTAINER:?set TEST_CONTAINER in scripts/deploy-test.env}"
: "${TEST_PLUGINS_DIR:?set TEST_PLUGINS_DIR in scripts/deploy-test.env}"
: "${TEST_SERVER_URL:?set TEST_SERVER_URL in scripts/deploy-test.env}"
container_user="${TEST_CONTAINER_USER:-abc}"

# The relay reads the build from here and writes it into the server, the files pass through it without being saved
# Unpacking runs as root inside the container, because the plugins folder belongs to the container's user
# Only this version's folder is replaced, Jellyfin removes older versions by itself when it restarts
# The folder goes to the container's user, a folder Jellyfin can't write meta.json into stops the server from starting
# Quoting layers: the relay's shell reads the single quotes, the server's shell the double quotes, and sh in the
# container the escaped ones around the folder name, which has a space in it
pull="ssh $RELAY_WORKSPACE_HOST \"tar -C $WORKSPACE_REPO/dist/dev -cf - '$folder'\""
install="docker exec -i $TEST_CONTAINER sh -c \"cd $TEST_PLUGINS_DIR && rm -rf \\\"$folder\\\" && tar -xf - && chown -R $container_user: \\\"$folder\\\"\""
push="ssh $RELAY_SERVER_HOST '$install && docker restart $TEST_CONTAINER'"

echo
echo "Run this on the relay machine:"
echo
echo "$pull | $push"
echo

if [[ -z "${TEST_API_KEY:-}" ]]; then
    echo "Then check Dashboard > Plugins shows Audiobook Library $version"
    exit 0
fi

# Wait

# Plain HTTP to Jellyfin's API, failed requests while the container restarts just mean try again
printf "Waiting for the test server to load %s (Ctrl+C to stop)" "$version"
for _ in $(seq 1 200); do
    installed="$(curl -fsS -m 5 -H "Authorization: MediaBrowser Token=\"$TEST_API_KEY\"" "$TEST_SERVER_URL/Plugins" 2>/dev/null \
        | python3 -c 'import json, sys
for p in json.load(sys.stdin):
    if p["Name"] == "Audiobook Library":
        print(p["Version"], p["Status"])' 2>/dev/null || true)"
    if [[ "$installed" == "$version "* ]]; then
        echo
        echo "Installed: Audiobook Library $installed"
        exit 0
    fi
    printf "."
    sleep 3
done

echo
echo "The server still reports ${installed:-nothing} after 10 minutes. Check the command ran and the server log." >&2
exit 1
