using BepInEx;
using BepInEx.Logging;
using HarmonyLib;

namespace OhMyGrid
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class OhMyGridPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "cgaggino.OhMyGrid";
        public const string PluginName = "OhMyGrid";
        public const string PluginVersion = "0.1.0";

        internal static ManualLogSource Log;

        private readonly Harmony _harmony = new Harmony(PluginGuid);

        private void Awake()
        {
            Log = Logger;
            Log.LogInfo($"{PluginName} v{PluginVersion} loaded — hello from the grid.");

            // Patches go here once we have any. CreateAndPatchAll is a no-op
            // until [HarmonyPatch]-decorated classes exist in this assembly.
            _harmony.PatchAll();
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
        }
    }
}
