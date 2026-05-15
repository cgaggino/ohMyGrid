using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

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

        private ConfigEntry<KeyboardShortcut> _dumpGridHotkey;

        private void Awake()
        {
            Log = Logger;

            _dumpGridHotkey = Config.Bind(
                "Debug",
                "DumpGridHotkey",
                new KeyboardShortcut(KeyCode.F8),
                "Hotkey: dump donut grid points around the player to the log.");

            Log.LogInfo($"{PluginName} v{PluginVersion} loaded — hello from the grid.");

            _harmony.PatchAll();
        }

        private void Update()
        {
            if (!_dumpGridHotkey.Value.IsDown()) return;

            var player = Player.m_localPlayer;
            if (player == null)
            {
                Log.LogInfo("DumpGridHotkey pressed but no local player (in menu?).");
                return;
            }

            var pos = player.transform.position;
            const float innerRadius = 2f;
            const float outerRadius = 6f;
            const float spacing = 1f;

            Log.LogInfo(
                $"Donut grid @ ({pos.x:0.##}, {pos.z:0.##}) " +
                $"inner={innerRadius} outer={outerRadius} spacing={spacing}");

            var count = 0;
            foreach (var p in GridGenerator.Donut(pos.x, pos.z, innerRadius, outerRadius, spacing))
            {
                Log.LogInfo($"  [{count++}] {p}");
            }
            Log.LogInfo($"Donut grid: {count} points.");
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
        }
    }
}
