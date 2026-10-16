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
