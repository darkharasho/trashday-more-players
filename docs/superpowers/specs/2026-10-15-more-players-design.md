# Trash Day More Players: design

## Goal

Raise Trash Day's 6-player lobby cap. The cap is set in the BepInEx config: default 12, allowed range 1–250. Only the host installs the mod. Packaged for Thunderstore under `darkharasho`.

## Research findings (Trash Day build of 2026-10-13, Unity 6000.3.9f1, Mono)

- `Ox1.OnlineLobbyManager.MaxConnectedPlayers` (a static int with a private setter, default 6) is the single source of truth:
  - `RequestCreateLobby()` passes it to `SteamMatchmaking.CreateLobby`.
  - `CreateLobby(...)` writes it to the lobby data key `"MaxPlayers"`.
  - `Ox1.OnlineSession.ValidatePayload()` rejects joins with `ServerFull` once `ConnectedClientsIds.Count >= MaxConnectedPlayers`.
- `ResetOnPlayModeStart()` (`RuntimeInitializeOnLoadMethod(SubsystemRegistration)`) resets it to 6.
- The game's own `[Command("trash.max-players")] SetMaxPlayersCommand(int)` clamps the value to 1..250. It updates the `"MaxPlayers"` lobby data and calls `SteamMatchmaking.SetLobbyMemberLimit` when we own the lobby.
- These are safe above 6 without client mods:
  - `JoinGameUI.SetPlayerBoxes` builds boxes from the lobby's `MaxPlayers`.
  - `PlayerListUI` hides players past its fixed rows; no exception.
  - `PlayerSpawner.FindSpawnPoint` uses `PlayerSpawnPoint.OverflowHeights` for extra players.
  - Characters are picked randomly and may repeat.

## Design

- **Config:** `General.MaxPlayers`, an int with default 12 and `AcceptableValueRange(1, 250)`.
- **Apply:** set `MaxConnectedPlayers` by reflection on the property's private setter.
  - Apply once in `Plugin.Awake`.
  - Apply again in a Harmony prefix on `OnlineLobbyManager.RequestCreateLobby`, so the value is right even after the game's startup reset.
- **Live changes:** when the config value changes, apply it. If we own the current lobby, invoke `SetMaxPlayersCommand(value)` so Steam's member limit and the lobby data update too.
- **Logging:** log the applied cap at Info level.
- **Error handling:** if the property or method is missing (the game updated), log an error once and leave the game untouched.

## Out of scope

- Client-side UI changes, such as more player-list rows.
- Gameplay scaling by player count. The game has none to scale.

## Packaging and release

- The repo layout mirrors `stonewards-more-players`:
  - `src/MorePlayers` (netstandard2.1 BepInEx 5 plugin)
  - `lib/refs` (trimmed refs for CI)
  - `scripts/{package.sh, update-refs.sh, thunderstore-publish.sh}`
  - `thunderstore/{manifest.json, icon.png, README.md}`
- `.github/workflows/release.yml` runs on a `v*` tag. It builds, packages, creates a GitHub Release, then runs `scripts/thunderstore-publish.sh` with `--community trash-day`.
  - Thunderstore has no Trash Day community yet, so that step fails until the community exists. Update the slug once it's approved.
- Dependency: the BepInEx pack for the Trash Day community, once it exists. Until then the manifest dependency is `BepInEx-BepInExPack-5.4.2100`.

## Testing

- `dotnet build` succeeds.
- `scripts/package.sh` produces a zip with `manifest.json`, `icon.png`, `README.md` and `BepInEx/plugins/...dll` at its root.
- Manual: host with the mod. The BepInEx log shows "Max players: 12", and the join list shows 12 boxes for the lobby.
- Not tested with 7 or more real players; the README says so.
