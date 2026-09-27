#!/usr/bin/env bash
#
# Packages the plugin as a Jellyfin repository, so it can be installed from the server's
# Dashboard instead of by copying a DLL into place.
#
# Produces artifacts/jellyfilter_<version>.zip and artifacts/manifest.json. Upload both to
# somewhere your Jellyfin server can reach over HTTP, then add the manifest.json URL under
# Dashboard -> Plugins -> Repositories.
#
# The manifest records the URL the server will download the zip from, so that URL has to be
# known at packaging time. By default it points at a GitHub release of REPO_URL tagged v<version>.
#
set -euo pipefail

cd "$(dirname "$0")/.."

VERSION="${VERSION:-1.0.0.0}"
TARGET_ABI="${TARGET_ABI:-10.11.0.0}"
OWNER="${OWNER:-jared}"
CHANGELOG="${CHANGELOG:-Initial release.}"
REPO_URL="${REPO_URL:-https://github.com/jared-skinner/jellyfilter}"

# A GitHub release serves its attachments from /releases/download/<tag>/, not from the repository
# root, so the tag has to be part of the URL.
BASE_URL="${BASE_URL:-${REPO_URL%/}/releases/download/v${VERSION}}"

if [ -z "$BASE_URL" ]; then
    echo "BASE_URL is empty: set it to the directory URL the zip will be served from." >&2
    exit 1
fi

BASE_URL="${BASE_URL%/}"
ZIP_NAME="jellyfilter_${VERSION}.zip"
OUT_DIR="artifacts"
STAGE_DIR="${OUT_DIR}/stage"

# The SDK is often installed under $HOME by the dotnet-install script, which does not put it on
# PATH for non-login shells.
find_dotnet() {
    if command -v dotnet >/dev/null 2>&1; then
        command -v dotnet
        return 0
    fi

    for candidate in \
        "${DOTNET_ROOT:-}/dotnet" \
        "$HOME/.dotnet/dotnet" \
        /usr/share/dotnet/dotnet \
        /usr/lib/dotnet/dotnet \
        /usr/local/share/dotnet/dotnet
    do
        if [ -x "$candidate" ]; then
            echo "$candidate"
            return 0
        fi
    done

    return 1
}

if ! DOTNET="$(find_dotnet)"; then
    echo "No .NET SDK found." >&2
    echo "Install one with:  curl -sSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 9.0" >&2
    echo "or set DOTNET_ROOT to an existing installation." >&2
    exit 1
fi

for tool in zip md5sum; do
    if ! command -v "$tool" >/dev/null 2>&1; then
        echo "Required tool '$tool' is not installed." >&2
        exit 1
    fi
done

rm -rf "$OUT_DIR"
mkdir -p "$STAGE_DIR"

"$DOTNET" build -c Release Jellyfin.Plugin.JellyFilter/Jellyfin.Plugin.JellyFilter.csproj

cp Jellyfin.Plugin.JellyFilter/bin/Release/net9.0/Jellyfin.Plugin.JellyFilter.dll "$STAGE_DIR/"

# The compiler output is deterministic, so pinning the archive's timestamp makes the whole zip
# reproducible. That matters because the manifest carries the zip's checksum: without this, simply
# re-running the script would invalidate an already published manifest.
touch -t 202001010000 "$STAGE_DIR/Jellyfin.Plugin.JellyFilter.dll"

# Jellyfin unpacks the zip straight into plugins/<name>_<version>/, so the DLL has to sit at
# the root of the archive rather than inside a folder.
(cd "$STAGE_DIR" && zip -q -X "../${ZIP_NAME}" Jellyfin.Plugin.JellyFilter.dll)
rm -rf "$STAGE_DIR"

CHECKSUM="$(md5sum "${OUT_DIR}/${ZIP_NAME}" | cut -d' ' -f1)"
TIMESTAMP="$(date -u +%Y-%m-%dT%H:%M:%SZ)"

# The manifest lives at the repository root rather than in artifacts/, because it is the file
# Jellyfin polls: it has to be committed and served, while the zip is a release attachment.
cat > manifest.json <<EOF
[
    {
        "guid": "9d4bf1d9-ad64-4006-8ecb-d285d826352d",
        "name": "JellyFilter",
        "owner": "${OWNER}",
        "category": "General",
        "overview": "Skip or mute scenes listed in a JSON file stored alongside the video.",
        "description": "Reads a sidecar JSON file listing scenes by content type and timestamp, and skips or mutes those scenes during playback according to each user's content preferences.",
        "versions": [
            {
                "version": "${VERSION}",
                "changelog": "${CHANGELOG}",
                "targetAbi": "${TARGET_ABI}",
                "sourceUrl": "${BASE_URL}/${ZIP_NAME}",
                "checksum": "${CHECKSUM}",
                "timestamp": "${TIMESTAMP}"
            }
        ]
    }
]
EOF

echo
echo "Wrote ${OUT_DIR}/${ZIP_NAME} (md5 ${CHECKSUM})"
echo "Wrote manifest.json"
echo
echo "The manifest records that exact zip's checksum, so publish this pair together:"
echo "  1. attach ${OUT_DIR}/${ZIP_NAME} to a release tagged v${VERSION}, so that"
echo "     ${BASE_URL}/${ZIP_NAME}"
echo "     resolves"
echo "  2. commit and push manifest.json"
echo "  3. add its raw URL as a repository in the Jellyfin dashboard"
