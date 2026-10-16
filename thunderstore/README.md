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
