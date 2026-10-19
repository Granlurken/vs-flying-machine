#!/bin/sh
# Kjører alle sjekkene: genererte assets er à jour, og Atlas-scenariene (headless VS-server med moden lastet).
# Krever Node, .NET 10 og VINTAGE_STORY satt til en 1.22-installasjon (mappa med VintagestoryAPI.dll).
# Ekstra argumenter går videre til dotnet test, f.eks. ./test.sh --filter "FullyQualifiedName~PlaneScenarios"
set -e
cd "$(dirname "$0")"
node tools/check_generated.js
dotnet test tests/FlyingMachine.Tests "$@"
