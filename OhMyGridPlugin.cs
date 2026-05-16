using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using System.Collections.Generic;
using UnityEngine;

namespace OhMyGrid
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class OhMyGridPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "cgaggino.OhMyGrid";
        public const string PluginName = "OhMyGrid";
        public const string PluginVersion = "0.1.0";

        private const float MinRadius = 0f;
        private const float MaxRadius = 32f;

        internal static ManualLogSource Log;

        private readonly Harmony _harmony = new Harmony(PluginGuid);

        private ConfigEntry<float> _innerRadius;
        private ConfigEntry<float> _outerRadius;
        private ConfigEntry<float> _spacing;

        private ConfigEntry<KeyboardShortcut> _dumpGridHotkey;
        private ConfigEntry<KeyboardShortcut> _lockCenterHotkey;
        private ConfigEntry<KeyboardShortcut> _increaseOuterHotkey;
        private ConfigEntry<KeyboardShortcut> _decreaseOuterHotkey;
        private ConfigEntry<KeyboardShortcut> _increaseInnerHotkey;
        private ConfigEntry<KeyboardShortcut> _decreaseInnerHotkey;

        private bool _centerLocked;
        private Vector3 _lockedCenter;

        private readonly List<GameObject> _ghostClones = new List<GameObject>();
        private GameObject _lastSourceGhost;

        // m_placementGhost is private on Player; FieldRef gives cheap typed access.
        private static readonly AccessTools.FieldRef<Player, GameObject> PlacementGhostRef =
            AccessTools.FieldRefAccess<Player, GameObject>("m_placementGhost");

        private void Awake()
        {
            Log = Logger;

            _innerRadius = Config.Bind("Grid", "InnerRadius", 2f,
                "Inner radius (m) of the donut. Points inside this radius are skipped.");
            _outerRadius = Config.Bind("Grid", "OuterRadius", 6f,
                "Outer radius (m) of the donut.");
            _spacing = Config.Bind("Grid", "Spacing", 1f,
                "Spacing (m) between rings and target spacing along each ring.");

            _dumpGridHotkey = Config.Bind("Hotkeys", "DumpGrid",
                new KeyboardShortcut(KeyCode.F8),
                "Dump donut grid points around the donut center to the log.");
            _lockCenterHotkey = Config.Bind("Hotkeys", "LockCenter",
                new KeyboardShortcut(KeyCode.F7),
                "Toggle locking the donut center at the current player position.");
            _increaseOuterHotkey = Config.Bind("Hotkeys", "IncreaseOuter",
                new KeyboardShortcut(KeyCode.RightBracket),
                "Increase outer radius by one Spacing step.");
            _decreaseOuterHotkey = Config.Bind("Hotkeys", "DecreaseOuter",
                new KeyboardShortcut(KeyCode.LeftBracket),
                "Decrease outer radius by one Spacing step.");
            _increaseInnerHotkey = Config.Bind("Hotkeys", "IncreaseInner",
                new KeyboardShortcut(KeyCode.RightBracket, KeyCode.LeftShift),
                "Increase inner radius by one Spacing step.");
            _decreaseInnerHotkey = Config.Bind("Hotkeys", "DecreaseInner",
                new KeyboardShortcut(KeyCode.LeftBracket, KeyCode.LeftShift),
                "Decrease inner radius by one Spacing step.");

            Log.LogInfo($"{PluginName} v{PluginVersion} loaded — hello from the grid.");

            _harmony.PatchAll();
        }

        private void Update()
        {
            HandleHotkeys();
            UpdateGhostClones();
        }

        private void HandleHotkeys()
        {
            if (_dumpGridHotkey.Value.IsDown()) DumpGrid();

            if (_lockCenterHotkey.Value.IsDown()) ToggleCenterLock();

            // Adjust by one Spacing step so each press adds/removes exactly one ring.
            // Check modifier-bound (inner) before bare (outer), so Shift+] doesn't also trigger ].
            var step = Mathf.Max(_spacing.Value, 0.01f);
            var innerFired = false;
            if (_increaseInnerHotkey.Value.IsDown())
            {
                AdjustRadius(_innerRadius, +step);
                innerFired = true;
            }
            else if (_decreaseInnerHotkey.Value.IsDown())
            {
                AdjustRadius(_innerRadius, -step);
                innerFired = true;
            }
            if (!innerFired)
            {
                if (_increaseOuterHotkey.Value.IsDown()) AdjustRadius(_outerRadius, +step);
                else if (_decreaseOuterHotkey.Value.IsDown()) AdjustRadius(_outerRadius, -step);
            }
        }

        private void ToggleCenterLock()
        {
            var player = Player.m_localPlayer;
            if (player == null) return;
            _centerLocked = !_centerLocked;
            _lockedCenter = player.transform.position;
            Log.LogInfo(_centerLocked
                ? $"Center locked at ({_lockedCenter.x:0.##}, {_lockedCenter.z:0.##})."
                : "Center unlocked — follows player.");
        }

        private void AdjustRadius(ConfigEntry<float> entry, float delta)
        {
            entry.Value = Mathf.Clamp(entry.Value + delta, MinRadius, MaxRadius);
            Log.LogInfo($"{entry.Definition.Key} = {entry.Value:0.##}m " +
                        $"(inner={_innerRadius.Value:0.##} outer={_outerRadius.Value:0.##})");
        }

        private Vector3 GetDonutCenter(Player player)
        {
            return _centerLocked ? _lockedCenter : player.transform.position;
        }

        private void DumpGrid()
        {
            var player = Player.m_localPlayer;
            if (player == null) { Log.LogInfo("DumpGrid: no local player (in menu?)."); return; }
            var center = GetDonutCenter(player);
            Log.LogInfo(
                $"Donut grid @ ({center.x:0.##}, {center.z:0.##}) " +
                $"inner={_innerRadius.Value} outer={_outerRadius.Value} spacing={_spacing.Value}");
            var count = 0;
            foreach (var p in GridGenerator.Donut(center.x, center.z,
                         _innerRadius.Value, _outerRadius.Value, _spacing.Value))
            {
                Log.LogInfo($"  [{count++}] {p}");
            }
            Log.LogInfo($"Donut grid: {count} points.");
        }

        private void UpdateGhostClones()
        {
            var player = Player.m_localPlayer;
            if (player == null) { HideAllClones(); return; }

            var ghost = PlacementGhostRef(player);
            // Only render clones when the active ghost is a Plant (cultivator + seed selected).
            if (ghost == null || ghost.GetComponent<Plant>() == null)
            {
                HideAllClones();
                _lastSourceGhost = null;
                return;
            }

            // Source changed (player swapped plant) — destroy clones so we re-Instantiate the new visual.
            if (ghost != _lastSourceGhost)
            {
                DestroyAllClones();
                _lastSourceGhost = ghost;
            }

            var center = GetDonutCenter(player);
            var points = new List<GridPoint>();
            foreach (var p in GridGenerator.Donut(center.x, center.z,
                         _innerRadius.Value, _outerRadius.Value, _spacing.Value))
            {
                points.Add(p);
            }

            while (_ghostClones.Count < points.Count)
            {
                var clone = Object.Instantiate(ghost);
                foreach (var c in clone.GetComponentsInChildren<Collider>())
                {
                    c.enabled = false;
                }
                _ghostClones.Add(clone);
            }

            for (int i = 0; i < points.Count; i++)
            {
                var pt = points[i];
                var clone = _ghostClones[i];
                var y = SampleGroundY(pt.X, pt.Z, ghost.transform.position.y);
                clone.transform.position = new Vector3(pt.X, y, pt.Z);
                clone.transform.rotation = ghost.transform.rotation;
                if (!clone.activeSelf) clone.SetActive(true);
            }
            for (int i = points.Count; i < _ghostClones.Count; i++)
            {
                if (_ghostClones[i].activeSelf) _ghostClones[i].SetActive(false);
            }
        }

        private static float SampleGroundY(float x, float z, float fallback)
        {
            var zone = ZoneSystem.instance;
            if (zone == null) return fallback;
            if (zone.GetGroundHeight(new Vector3(x, 1000f, z), out var y)) return y;
            return fallback;
        }

        private void HideAllClones()
        {
            for (int i = 0; i < _ghostClones.Count; i++)
            {
                var c = _ghostClones[i];
                if (c != null && c.activeSelf) c.SetActive(false);
            }
        }

        private void DestroyAllClones()
        {
            for (int i = 0; i < _ghostClones.Count; i++)
            {
                if (_ghostClones[i] != null) Object.Destroy(_ghostClones[i]);
            }
            _ghostClones.Clear();
        }

        private void OnDestroy()
        {
            DestroyAllClones();
            _harmony?.UnpatchSelf();
        }
    }
}
