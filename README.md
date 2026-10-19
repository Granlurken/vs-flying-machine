# Flying Machine

A homebuilt biplane for [Vintage Story](https://www.vintagestory.at). Wood, linen and a
hammered-iron engine. It flies, mostly.

Mod ID: `flyingmachine` · Game version: 1.22

## Features

- **Built, not crafted.** Wings, engine, propeller, wheels and an instrument panel are
  made at the workbench and the anvil, then assembled step by step on a building site
  out in the open (9×9 blocks of flat ground).
- **Runs on Aqua Vitae.** Pour it into the tank with a bucket, jug or bottle. When the
  tank runs dry the engine dies and you glide.
- **Simple flight model.** Throttle and nose stay where you leave them, it stalls when
  too slow, and it needs a runway to take off.
- **Instruments.** Speed, altitude, fuel, compass, variometer and warning lamps.
- **Passenger seat and cargo shelf** for a chest, basket or crate.
- **Take it apart with a wrench** and get back every part it was built from, then
  rebuild it somewhere else.
- **Multiplayer-aware.** Only the builder (or admins / claim holders) can take it apart,
  one player fills the tank at a time, and you can't jump out mid-flight.

The in-game handbook has a full guide (search for "Flying Machine").

## Building from source

Requires the .NET 10 SDK and a Vintage Story install. The build looks for the game in
`%APPDATA%\Vintagestory`; set the `VINTAGE_STORY` environment variable if yours is elsewhere.

```sh
./build.sh      # packages a release zip into Releases/
./deploy.sh     # builds and copies the mod into your VintagestoryData/Mods folder
```

`tools/` holds the Node scripts that generate the model, item shapes and gauge textures.

## Testing

```sh
VINTAGE_STORY=~/path/to/vintagestory ./test.sh
```

This runs two checks:

- `node tools/check_generated.js` runs the generators in a temporary copy and fails if the
  committed shapes or textures differ from what they produce.
- `dotnet test tests/FlyingMachine.Tests` runs [Atlas](https://github.com/Pixnop/Atlas)
  scenarios. Atlas boots a headless Vintage Story server in-process with the mod loaded.
  The scenarios cover registration, recipes, lang, fuel and refuelling, the building
  site's ground check, ownership, and build materials.

Extra arguments go to `dotnet test`, for example
`./test.sh --filter "FullyQualifiedName~PlaneScenarios"`. CI (`.github/workflows/ci.yml`)
runs both checks against the 1.22.7 dedicated server and uploads the packaged zip.

## License

Code and assets are under the [MIT License](LICENSE): do what you like with them, just
keep the license notice. The engine sounds are CC0, see [CREDITS](CREDITS.md).
