#!/bin/sh
# Bygger FlyingMachine og kopierer mod-mappa inn i VS. Start spillet (eller verdenen) på nytt etterpå.
set -e
cd "$(dirname "$0")/FlyingMachine"
dotnet build -c Release
DEST="$APPDATA/VintagestoryData/Mods/flyingmachine"
rm -rf "$DEST"
cp -r bin/Release/Mods/mod "$DEST"
echo "Deployed to $DEST"
