#!/bin/sh
# Bygger FlyingMachine, kopierer den inn i den isolerte testserveren og starter serveren.
# Koble til fra spillet: Multiplayer -> 127.0.0.1 (port 42420). Superflat, kreativ, alle er admin.
# Stopp med Ctrl+C (eller /stop i konsollen).
set -e
cd "$(dirname "$0")"
./deploy.sh
S="$(cd .. && pwd)/FlyingMachineTestServer"
mkdir -p "$S/Mods"
rm -rf "$S/Mods/flyingmachine"
cp -r "$APPDATA/VintagestoryData/Mods/flyingmachine" "$S/Mods/flyingmachine"
cd "$APPDATA/Vintagestory"
./VintagestoryServer.exe --dataPath "$(cygpath -m "$S")"
