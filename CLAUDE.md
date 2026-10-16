## Project Context

A BepInEx/Harmony mod for Trash Day (Off by One Games, Steam app 3314580, Unity 6000.3 Mono) that raises the 6-player lobby cap. It sets `Ox1.OnlineLobbyManager.MaxConnectedPlayers` through its private setter. That value sizes the Steam lobby, is written to the lobby's `MaxPlayers` data that the join list reads, and is the limit `OnlineSession` checks before answering `ServerFull`. The game resets it to 6 at startup, so prefixes on `CreateLobby` (which writes the lobby data before creating the Steam lobby) and `RequestCreateLobby` (the lost-lobby recreate path) set it again. Only the host needs it. Packaged for Thunderstore under `darkharasho`.

## Layout

- `src/MorePlayers/Plugin.cs` — config and Harmony bootstrap
- `src/MorePlayers/Patches/MaxPlayersPatches.cs` — `MaxPlayersCap` and the `CreateLobby`/`RequestCreateLobby` prefixes
- `scripts/package.sh` builds the Thunderstore zip; `scripts/update-refs.sh` regenerates `lib/refs` for CI
- Release: bump the version in `Plugin.cs`, the csproj, `thunderstore/manifest.json` and `CHANGELOG.md`, then push tag `v<version>`

## Notes

- Decompile with `DOTNET_ROOT=/home/linuxbrew/.linuxbrew/opt/dotnet/libexec ~/.dotnet/tools/ilspycmd -p -o /tmp/trashday-src .../Trash_Data/Managed/Assembly-CSharp.dll`.
- Thunderstore had no Trash Day community at first release. The workflow's `--community trash-day` is a placeholder; set the real slug and `--categories` once it exists, and remove the step's `continue-on-error`.
- No r2modman profile yet: BepInEx is installed straight into the game folder, and the build deploys there.
- Sibling mods: `trashday-admin-menu` and `trashday-mod-settings` (planned). Settings carry `ConfigurationManagerAttributes` so the settings menu can show them.
