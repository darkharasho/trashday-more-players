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
