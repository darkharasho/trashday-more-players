using System;
using System.Reflection;
using System.Runtime.CompilerServices;
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
            AccessTools.PropertySetter(typeof(OnlineLobbyManager), "MaxConnectedPlayers");

        // The game's own "trash.max-players" console command. Besides setting the value, it updates the current
        // lobby's "MaxPlayers" data and Steam member limit when we own the lobby.
        private static readonly MethodInfo SetMaxPlayersCommand =
            AccessTools.Method(typeof(OnlineLobbyManager), "SetMaxPlayersCommand", new[] { typeof(int) });

        private static bool _loggedFailure;

        // Each entry point catches everything, so a game update that renames what we touch leaves the game at
        // its own default instead of breaking hosting. The work lives in separate non-inlined methods so a
        // missing member fails inside the try rather than when the caller is compiled.
        internal static void Apply()
        {
            try { ApplyCore(); }
            catch (Exception e) { LogFailureOnce(e); }
        }

        internal static void ApplyLive()
        {
            try { ApplyLiveCore(); }
            catch (Exception e) { LogFailureOnce(e); }
        }

        // RequestCreateLobby is also called directly when a lost lobby is recreated, which re-sends the lobby
        // data stored earlier, so refresh the stored "MaxPlayers" too.
        internal static void ApplyWithLobbyData()
        {
            try
            {
                ApplyCore();
                SetLobbyMaxPlayers();
            }
            catch (Exception e) { LogFailureOnce(e); }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ApplyCore()
        {
            int value = Plugin.MaxPlayers.Value;
            if (OnlineLobbyManager.MaxConnectedPlayers == value) return;
            if (Setter == null) throw new MissingMemberException(nameof(OnlineLobbyManager), "MaxConnectedPlayers setter");
            Setter.Invoke(null, new object[] { value });
            Plugin.Log.LogInfo($"Max players: {value}");
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ApplyLiveCore()
        {
            if (!OnlineLobbyManager.IsLobbyOwner)
            {
                ApplyCore();
                return;
            }
            if (SetMaxPlayersCommand == null) throw new MissingMemberException(nameof(OnlineLobbyManager), "SetMaxPlayersCommand");
            // Logs "Max players: N (applied to the current lobby)" itself.
            SetMaxPlayersCommand.Invoke(null, new object[] { Plugin.MaxPlayers.Value });
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void SetLobbyMaxPlayers() =>
            OnlineLobbyManager.SetLobbyData(OnlineLobbyManager.MaxPlayersKey, OnlineLobbyManager.MaxConnectedPlayers.ToString());

        private static void LogFailureOnce(Exception e)
        {
            if (_loggedFailure) return;
            _loggedFailure = true;
            Plugin.Log.LogError($"Couldn't set the player cap, likely a game update; it may stay at the game's default. {e}");
        }
    }

    // The game resets the cap to 6 at startup (ResetOnPlayModeStart). CreateLobby writes the cap into the lobby
    // data before it creates the Steam lobby, so set it first. Also picks up config file edits since the last lobby.
    [HarmonyPatch(typeof(OnlineLobbyManager), nameof(OnlineLobbyManager.CreateLobby))]
    internal static class ApplyBeforeCreateLobbyPatch
    {
        private static void Prefix() => MaxPlayersCap.Apply();
    }

    // Covers the lost-lobby recreate path, which skips CreateLobby.
    [HarmonyPatch(typeof(OnlineLobbyManager), "RequestCreateLobby")]
    internal static class ApplyBeforeRequestCreateLobbyPatch
    {
        private static void Prefix() => MaxPlayersCap.ApplyWithLobbyData();
    }
}
