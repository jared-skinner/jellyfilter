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
# known at packaging time:
#
#   BASE_URL=https://example.com/jellyfin scripts/package.sh
#
set -euo pipefail

cd "$(dirname "$0")/.."

VERSION="${VERSION:-1.0.0.0}"
TARGET_ABI="${TARGET_ABI:-10.11.0.0}"
OWNER="${OWNER:-jared}"
CHANGELOG="${CHANGELOG:-Initial release.}"
BASE_URL="${BASE_URL:-}"

if [ -z "$BASE_URL" ]; then
    echo "BASE_URL is required: the directory URL the zip will be served from." >&2
    echo "  example: BASE_URL=https://github.com/you/jellyfilter/releases/download/v${VERSION} $0" >&2
    exit 1
fi

BASE_URL="${BASE_URL%/}"
ZIP_NAME="jellyfilter_${VERSION}.zip"
OUT_DIR="artifacts"
STAGE_DIR="${OUT_DIR}/stage"

rm -rf "$OUT_DIR"
mkdir -p "$STAGE_DIR"

dotnet build -c Release Jellyfin.Plugin.JellyFilter/Jellyfin.Plugin.JellyFilter.csproj

cp Jellyfin.Plugin.JellyFilter/bin/Release/net9.0/Jellyfin.Plugin.JellyFilter.dll "$STAGE_DIR/"

# Jellyfin unpacks the zip straight into plugins/<name>_<version>/, so the DLL has to sit at
# the root of the archive rather than inside a folder.
(cd "$STAGE_DIR" && zip -q -r "../${ZIP_NAME}" .)
rm -rf "$STAGE_DIR"

CHECKSUM="$(md5sum "${OUT_DIR}/${ZIP_NAME}" | cut -d' ' -f1)"
TIMESTAMP="$(date -u +%Y-%m-%dT%H:%M:%SZ)"

cat > "${OUT_DIR}/manifest.json" <<EOF
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
echo "Wrote ${OUT_DIR}/manifest.json"
echo
echo "Next: publish both files so that ${BASE_URL}/${ZIP_NAME} resolves,"
echo "then add the manifest.json URL as a repository in the Jellyfin dashboard."
