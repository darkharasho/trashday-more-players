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
        public const string PluginVersion = "0.1.1";

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
