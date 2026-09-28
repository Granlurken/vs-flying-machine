#!/bin/sh
# Bygger Wright, kopierer den inn i den isolerte testserveren og starter serveren.
# Koble til fra spillet: Multiplayer -> 127.0.0.1 (port 42420). Superflat, kreativ, alle er admin.
# Stopp med Ctrl+C (eller /stop i konsollen).
set -e
cd "$(dirname "$0")"
./deploy.sh
S="$(cd .. && pwd)/WrightTestServer"
mkdir -p "$S/Mods"
rm -rf "$S/Mods/wright"
cp -r "$APPDATA/VintagestoryData/Mods/wright" "$S/Mods/wright"
cd "$APPDATA/Vintagestory"
./VintagestoryServer.exe --dataPath "$(cygpath -m "$S")"
