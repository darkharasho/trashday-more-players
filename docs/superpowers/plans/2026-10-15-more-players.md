# Trash Day More Players Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A host-only BepInEx 5 plugin that raises Trash Day's lobby cap from 6 to a configurable value (default 12). It is packaged and released for Thunderstore like the author's Stonewards mods.

**Architecture:**
- One static helper sets `Ox1.OnlineLobbyManager.MaxConnectedPlayers` through its private setter (Harmony `AccessTools`).
- It is applied in `Plugin.Awake`, again in a Harmony prefix on `OnlineLobbyManager.RequestCreateLobby` (the game resets the value to 6 at startup), and on config change.
- On a config change while hosting, the helper calls the game's private `SetMaxPlayersCommand(int)`. That also updates the live Steam lobby.

**Tech Stack:**
- C# netstandard2.1, BepInEx 5.4 and HarmonyX
- `refasmer`, for the CI reference assemblies
- bash packaging scripts
- GitHub Actions and `tcli`, through `scripts/thunderstore-publish.sh`

**Spec:** `docs/superpowers/specs/2026-10-15-more-players-design.md`

**Sibling to mirror:** `~/Documents/GitHub/stonewards-more-players`. Same layout, scripts and workflow. Only the game-specific parts change.

## Global Constraints

- Plugin GUID `com.darkharasho.trashday.moreplayers`, name `MorePlayers`, version `0.1.0`. The version must match in `Plugin.cs`, the csproj, `thunderstore/manifest.json` and `CHANGELOG.md`.
- Config entry `General.MaxPlayers`, an int with default 12 and `AcceptableValueRange<int>(1, 250)`.
- Only the host needs the mod. Nothing client-side.
- Game path `/var/mnt/data/SteamLibrary/steamapps/common/TrashDay`, managed DLLs in `Trash_Data/Managed`.
- Thunderstore team `darkharasho`, community slug `trash-day` (a placeholder until Thunderstore creates the community).
- Manifest dependency `BepInEx-BepInExPack-5.4.2305`, matching the sibling. The spec said 5.4.2100; 2305 is the current pack used by the sibling mods.
- The package zip root has `manifest.json`, `icon.png` (256×256), `README.md`, `CHANGELOG.md` and `plugins/MorePlayers.dll`.
- Commits end with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`. Commits are signed through 1Password. If signing fails, stop and ask; never pass `--no-gpg-sign`.

## Review Focus

There is no unit-test harness for Unity BepInEx plugins in these repos. The automated checks are the build and the package validation. Game behaviour is checked by hand in Task 4.

1. **Game update renames `MaxConnectedPlayers`, `SetMaxPlayersCommand` or `RequestCreateLobby`.** The plugin should log one error and leave the game at its vanilla 6, not throw out of `Awake`. The `PatchAll` try/catch and null checks in Task 1 cover it; Task 4 step 4 checks the log.
2. **Startup reset after `Awake`.** `ResetOnPlayModeStart` sets 6 at `SubsystemRegistration`. The `RequestCreateLobby` prefix must re-apply before every lobby. Task 4 checks the "Max players" log line appears and the join list shows 12.
3. **Config edited while not hosting.** It should only store the value, with no Steam calls. Task 1's `ApplyLive` branches on `IsLobbyOwner`.
4. **Out-of-range value typed into the .cfg (e.g. 500).** BepInEx clamps it to 250 through `AcceptableValueRange`; the plugin must not pass anything else. Task 4 step 5 checks this.
5. **Lowering the cap below the current player count while hosting.** Existing players stay connected and new joins are refused. That is vanilla `ServerFull` behaviour, documented in the README (Task 2).

---

### Task 1: Plugin project that builds

**Files:**
- Create: `src/MorePlayers/MorePlayers.csproj`
- Create: `src/MorePlayers/Plugin.cs`
- Create: `src/MorePlayers/ConfigurationManagerAttributes.cs`
- Create: `src/MorePlayers/Patches/MaxPlayersPatches.cs`
- Create: `scripts/update-refs.sh`
- Create: `lib/refs/*.dll` (generated)

**Interfaces:**
- Produces:
  - `Plugin.MaxPlayers` (`ConfigEntry<int>`)
  - `Plugin.Log` (`ManualLogSource`)
  - `MorePlayers.Patches.MaxPlayersCap.Apply()` and `.ApplyLive()`
  - build output `src/MorePlayers/bin/Release/MorePlayers.dll`

- [ ] **Step 1: Write the csproj**

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>netstandard2.1</TargetFramework>
    <AssemblyName>MorePlayers</AssemblyName>
    <RootNamespace>MorePlayers</RootNamespace>
    <Version>0.1.0</Version>
    <LangVersion>latest</LangVersion>
    <Nullable>disable</Nullable>
    <GenerateDocumentationFile>false</GenerateDocumentationFile>
    <AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>
    <DebugType>embedded</DebugType>
  </PropertyGroup>

  <!-- Machine-specific paths. Override in Local.props (gitignored) or with -p:GamePath=... -->
  <Import Project="$(MSBuildThisFileDirectory)../../Local.props" Condition="Exists('$(MSBuildThisFileDirectory)../../Local.props')" />

  <!-- There's no r2modman community for Trash Day yet, so BepInEx is installed straight into the game folder. -->
  <PropertyGroup>
    <GamePath Condition="'$(GamePath)' == ''">/var/mnt/data/SteamLibrary/steamapps/common/TrashDay</GamePath>
    <ProfilePath Condition="'$(ProfilePath)' == ''">$(GamePath)</ProfilePath>
    <ManagedPath>$(GamePath)/Trash_Data/Managed</ManagedPath>
    <BepInExCorePath>$(ProfilePath)/BepInEx/core</BepInExCorePath>
    <DeployPath>$(ProfilePath)/BepInEx/plugins/darkharasho-MorePlayers</DeployPath>
  </PropertyGroup>

  <!-- Without the game installed (CI), or with -p:UseRefs=true, build against the reference
       assemblies from scripts/update-refs.sh. -->
  <PropertyGroup Condition="'$(UseRefs)' == 'true' Or !Exists('$(ManagedPath)/Assembly-CSharp.dll')">
    <ManagedPath>$(MSBuildThisFileDirectory)../../lib/refs</ManagedPath>
  </PropertyGroup>
  <PropertyGroup Condition="'$(UseRefs)' == 'true' Or !Exists('$(BepInExCorePath)/BepInEx.dll')">
    <BepInExCorePath>$(MSBuildThisFileDirectory)../../lib/refs</BepInExCorePath>
  </PropertyGroup>

  <ItemGroup>
    <Reference Include="BepInEx" HintPath="$(BepInExCorePath)/BepInEx.dll" Private="false" />
    <Reference Include="0Harmony" HintPath="$(BepInExCorePath)/0Harmony.dll" Private="false" />
    <Reference Include="Assembly-CSharp" HintPath="$(ManagedPath)/Assembly-CSharp.dll" Private="false" />
    <Reference Include="UnityEngine" HintPath="$(ManagedPath)/UnityEngine.dll" Private="false" />
    <Reference Include="UnityEngine.CoreModule" HintPath="$(ManagedPath)/UnityEngine.CoreModule.dll" Private="false" />
  </ItemGroup>

  <Target Name="DeployToProfile" AfterTargets="Build" Condition="Exists('$(ProfilePath)/BepInEx/plugins')">
    <MakeDir Directories="$(DeployPath)" />
    <Copy SourceFiles="$(TargetPath)" DestinationFolder="$(DeployPath)" />
    <Message Importance="high" Text="Deployed $(TargetFileName) to $(DeployPath)" />
  </Target>

</Project>
```

- [ ] **Step 2: Write `ConfigurationManagerAttributes.cs`.** It is the sibling's file with the mod name changed, so the future `trashday-mod-settings` can read `DispName` by field name.

```csharp
namespace MorePlayers
{
    /// <summary>
    /// Tag for a setting's <see cref="BepInEx.Configuration.ConfigDescription"/> that tells a settings menu
    /// (BepInEx.ConfigurationManager, or the planned darkharasho Trash Day ModSettings) how to show it. Read by
    /// field name, so no reference to that mod is needed and the mod works the same without it.
    /// </summary>
    internal sealed class ConfigurationManagerAttributes
    {
        public string DispName;
        public int? Order;
    }
}
```

- [ ] **Step 3: Write `Plugin.cs`**

```csharp
using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using MorePlayers.Patches;

namespace MorePlayers
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.darkharasho.trashday.moreplayers";
        public const string PluginName = "MorePlayers";
        public const string PluginVersion = "0.1.0";

        internal static ManualLogSource Log;
        internal static ConfigEntry<int> MaxPlayers;

        private Harmony _harmony;

        private void Awake()
        {
            Log = Logger;

            MaxPlayers = Config.Bind("General", "MaxPlayers", 12, new ConfigDescription(
                "Most players allowed in a lobby you host, including you. The game's default is 6. Only the host needs this mod. Changing it while hosting updates the current lobby.",
                new AcceptableValueRange<int>(1, 250),
                new ConfigurationManagerAttributes { DispName = "Max players" }));
            MaxPlayers.SettingChanged += (_, _) => MaxPlayersCap.ApplyLive();

            _harmony = new Harmony(PluginGuid);
            try
            {
                _harmony.PatchAll();
            }
            catch (Exception e)
            {
                Log.LogError($"Patching failed, likely a game update; the player cap stays at the game's default. {e}");
                _harmony.UnpatchSelf();
                return;
            }

            MaxPlayersCap.Apply();
            Log.LogInfo($"{PluginName} {PluginVersion} loaded (max players {MaxPlayers.Value})");
        }

        private void OnDestroy() => _harmony?.UnpatchSelf();
    }
}
```

- [ ] **Step 4: Write `Patches/MaxPlayersPatches.cs`**

```csharp
using System.Reflection;
using HarmonyLib;
using Ox1;

namespace MorePlayers.Patches
{
    // OnlineLobbyManager.MaxConnectedPlayers is the one value behind the cap. It sizes the Steam lobby
    // (RequestCreateLobby), is written to the lobby's "MaxPlayers" data that the join list reads, and is the
    // limit OnlineSession's connection approval checks before answering ServerFull. Its setter is private.
    internal static class MaxPlayersCap
    {
        private static readonly MethodInfo Setter =
            AccessTools.PropertySetter(typeof(OnlineLobbyManager), nameof(OnlineLobbyManager.MaxConnectedPlayers));

        // The game's own "trash.max-players" console command. Besides setting the value, it updates the current
        // lobby's "MaxPlayers" data and Steam member limit when we own the lobby.
        private static readonly MethodInfo SetMaxPlayersCommand =
            AccessTools.Method(typeof(OnlineLobbyManager), "SetMaxPlayersCommand", new[] { typeof(int) });

        private static bool _loggedMissing;

        internal static void Apply()
        {
            int value = Plugin.MaxPlayers.Value;
            if (OnlineLobbyManager.MaxConnectedPlayers == value) return;
            if (Setter == null)
            {
                LogMissingOnce("OnlineLobbyManager.MaxConnectedPlayers setter");
                return;
            }
            Setter.Invoke(null, new object[] { value });
            Plugin.Log.LogInfo($"Max players: {value}");
        }

        internal static void ApplyLive()
        {
            if (!OnlineLobbyManager.IsLobbyOwner || SetMaxPlayersCommand == null)
            {
                if (OnlineLobbyManager.IsLobbyOwner) LogMissingOnce("OnlineLobbyManager.SetMaxPlayersCommand");
                Apply();
                return;
            }
            SetMaxPlayersCommand.Invoke(null, new object[] { Plugin.MaxPlayers.Value });
            Plugin.Log.LogInfo($"Max players: {Plugin.MaxPlayers.Value} (applied to the current lobby)");
        }

        private static void LogMissingOnce(string member)
        {
            if (_loggedMissing) return;
            _loggedMissing = true;
            Plugin.Log.LogError($"{member} not found, likely a game update; the player cap may stay at the game's default.");
        }
    }

    // The game resets the cap to 6 at startup (ResetOnPlayModeStart), so set it again right before every lobby
    // is created. Also picks up config file edits made since the last lobby.
    [HarmonyPatch(typeof(OnlineLobbyManager), "RequestCreateLobby")]
    internal static class ApplyBeforeCreateLobbyPatch
    {
        private static void Prefix() => MaxPlayersCap.Apply();
    }
}
```

- [ ] **Step 5: Write `scripts/update-refs.sh`** and `chmod +x` it.

```bash
#!/usr/bin/env bash
# Regenerates lib/refs: reference-only copies (public API signatures, no method bodies) of the
# game and BepInEx assemblies, so CI can build without the game installed.
# Needs the refasmer tool: dotnet tool install -g JetBrains.Refasmer.CliTool
set -euo pipefail

root="$(cd "$(dirname "$0")/.." && pwd)"
game_root="${GAME_PATH:-/var/mnt/data/SteamLibrary/steamapps/common/TrashDay}"
game="$game_root/Trash_Data/Managed"
# Any BepInEx 5.4 core works. Falls back to the Stonewards r2modman profile if BepInEx isn't in the game folder.
bepinex="${BEPINEX_CORE:-$game_root/BepInEx/core}"
[ -f "$bepinex/BepInEx.dll" ] || bepinex="$HOME/.config/r2modmanPlus-local/Stonewards/profiles/Default/BepInEx/core"
out="$root/lib/refs"

# Keep in sync with the <Reference> items in src/MorePlayers/MorePlayers.csproj.
game_dlls=(Assembly-CSharp UnityEngine UnityEngine.CoreModule)
bepinex_dlls=(BepInEx 0Harmony)

rm -rf "$out"
mkdir -p "$out"
for name in "${game_dlls[@]}"; do refasmer -q -c --omit-non-api-members=true -O "$out" "$game/$name.dll"; done
for name in "${bepinex_dlls[@]}"; do refasmer -q -c --omit-non-api-members=true -O "$out" "$bepinex/$name.dll"; done
ls -l "$out"
```

- [ ] **Step 6: Generate the refs**

Run: `PATH="$HOME/.dotnet/tools:$PATH" DOTNET_ROOT=/home/linuxbrew/.linuxbrew/opt/dotnet/libexec scripts/update-refs.sh`
Expected: `lib/refs` lists 5 DLLs.

`--omit-non-api-members=true` drops private members. Our private-member access goes through `AccessTools` strings or `nameof` on the public getter, so this is fine.

- [ ] **Step 7: Build against the real game, then against the refs (as CI would)**

Run: `dotnet build src/MorePlayers/MorePlayers.csproj -c Release && dotnet build src/MorePlayers/MorePlayers.csproj -c Release -p:UseRefs=true`
Expected: both print `Build succeeded` with 0 errors.

- [ ] **Step 8: Commit**

```bash
git add src scripts/update-refs.sh lib
git commit -m "feat: raise Trash Day lobby cap from config (default 12)

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 2: Thunderstore package

**Files:**
- Create: `thunderstore/manifest.json`, `thunderstore/icon.svg`, `thunderstore/icon.png`, `thunderstore/README.md`
- Create: `README.md`, `CHANGELOG.md`
- Create: `scripts/package.sh` (copy of the sibling's; only the game-independent parts are reused)

**Interfaces:**
- Consumes: the Task 1 project at `src/MorePlayers/MorePlayers.csproj`
- Produces: `dist/darkharasho-MorePlayers-0.1.0.zip`

- [ ] **Step 1: Write `thunderstore/manifest.json`**

```json
{
    "name": "MorePlayers",
    "version_number": "0.1.0",
    "website_url": "https://github.com/darkharasho/trashday-more-players",
    "description": "Raises Trash Day's lobby cap from 6 to 12 players (configurable up to 250). Only the host needs it.",
    "dependencies": [
        "BepInEx-BepInExPack-5.4.2305"
    ]
}
```

- [ ] **Step 2: Write `CHANGELOG.md`**

```markdown
# Changelog

## 0.1.0

- First release: configurable player cap for lobbies you host (default 12, up to 250).
```

- [ ] **Step 3: Write `thunderstore/README.md`.** Also copy it to the root `README.md`, adding a "Building" section to the root one.

```markdown
# More Players

Raises Trash Day's lobby cap from 6 to **12** players. The cap is configurable from 1 to 250.

**Only the host needs this mod.** Friends join as usual. The join list shows the bigger lobby to everyone.

## Config

`BepInEx/config/com.darkharasho.trashday.moreplayers.cfg`

| Setting | Default | Notes |
| --- | --- | --- |
| `General.MaxPlayers` | 12 | Includes you. Changing it while hosting updates the current lobby. |

## Notes

- Characters can repeat once there are more players than character models.
- The in-game player list has fixed rows, so players past the last row aren't listed there. They're still in the game.
- Lowering the cap below the current player count doesn't kick anyone; it only refuses new joins.
- Tested with the host and the lobby list. Not yet tested with more than 6 real players. Please report issues on GitHub.
```

Root `README.md` extra section:

```markdown
## Building

- Install BepInEx 5 into the game folder, then run `dotnet build src/MorePlayers/MorePlayers.csproj -c Release`. The build deploys the DLL to `BepInEx/plugins/darkharasho-MorePlayers/`.
- Without the game installed, the build falls back to `lib/refs` (regenerate it with `scripts/update-refs.sh`).
- `scripts/package.sh` builds the Thunderstore zip into `dist/`.
```

- [ ] **Step 4: Write the icon.** Create `thunderstore/icon.svg`: a 256×256 rounded tile with a dark asphalt background, a green trash can, and a yellow "12" badge. Then render it with `rsvg-convert -w 256 -h 256 thunderstore/icon.svg -o thunderstore/icon.png`.

```svg
<svg xmlns="http://www.w3.org/2000/svg" width="256" height="256" viewBox="0 0 256 256">
  <defs>
    <linearGradient id="bg" x1="0" y1="0" x2="0" y2="1">
      <stop offset="0" stop-color="#3a3f44"/>
      <stop offset="1" stop-color="#1c1f22"/>
    </linearGradient>
    <linearGradient id="can" x1="0" y1="0" x2="1" y2="0">
      <stop offset="0" stop-color="#3f8f4a"/>
      <stop offset="0.5" stop-color="#5cb866"/>
      <stop offset="1" stop-color="#2f6e38"/>
    </linearGradient>
    <linearGradient id="badge" x1="0" y1="0" x2="0" y2="1">
      <stop offset="0" stop-color="#ffd84a"/>
      <stop offset="1" stop-color="#e09a12"/>
    </linearGradient>
  </defs>
  <rect width="256" height="256" rx="36" fill="url(#bg)"/>
  <!-- trash can -->
  <rect x="58" y="70" width="112" height="16" rx="6" fill="#6fcf79"/>
  <rect x="96" y="58" width="36" height="14" rx="5" fill="#6fcf79"/>
  <path d="M66 92 H162 L152 214 Q151 222 143 222 H85 Q77 222 76 214 Z" fill="url(#can)"/>
  <path d="M94 108 L98 204 M114 108 V204 M134 108 L130 204" stroke="#24552b" stroke-width="7" stroke-linecap="round"/>
  <!-- player-count badge -->
  <circle cx="186" cy="178" r="50" fill="#15171a"/>
  <circle cx="186" cy="178" r="44" fill="url(#badge)"/>
  <text x="186" y="196" text-anchor="middle" font-family="DejaVu Sans, Arial, sans-serif" font-weight="bold" font-size="50" fill="#1c1f22">12</text>
</svg>
```

Check: `python3 -c "import struct;d=open('thunderstore/icon.png','rb').read(24);print(struct.unpack('>II',d[16:24]))"`. It should print `(256, 256)`. Look at the PNG with the Read tool before moving on.

- [ ] **Step 5: Copy `scripts/package.sh`** verbatim from `~/Documents/GitHub/stonewards-more-players/scripts/package.sh`, then `chmod +x` it. It already uses `src/MorePlayers`, `MorePlayers.dll` and `darkharasho-MorePlayers-$version.zip`, so no edits are needed.

- [ ] **Step 6: Build the package**

Run: `scripts/package.sh`
Expected: the `unzip -l` listing shows exactly `manifest.json`, `icon.png`, `README.md`, `CHANGELOG.md`, `plugins/MorePlayers.dll`, and the script ends with `Package: .../dist/darkharasho-MorePlayers-0.1.0.zip`.

- [ ] **Step 7: Check that the version guard works.** Temporarily change `"version_number"` to `"0.1.1"`, run `scripts/package.sh`, and expect `manifest check failed` naming Plugin.cs, the csproj and CHANGELOG. Revert, then confirm `git diff thunderstore/manifest.json` is empty.

- [ ] **Step 8: Commit**

```bash
git add thunderstore README.md CHANGELOG.md scripts/package.sh
git commit -m "build: Thunderstore package, icon and docs

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 3: Release workflow and project notes

**Files:**
- Create: `.github/workflows/release.yml`
- Create: `scripts/thunderstore-publish.sh` (copied from `~/.claude/templates/thunderstore/thunderstore-publish.sh`)
- Create: `CLAUDE.md`

**Interfaces:**
- Consumes: `scripts/package.sh` → `dist/darkharasho-MorePlayers-$VERSION.zip`
- Produces: a tag `v<version>` that triggers the GitHub Release and the Thunderstore publish

- [ ] **Step 1: Copy the publish script.** `cp ~/.claude/templates/thunderstore/thunderstore-publish.sh scripts/ && chmod +x scripts/thunderstore-publish.sh`. Read its header comment to confirm the arguments (`<zip> --community <slug> [--categories ...]`).

- [ ] **Step 2: Write `.github/workflows/release.yml`.** It is the sibling's workflow with these differences:
- the release notes line names Trash Day;
- the publish step uses `--community trash-day`;
- there is no `--categories` until the community exists, since category slugs are per community. The publish script errors on a first upload without categories; that's acceptable until the community exists.

```yaml
name: Release

on:
  push:
    tags: ["v*"]

permissions:
  contents: write

jobs:
  release:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4

      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: 8.0.x

      - name: Check tag matches manifest version
        run: |
          version="$(jq -r .version_number thunderstore/manifest.json)"
          if [ "v$version" != "$GITHUB_REF_NAME" ]; then
            echo "::error::Tag $GITHUB_REF_NAME does not match manifest version $version"
            exit 1
          fi
          echo "VERSION=$version" >> "$GITHUB_ENV"

      # The game isn't installed here, so the build uses the reference assemblies in lib/refs.
      - name: Build and package
        run: scripts/package.sh

      - name: Release notes from changelog
        run: |
          awk -v v="## $VERSION" '$0 == v {on=1; next} /^## / {on=0} on' CHANGELOG.md > notes.md
          cat >> notes.md <<EOF

          For Trash Day. Install with r2modman, or download \`darkharasho-MorePlayers-$VERSION.zip\` below and use **Settings → Import local mod**. Requires BepInExPack 5.4.2305. Only the host needs it.
          EOF

      - name: Create or update GitHub release
        env:
          GH_TOKEN: ${{ github.token }}
        run: |
          zip="dist/darkharasho-MorePlayers-$VERSION.zip"
          if gh release view "$GITHUB_REF_NAME" >/dev/null 2>&1; then
            gh release upload "$GITHUB_REF_NAME" "$zip" --clobber
            # Deleting a tag to re-push it turns its release into a draft; publish it again.
            gh release edit "$GITHUB_REF_NAME" --draft=false
          else
            gh release create "$GITHUB_REF_NAME" "$zip" --title "MorePlayers $VERSION" --notes-file notes.md
          fi

      # Skips versions already on Thunderstore, so re-pushing a tag is safe. Thunderstore has no Trash Day
      # community yet; "trash-day" is a placeholder slug. Fix it (and add --categories) once the community exists.
      - name: Publish to Thunderstore
        env:
          THUNDERSTORE_TOKEN: ${{ secrets.THUNDERSTORE_TOKEN }}
        run: scripts/thunderstore-publish.sh "dist/darkharasho-MorePlayers-$VERSION.zip" --community trash-day

      - uses: actions/upload-artifact@v4
        with:
          name: darkharasho-MorePlayers-${{ env.VERSION }}
          path: dist/*.zip
```

- [ ] **Step 3: Validate the YAML**

Run: `python3 -c "import yaml,sys;yaml.safe_load(open('.github/workflows/release.yml'));print('ok')"`. If `actionlint` is installed, also run `actionlint`.
Expected: `ok`.

- [ ] **Step 4: Write `CLAUDE.md`**

```markdown
## Project Context

A BepInEx/Harmony mod for Trash Day (Off by One Games, Steam app 3314580, Unity 6000.3 Mono) that raises the 6-player lobby cap. It sets `Ox1.OnlineLobbyManager.MaxConnectedPlayers` through its private setter. That value sizes the Steam lobby, is written to the lobby's `MaxPlayers` data that the join list reads, and is the limit `OnlineSession` checks before answering `ServerFull`. The game resets it to 6 at startup, so a prefix on `RequestCreateLobby` sets it again. Only the host needs it. Packaged for Thunderstore under `darkharasho`.

## Layout

- `src/MorePlayers/Plugin.cs` — config and Harmony bootstrap
- `src/MorePlayers/Patches/MaxPlayersPatches.cs` — `MaxPlayersCap` and the `RequestCreateLobby` prefix
- `scripts/package.sh` builds the Thunderstore zip; `scripts/update-refs.sh` regenerates `lib/refs` for CI
- Release: bump the version in `Plugin.cs`, the csproj, `thunderstore/manifest.json` and `CHANGELOG.md`, then push tag `v<version>`

## Notes

- Decompile with `DOTNET_ROOT=/home/linuxbrew/.linuxbrew/opt/dotnet/libexec ~/.dotnet/tools/ilspycmd -p -o /tmp/trashday-src .../Trash_Data/Managed/Assembly-CSharp.dll`.
- Thunderstore had no Trash Day community at first release. The workflow's `--community trash-day` is a placeholder; set the real slug and `--categories` once it exists.
- No r2modman profile yet: BepInEx is installed straight into the game folder, and the build deploys there.
- Sibling mods: `trashday-admin-menu` and `trashday-mod-settings` (planned). Settings carry `ConfigurationManagerAttributes` so the settings menu can show them.
```

- [ ] **Step 5: Commit**

```bash
git add .github scripts/thunderstore-publish.sh CLAUDE.md
git commit -m "ci: tag-triggered GitHub and Thunderstore release

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 4: In-game verification (needs the user)

BepInEx has to go into the game folder under Proton. **Ask the user before installing it**, because it changes their game install.

- [ ] **Step 1: Install BepInEx 5 (x64).** Download `BepInEx_win_x64_5.4.23.x.zip` from https://github.com/BepInEx/BepInEx/releases into a fresh empty directory under `/tmp`. Extract it into the game folder. Then tell the user to set the Steam launch options to `WINEDLLOVERRIDES="winhttp=n,b" %command%`.

- [ ] **Step 2: Deploy and launch.** Run `dotnet build src/MorePlayers/MorePlayers.csproj -c Release` and expect `Deployed MorePlayers.dll to .../BepInEx/plugins/darkharasho-MorePlayers`. The user launches the game once and hosts a lobby.

- [ ] **Step 3: Check the log.** In `BepInEx/LogOutput.log`, expect `Max players: 12` and `MorePlayers 0.1.0 loaded (max players 12)`. Also expect the game's own `SetLobbyData MaxPlayers=12` debug line when the lobby is created, if Unity logs are forwarded.

- [ ] **Step 4: Check the failure path.** Temporarily rename `"RequestCreateLobby"` to `"RequestCreateLobbyX"` in the patch and rebuild. Launch and expect one `Patching failed` error, with the game still working at 6. Revert and rebuild.

- [ ] **Step 5: Check the clamp.** Set `MaxPlayers = 500` in the .cfg and relaunch. Expect `Max players: 250`. Then set it back to 12.

- [ ] **Step 6: Second account (if available).** A friend or second Steam account opens the join list and should see 12 player boxes on the host's lobby.

---

### Task 5: Publish the repo (needs the user's go-ahead)

- [ ] **Step 1:** Ask the user before creating a public GitHub repo. Then run `gh repo create darkharasho/trashday-more-players --public --source . --push`.
- [ ] **Step 2:** Set the secret without printing it: `( eval "$(grep '^export THUNDERSTORE_ACCESS_TOKEN=' ~/.bashrc)"; printf %s "$THUNDERSTORE_ACCESS_TOKEN" | gh secret set THUNDERSTORE_TOKEN -R darkharasho/trashday-more-players )`.
- [ ] **Step 3:** Don't push a `v0.1.0` tag until the user says so. A tag creates a public GitHub Release, and once the community exists it publishes a version that can never be replaced.
