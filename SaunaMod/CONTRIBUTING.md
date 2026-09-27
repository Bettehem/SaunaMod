# Contributing to SaunaMod

Contributions are welcome. Bug fixes, compatibility improvements, localization fixes and well-scoped gameplay improvements are especially useful.

## Before making a larger change

For substantial gameplay changes or new systems, please open an issue first so the approach can be discussed before a lot of code is written.

## Development setup

1. Clone the repository.
2. Copy `Environment.props.example` to `Environment.props` and set your local Valheim path if the project does not detect it automatically.
3. Restore the NuGet packages in Visual Studio.
4. Open the solution and build the project.
5. Test changes in a clean Valheim profile with BepInEx and Jötunn installed.

Do not commit Valheim game assemblies, Unity-generated `Library` files, build output, or your local `Environment.props`.

## Pull requests

Please keep pull requests focused on one change where practical. Include a short explanation of what changed and how it was tested.

For gameplay changes, mention whether the change was tested in multiplayer. SaunaMod configuration and gameplay behaviour are intended to remain server-authoritative where applicable.

## Style

Keep code comments in English. Preserve existing gameplay behaviour unless the pull request intentionally changes it. Avoid modifying shared vanilla materials or prefabs globally when a local clone can be used instead.

## License

By contributing, you agree that your contribution may be distributed under the repository's MIT License.
