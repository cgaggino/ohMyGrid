using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using System;
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
        // Cached components per clone (parallel to _ghostClones) to avoid
        // GetComponent every frame for the validity tint check.
        private readonly List<Plant> _ghostClonePlants = new List<Plant>();
        private readonly List<Piece> _ghostClonePieces = new List<Piece>();
        private GameObject _lastSourceGhost;

        private static OhMyGridPlugin Instance;
        private static bool _inDonutPlace;

        // m_placementGhost is private on Player; FieldRef gives cheap typed access.
        private static readonly AccessTools.FieldRef<Player, GameObject> PlacementGhostRef =
            AccessTools.FieldRefAccess<Player, GameObject>("m_placementGhost");

        // Plant.HaveGrowSpace is private in 0.221.x; MethodDelegate gives a fast typed call.
        private static readonly Func<Plant, bool> HaveGrowSpaceCall =
            AccessTools.MethodDelegate<Func<Plant, bool>>(
                AccessTools.Method(typeof(Plant), "HaveGrowSpace"));

        private void Awake()
        {
            Log = Logger;
            Instance = this;

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
                var clone = UnityEngine.Object.Instantiate(ghost);
                foreach (var c in clone.GetComponentsInChildren<Collider>())
                {
                    c.enabled = false;
                }
                _ghostClones.Add(clone);
                _ghostClonePlants.Add(clone.GetComponent<Plant>());
                _ghostClonePieces.Add(clone.GetComponent<Piece>());
            }

            for (int i = 0; i < points.Count; i++)
            {
                var pt = points[i];
                var clone = _ghostClones[i];
                var y = SampleGroundY(pt.X, pt.Z, ghost.transform.position.y);
                clone.transform.position = new Vector3(pt.X, y, pt.Z);
                clone.transform.rotation = ghost.transform.rotation;
                if (!clone.activeSelf) clone.SetActive(true);

                // Per-clone red tint when the position fails Plant.HaveGrowSpace.
                // HaveGrowSpace reads transform.position, so this works after the move above.
                var clonePlant = _ghostClonePlants[i];
                var clonePiece = _ghostClonePieces[i];
                if (clonePiece != null)
                {
                    var invalid = clonePlant != null && !HaveGrowSpaceCall(clonePlant);
                    clonePiece.SetInvalidPlacementHeightlight(invalid);
                }
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
                if (_ghostClones[i] != null) UnityEngine.Object.Destroy(_ghostClones[i]);
            }
            _ghostClones.Clear();
            _ghostClonePlants.Clear();
            _ghostClonePieces.Clear();
        }

        private bool PlaceDonut(Player player, Piece piece, GameObject ghost)
        {
            var center = GetDonutCenter(player);
            var originalGhostPos = ghost.transform.position;
            var originalGhostRot = ghost.transform.rotation;
            var ghostPlant = ghost.GetComponent<Plant>();

            int planted = 0, noSpace = 0;
            bool outOfResources = false;

            _inDonutPlace = true;
            try
            {
                foreach (var p in GridGenerator.Donut(center.x, center.z,
                             _innerRadius.Value, _outerRadius.Value, _spacing.Value))
                {
                    if (!player.HaveRequirements(piece, Player.RequirementMode.CanBuild))
                    {
                        outOfResources = true;
                        break;
                    }

                    var y = SampleGroundY(p.X, p.Z, originalGhostPos.y);
                    var pos = new Vector3(p.X, y, p.Z);

                    // Per-point grow-space check uses the ghost's Plant component at this position.
                    if (ghostPlant != null)
                    {
                        ghost.transform.position = pos;
                        if (!HaveGrowSpaceCall(ghostPlant))
                        {
                            noSpace++;
                            continue;
                        }
                    }

                    try
                    {
                        player.PlacePiece(piece, pos, originalGhostRot, false);
                        planted++;
                    }
                    catch (Exception e)
                    {
                        Log.LogWarning($"PlacePiece failed at ({p.X:0.##}, {p.Z:0.##}): {e.Message}");
                    }
                }
            }
            finally
            {
                _inDonutPlace = false;
                ghost.transform.position = originalGhostPos;
                ghost.transform.rotation = originalGhostRot;
            }

            Log.LogInfo(
                $"Donut plant: planted={planted} noSpace={noSpace}" +
                (outOfResources ? " (out of resources)" : ""));
            return planted > 0;
        }

        [HarmonyPatch(typeof(Player), nameof(Player.TryPlacePiece))]
        private static class TryPlacePiecePatch
        {
            [HarmonyPrefix]
            private static bool Prefix(Player __instance, Piece piece, ref bool __result)
            {
                if (_inDonutPlace) return true;
                if (Instance == null) return true;

                var ghost = PlacementGhostRef(__instance);
                if (ghost == null || ghost.GetComponent<Plant>() == null) return true;

                __result = Instance.PlaceDonut(__instance, piece, ghost);
                return false;
            }
        }

        private void OnDestroy()
        {
            DestroyAllClones();
            _harmony?.UnpatchSelf();
            if (Instance == this) Instance = null;
        }
    }
}
