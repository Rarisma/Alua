#!/usr/bin/env bash
# Regenerate net.rarisma.gravity's offline NuGet feed (nuget-sources.json).
#
# IMPORTANT: the package closure must be harvested with the SAME .NET SDK the Flatpak build uses
# (the org.freedesktop.Sdk.Extension.dotnet10 extension), NOT the host SDK. The two resolve
# different package sets (different patch → different ref/runtime packs), and a feed generated with
# the host SDK will be missing packages at offline-build time. So we run the publish INSIDE the
# GNOME SDK sandbox (network on) and enumerate exactly what it downloads.
#
# Run from the repo root after changing dependencies:  ./tools/regen-nuget-sources.sh
set -euo pipefail

REPO="$(cd "$(dirname "$0")/.." && pwd)"
HARVEST=/tmp/alua-nuget-harvest
RUNTIME_VER=49          # keep in sync with runtime-version in net.rarisma.gravity.yaml

rm -rf "$HARVEST"
flatpak run --devel --share=network \
  --filesystem=/tmp --filesystem="$REPO" \
  --env=DOTNET_CLI_TELEMETRY_OPTOUT=1 --env=DOTNET_NOLOGO=1 --env=HOME=/tmp/alua-sbx-home \
  --command=sh "org.gnome.Sdk//${RUNTIME_VER}" -c "
    export PATH=/usr/lib/sdk/dotnet10/bin:\$PATH
    mkdir -p /tmp/alua-sbx-home
    cd '$REPO'
    NUGET_PACKAGES='$HARVEST' dotnet publish Alua/Alua.csproj -c Release -f net10.0-desktop \
      -r linux-x64 --self-contained -p:FlatpakBuild=true -p:AllowMissingPrunePackageData=true \
      -o /tmp/alua-sbx-pub
  "

python3 "$REPO/tools/gen-nuget-sources.py" "$HARVEST" "$REPO/nuget-sources.json"
echo "Done. Review and commit nuget-sources.json."
