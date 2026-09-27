# SaunaMod

A functional Viking sauna for Valheim, built with BepInEx and Jötunn.

SaunaMod adds a stone sauna stove, physical steam, progression through the **Well Steamed** effect, sauna whisks, a bucket with ladle, resistance-mead infusions, sauna-only comfort and synchronized server configuration. The mod is designed around recovery, preparation and environmental effects rather than combat bonuses.

## Features

- Functional sauna stove with room-filling steam
- **Steaming** and **Well Steamed** status effects
- Sauna whisks that improve the sauna and reduce the drawbacks of being Wet
- Sauna bucket with ladle that increases steam output
- Resistance mead infusion through sauna steam
- Conditional sauna comfort from bathhouse upgrades
- Server-authoritative synchronized configuration
- Configurable gameplay, steam behaviour and crafting recipes
- Optional lightweight in-game tuning editor
- English, Russian, German, Spanish and Finnish localization
- Multiplayer support

## Requirements

- Valheim
- BepInEx for Valheim
- Jötunn

## Development

The project is based on the Jötunn mod stub structure.

1. Clone the repository.
2. Copy `Environment.props.example` to `Environment.props`.
3. If required, set `VALHEIM_INSTALL` and `MOD_DEPLOYPATH` in your local `Environment.props`.
4. Restore NuGet packages in Visual Studio.
5. Open the solution and build the project.

`Environment.props` is intentionally not tracked because it contains machine-specific paths.

Do not commit game assemblies copied from Valheim into the Unity project. Those files are required only on the local development machine.

## Contributing

Contributions are welcome. See [CONTRIBUTING.md](CONTRIBUTING.md) for the development and pull-request guidelines.

## Distribution

SaunaMod is distributed through Thunderstore and Nexus Mods. Release packages include the compiled DLL together with the required icons and sound assets.

## License

SaunaMod source code is licensed under the [MIT License](LICENSE).

Copyright © 2026 Nekitker.

Valheim and its assets belong to their respective owners. SaunaMod is an unofficial community mod and is not affiliated with Iron Gate or Coffee Stain Publishing.
