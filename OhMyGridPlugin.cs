using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace OhMyGrid
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class OhMyGridPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "cgaggino.OhMyGrid";
        public const string PluginName = "OhMyGrid";
        public const string PluginVersion = "1.0.2";

        private const float MinRadius = 0f;
        private const float MaxRadius = 32f;

        internal static ManualLogSource Log;

        private readonly Harmony _harmony = new Harmony(PluginGuid);

        public enum CenterMode { Player, Fixed, Cursor, CursorSnap }
        public enum GeometryMode { Off, Donut }

        private ConfigEntry<float> _innerRadius;
        private ConfigEntry<float> _outerRadius;
        private ConfigEntry<float> _spacing;

        private ConfigEntry<float> _autoSnapRadius;
        private ConfigEntry<CenterMode> _centerMode;
        private ConfigEntry<GeometryMode> _geometryMode;

        private ConfigEntry<bool> _showCostOverlay;

        private ConfigEntry<bool> _massInteractEnabled;
        private ConfigEntry<float> _massInteractRadius;

        private ConfigEntry<KeyboardShortcut> _dumpGridHotkey;
        private ConfigEntry<KeyboardShortcut> _cycleModeHotkey;
        private ConfigEntry<KeyboardShortcut> _cycleGeometryHotkey;
        private ConfigEntry<KeyboardShortcut> _increaseOuterHotkey;
        private ConfigEntry<KeyboardShortcut> _decreaseOuterHotkey;
        private ConfigEntry<KeyboardShortcut> _increaseInnerHotkey;
        private ConfigEntry<KeyboardShortcut> _decreaseInnerHotkey;
        private ConfigEntry<KeyboardShortcut> _massInteractHotkey;

        // Runtime state.
        private Vector3 _fixedCenter;
        private bool _hasSnap;

        private readonly List<GameObject> _ghostClones = new List<GameObject>();
        // Cached per-clone Piece (parallel to _ghostClones) for the red-tint call.
        // Plant on the clone is disabled (see UpdateGhostClones), so we don't keep
        // a parallel list for it — validity is checked against the source ghost's Plant.
        private readonly List<Piece> _ghostClonePieces = new List<Piece>();
        private GameObject _lastSourceGhost;

        // Overlay state (slice F): cached from the source ghost when it changes,
        // and the live count of valid points so the overlay shows only what we'd plant.
        private Piece.Requirement[] _activeResources;
        private GameObject[] _activeGrownPrefabs;
        private string _activePieceLabel;
        private int _validPointsCount;
        private int _totalPointsCount;
        // How many of the valid points the player can actually pay for (int.MaxValue = free build).
        private int _affordableCount = int.MaxValue;
        // Set when cloning the current source ghost failed; preview stays off (and quiet)
        // until Valheim hands us a different ghost, instead of throwing once per frame.
        private bool _cloneFailedForGhost;
        private readonly StringBuilder _overlaySb = new StringBuilder(256);
        private GUIStyle _overlayStyle;

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

            _autoSnapRadius = Config.Bind("Center", "AutoSnapRadius", 12f,
                "Search radius (m) for CursorSnap. The donut snaps to the CENTROID " +
                "of all Plants found within this radius from the cursor — so for " +
                "concentric donut placement, this should be ≥ the outer radius of " +
                "the existing donut you're aligning to.");
            _centerMode = Config.Bind("Center", "Mode", CenterMode.Player,
                "Current center mode. F7 cycles. Persists across sessions; if the " +
                "saved value is Fixed it resets to Player on load (no persisted fixed center).");
            if (_centerMode.Value == CenterMode.Fixed) _centerMode.Value = CenterMode.Player;

            _geometryMode = Config.Bind("Geometry", "Mode", GeometryMode.Donut,
                "Active geometry. Off = vanilla single-plant (mod disabled). Donut = circular ring(s). F6 cycles. Persists.");

            _showCostOverlay = Config.Bind("Overlay", "ShowCostOverlay", true,
                "Show an on-screen overlay with valid-point count, required seeds and expected yield while a plant ghost is active.");

            _massInteractEnabled = Config.Bind("MassInteract", "Enabled", true,
                "Shift+E interacts with all Pickable / Fireplace / Smelter switches within MassInteractRadius.");
            _massInteractRadius = Config.Bind("MassInteract", "Radius", 5f,
                "Search radius (m) for Shift+E mass interact.");

            _dumpGridHotkey = Config.Bind("Hotkeys", "DumpGrid",
                new KeyboardShortcut(KeyCode.F8),
                "Dump donut grid points around the donut center to the log.");
            _cycleModeHotkey = Config.Bind("Hotkeys", "CycleCenterMode",
                new KeyboardShortcut(KeyCode.F7),
                "Cycle the donut center mode: Player → Fixed → Cursor → CursorSnap → Player.");
            _cycleGeometryHotkey = Config.Bind("Hotkeys", "CycleGeometry",
                new KeyboardShortcut(KeyCode.F6),
                "Cycle the geometry: Off → Donut → Off.");
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
            _massInteractHotkey = Config.Bind("Hotkeys", "MassInteract",
                new KeyboardShortcut(KeyCode.E, KeyCode.LeftShift),
                "Mass-interact with all Pickable / Fireplace / Smelter switches in range.");

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

            if (_cycleModeHotkey.Value.IsDown()) CycleMode();

            if (_cycleGeometryHotkey.Value.IsDown()) CycleGeometry();

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

            if (_massInteractEnabled.Value && _massInteractHotkey.Value.IsDown())
            {
                var player = Player.m_localPlayer;
                if (player != null) MassInteract(player);
            }
        }

        private void CycleMode()
        {
            var player = Player.m_localPlayer;
            if (player == null) return;
            var ghost = PlacementGhostRef(player);

            // Snapshot the donut's current resolved center BEFORE switching, so
            // when we land on Fixed it pins exactly where the donut already was.
            var snapshotBefore = GetDonutCenter(player, ghost);

            _centerMode.Value = NextMode(_centerMode.Value);

            string message;
            if (_centerMode.Value == CenterMode.Fixed)
            {
                _fixedCenter = snapshotBefore;
                message = $"Mode: Fixed @ ({_fixedCenter.x:0.##}, {_fixedCenter.z:0.##})";
            }
            else
            {
                message = $"Mode: {_centerMode.Value}";
            }
            Log.LogInfo(message);
            ShowHudMessage(message);
        }

        private static void ShowHudMessage(string text)
        {
            var hud = MessageHud.instance;
            if (hud == null) return;
            hud.ShowMessage(MessageHud.MessageType.TopLeft, text, 0, null, false);
        }

        private static CenterMode NextMode(CenterMode m)
        {
            switch (m)
            {
                case CenterMode.Player: return CenterMode.Fixed;
                case CenterMode.Fixed: return CenterMode.Cursor;
                case CenterMode.Cursor: return CenterMode.CursorSnap;
                case CenterMode.CursorSnap: return CenterMode.Player;
                default: return CenterMode.Player;
            }
        }

        private void CycleGeometry()
        {
            Log.LogInfo($"Geometry: {NextGeometry(_geometryMode.Value)}");
            _geometryMode.Value = NextGeometry(_geometryMode.Value);
            var msg = $"Geometry: {_geometryMode.Value}";
            Log.LogInfo(msg);
            ShowHudMessage(msg);
        }

        private static GeometryMode NextGeometry(GeometryMode g)
        {
            switch (g)
            {
                case GeometryMode.Off: return GeometryMode.Donut;
                case GeometryMode.Donut: return GeometryMode.Off;
                default: return GeometryMode.Donut;
            }
        }

        private void AdjustRadius(ConfigEntry<float> entry, float delta)
        {
            entry.Value = Mathf.Clamp(entry.Value + delta, MinRadius, MaxRadius);
            Log.LogInfo($"{entry.Definition.Key} = {entry.Value:0.##}m " +
                        $"(inner={_innerRadius.Value:0.##} outer={_outerRadius.Value:0.##})");
        }

        private Vector3 GetDonutCenter(Player player, GameObject ghost)
        {
            if (_centerMode.Value != CenterMode.CursorSnap && _hasSnap)
            {
                _hasSnap = false;
            }

            switch (_centerMode.Value)
            {
                case CenterMode.Fixed:
                    return _fixedCenter;

                case CenterMode.Cursor:
                    return ghost != null ? ghost.transform.position : player.transform.position;

                case CenterMode.CursorSnap:
                {
                    var basePos = ghost != null ? ghost.transform.position : player.transform.position;
                    var centroid = FindPlantCentroid(basePos, _autoSnapRadius.Value);
                    if (centroid.HasValue)
                    {
                        if (!_hasSnap)
                        {
                            var c = centroid.Value;
                            Log.LogInfo($"AutoSnap → centroid ({c.x:0.##}, {c.z:0.##}).");
                            ShowHudMessage("AutoSnap: locked");
                            _hasSnap = true;
                        }
                        return centroid.Value;
                    }
                    if (_hasSnap)
                    {
                        Log.LogInfo("AutoSnap: no plants in range.");
                        ShowHudMessage("AutoSnap: lost");
                        _hasSnap = false;
                    }
                    return basePos;
                }

                case CenterMode.Player:
                default:
                    return player.transform.position;
            }
        }

        // Returns the average XYZ of all in-world Plants within radius of `from`,
        // or null if none. Centroid is the right anchor for concentric donuts:
        // standing roughly inside an existing donut yields a centroid ≈ its center.
        // OverlapSphere ignores disabled colliders, so our own clones and the
        // Valheim cursor ghost (both with colliders disabled) don't contribute.
        private static Vector3? FindPlantCentroid(Vector3 from, float radius)
        {
            var hits = Physics.OverlapSphere(from, radius);
            var sum = Vector3.zero;
            var count = 0;
            // Dedupe by GameObject — one Plant can have multiple colliders in children.
            var seen = new HashSet<int>();
            for (int i = 0; i < hits.Length; i++)
            {
                var p = hits[i].GetComponentInParent<Plant>();
                if (p == null) continue;
                var id = p.GetInstanceID();
                if (!seen.Add(id)) continue;
                sum += p.transform.position;
                count++;
            }
            if (count == 0) return null;
            return sum / count;
        }

        private void DumpGrid()
        {
            var player = Player.m_localPlayer;
            if (player == null) { Log.LogInfo("DumpGrid: no local player (in menu?)."); return; }
            var center = GetDonutCenter(player, PlacementGhostRef(player));
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

            // Geometry Off = mod stays out of the way; vanilla single-plant.
            if (_geometryMode.Value == GeometryMode.Off)
            {
                HideAllClones();
                _lastSourceGhost = null;
                _activeResources = null;
                _activeGrownPrefabs = null;
                _activePieceLabel = null;
                _validPointsCount = 0;
                _totalPointsCount = 0;
                return;
            }

            var ghost = PlacementGhostRef(player);
            // Only render clones when the active ghost is a Plant (cultivator + seed selected).
            var sourcePlant = ghost != null ? ghost.GetComponent<Plant>() : null;
            if (ghost == null || sourcePlant == null)
            {
                HideAllClones();
                _lastSourceGhost = null;
                _activeResources = null;
                _activeGrownPrefabs = null;
                _activePieceLabel = null;
                _validPointsCount = 0;
                _totalPointsCount = 0;
                return;
            }

            // Source changed (player swapped plant) — destroy clones so we re-Instantiate the new visual.
            if (ghost != _lastSourceGhost)
            {
                DestroyAllClones();
                _lastSourceGhost = ghost;
                _cloneFailedForGhost = false;

                // Cache the resource/yield info for the overlay (slice F).
                var sourcePiece = ghost.GetComponent<Piece>();
                _activeResources = sourcePiece != null ? sourcePiece.m_resources : null;
                _activeGrownPrefabs = sourcePlant.m_grownPrefabs;
                _activePieceLabel = sourcePiece != null
                    ? LocalizeOrRaw(sourcePiece.m_name)
                    : ghost.name;
            }

            var center = GetDonutCenter(player, ghost);
            var points = new List<GridPoint>();
            foreach (var p in GridGenerator.Donut(center.x, center.z,
                         _innerRadius.Value, _outerRadius.Value, _spacing.Value))
            {
                points.Add(p);
            }
            _totalPointsCount = points.Count;

            if (_cloneFailedForGhost)
            {
                HideAllClones();
                _validPointsCount = 0;
                return;
            }
            if (_ghostClones.Count < points.Count)
            {
                var before = _ghostClones.Count;
                try
                {
                    while (_ghostClones.Count < points.Count)
                    {
                        var clone = CreateGhostClone(ghost);
                        _ghostClones.Add(clone);
                        _ghostClonePieces.Add(clone.GetComponent<Piece>());
                    }
                    Log.LogDebug($"Ghost clones: {before} → {_ghostClones.Count} for '{ghost.name}' (active={ghost.activeSelf})");
                }
                catch (Exception e)
                {
                    _cloneFailedForGhost = true;
                    Log.LogError($"Ghost preview disabled for '{ghost.name}': cloning threw {e.GetType().Name}: {e.Message}");
                    HideAllClones();
                    _validPointsCount = 0;
                    return;
                }
            }

            // Validity check moves the SOURCE ghost (whose Plant is properly set up by
            // Valheim) to each point in turn and calls HaveGrowSpace. We restore the
            // source ghost's transform at the end so Valheim's next UpdatePlacementGhost
            // pass picks up the cursor position normally.
            var originalSourcePos = ghost.transform.position;
            var originalSourceRot = ghost.transform.rotation;
            var playerXZ = new Vector2(player.transform.position.x, player.transform.position.z);
            var maxPlaceDistSq = player.m_maxPlaceDistance * player.m_maxPlaceDistance;
            var needsCultivated = sourcePlant.m_needCultivatedGround;
            var sourcePieceForCost = ghost.GetComponent<Piece>();
            var affordable = CountAffordable(player, sourcePieceForCost);
            var valid = 0;

            for (int i = 0; i < points.Count; i++)
            {
                var pt = points[i];
                var clone = _ghostClones[i];
                var y = SampleGroundY(pt.X, pt.Z, originalSourcePos.y);
                var pos = new Vector3(pt.X, y, pt.Z);
                clone.transform.position = pos;
                clone.transform.rotation = originalSourceRot;
                if (!clone.activeSelf) clone.SetActive(true);

                // Out of reach? Red, never planted.
                var dx = pt.X - playerXZ.x;
                var dz = pt.Z - playerXZ.y;
                var tooFar = dx * dx + dz * dz > maxPlaceDistSq;

                // Cultivated ground required for most plants (carrots/turnips/onions/etc).
                var notCultivated = needsCultivated && !IsCultivatedAt(pos);

                // Move source ghost to test position for HaveGrowSpace.
                ghost.transform.position = pos;
                var noSpace = !HaveGrowSpaceCall(sourcePlant);

                var invalid = tooFar || noSpace || notCultivated;
                // Points the inventory can't pay for are shown red too: the donut only
                // plants what you can afford (in ring order), so this is what will happen.
                var unaffordable = !invalid && valid >= affordable;
                if (!invalid) valid++;
                var clonePiece = _ghostClonePieces[i];
                if (clonePiece != null) clonePiece.SetInvalidPlacementHeightlight(invalid || unaffordable);
            }

            ghost.transform.position = originalSourcePos;
            ghost.transform.rotation = originalSourceRot;
            _validPointsCount = valid;
            _affordableCount = affordable;
            for (int i = points.Count; i < _ghostClones.Count; i++)
            {
                if (_ghostClones[i].activeSelf) _ghostClones[i].SetActive(false);
            }
        }

        /// <summary>
        /// Visual-only copy of the placement ghost. The ghost has no ZNetView (Valheim
        /// strips it via ZNetView.m_forceDisableInit), so if Plant.Awake ran on a clone it
        /// would NRE on m_nview — and since Valheim 1.0 Plant derives from SlowUpdate,
        /// whose Awake registers the object with the world's SlowUpdater *before* that NRE,
        /// leaving a half-built "plant" in the simulation (1.0.1 bugs: no ghosts rendered +
        /// Plant.SUpdate NREs). So: instantiate INACTIVE (no Awake), remove Plant and any
        /// stray ZNetView, then activate. Piece stays (Awake is harmless without a
        /// ZNetView) because SetInvalidPlacementHeightlight is what tints the clone.
        /// </summary>
        private static GameObject CreateGhostClone(GameObject ghost)
        {
            var wasActive = ghost.activeSelf;
            ghost.SetActive(false);
            GameObject clone;
            try
            {
                clone = UnityEngine.Object.Instantiate(ghost);
            }
            finally
            {
                ghost.SetActive(wasActive);
            }
            clone.name = ghost.name + " (OhMyGrid ghost)";
            foreach (var plant in clone.GetComponentsInChildren<Plant>(true))
            {
                UnityEngine.Object.DestroyImmediate(plant);
            }
            foreach (var nview in clone.GetComponentsInChildren<ZNetView>(true))
            {
                UnityEngine.Object.DestroyImmediate(nview);
            }
            foreach (var c in clone.GetComponentsInChildren<Collider>(true))
            {
                c.enabled = false;
            }
            clone.SetActive(true);
            return clone;
        }

        /// <summary>
        /// True when placement is free for this player: no-cost cheat or the world's
        /// free-build global key — the same two exemptions vanilla applies.
        /// </summary>
        private static bool IsFreeBuild(Player player, Piece piece)
        {
            if (player.NoCostCheat()) return true;
            var zs = ZoneSystem.instance;
            return zs != null && piece != null && zs.GetGlobalKey(piece.FreeBuildKey());
        }

        /// <summary>
        /// How many copies of <paramref name="piece"/> the player's inventory can pay for
        /// (int.MaxValue when building is free or the piece costs nothing).
        /// </summary>
        private static int CountAffordable(Player player, Piece piece)
        {
            if (piece == null || IsFreeBuild(player, piece)) return int.MaxValue;
            var inv = player.GetInventory();
            if (inv == null) return 0;
            var affordable = int.MaxValue;
            var resources = piece.m_resources;
            if (resources == null) return int.MaxValue;
            for (int i = 0; i < resources.Length; i++)
            {
                var req = resources[i];
                if (req == null || req.m_resItem == null || req.m_amount <= 0) continue;
                var shared = req.m_resItem.m_itemData?.m_shared;
                if (shared == null) continue;
                var have = inv.CountItems(shared.m_name);
                affordable = Mathf.Min(affordable, have / req.m_amount);
            }
            return affordable;
        }

        /// <summary>"item×perPlant have N, ..." for the log line — never affects gameplay.</summary>
        private static string DescribeStock(Player player, Piece piece)
        {
            var inv = player.GetInventory();
            if (inv == null || piece == null || piece.m_resources == null) return "?";
            var parts = new List<string>();
            foreach (var req in piece.m_resources)
            {
                var shared = req?.m_resItem?.m_itemData?.m_shared;
                if (shared == null || req.m_amount <= 0) continue;
                parts.Add($"{shared.m_name}×{req.m_amount} have {inv.CountItems(shared.m_name)}");
            }
            return string.Join(", ", parts);
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
            _ghostClonePieces.Clear();
        }

        private bool PlaceDonut(Player player, Piece piece, GameObject ghost)
        {
            var center = GetDonutCenter(player, ghost);
            var originalGhostPos = ghost.transform.position;
            var originalGhostRot = ghost.transform.rotation;
            var ghostPlant = ghost.GetComponent<Plant>();
            var playerXZ = new Vector2(player.transform.position.x, player.transform.position.z);
            var maxPlaceDistSq = player.m_maxPlaceDistance * player.m_maxPlaceDistance;
            var needsCultivated = ghostPlant != null && ghostPlant.m_needCultivatedGround;

            int planted = 0, noSpace = 0, tooFar = 0, notCultivated = 0;
            bool outOfResources = false;
            // Cost accounting. Vanilla's UpdatePlacement charges exactly ONE piece (and the
            // build stamina) when TryPlacePiece returns true, so here we charge every plant
            // except the last one and let vanilla pay for that (keeps stamina/skill/recent-
            // piece side effects intact). Before each placement we require enough stock for
            // the still-unpaid previous plant PLUS the new one, so the tally is exact.
            var freeBuild = IsFreeBuild(player, piece);
            var unpaid = 0;
            var affordableBefore = CountAffordable(player, piece);
            var stockBefore = DescribeStock(player, piece);

            _inDonutPlace = true;
            try
            {
                foreach (var p in GridGenerator.Donut(center.x, center.z,
                             _innerRadius.Value, _outerRadius.Value, _spacing.Value))
                {
                    if (!freeBuild && CountAffordable(player, piece) < unpaid + 1)
                    {
                        outOfResources = true;
                        break;
                    }

                    var dx = p.X - playerXZ.x;
                    var dz = p.Z - playerXZ.y;
                    if (dx * dx + dz * dz > maxPlaceDistSq)
                    {
                        tooFar++;
                        continue;
                    }

                    var y = SampleGroundY(p.X, p.Z, originalGhostPos.y);
                    var pointPos = new Vector3(p.X, y, p.Z);

                    if (needsCultivated && !IsCultivatedAt(pointPos))
                    {
                        notCultivated++;
                        continue;
                    }
                    var pos = pointPos;

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
                        if (!freeBuild)
                        {
                            // Pay for the previous plant now; this one stays unpaid until
                            // either the next iteration or vanilla's post-TryPlacePiece charge.
                            if (unpaid > 0) player.ConsumeResources(piece.m_resources, 0);
                            unpaid = 1;
                        }
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
                $"Donut plant: planted={planted} noSpace={noSpace} tooFar={tooFar} notCultivated={notCultivated}" +
                $" | cost: affordableBefore={(affordableBefore == int.MaxValue ? "∞" : affordableBefore.ToString())}" +
                $" stockBefore=[{stockBefore}] stockAfterMod=[{DescribeStock(player, piece)}]" +
                $" paidHere={(freeBuild ? 0 : Mathf.Max(planted - 1, 0))} paidByVanilla={(planted > 0 && !freeBuild ? 1 : 0)}" +
                (freeBuild ? " (free build)" : "") +
                (outOfResources ? " (out of resources)" : ""));
            return planted > 0;
        }

        private static bool IsCultivatedAt(Vector3 pos)
        {
            var hm = Heightmap.FindHeightmap(pos);
            return hm != null && hm.IsCultivated(pos);
        }

        private void OnGUI()
        {
            if (!_showCostOverlay.Value) return;
            if (_totalPointsCount == 0) return;
            if (_activeResources == null && _activeGrownPrefabs == null) return;

            var sb = _overlaySb;
            sb.Length = 0;
            var willPlant = Mathf.Min(_validPointsCount, _affordableCount);
            sb.Append(_activePieceLabel ?? "Plant")
              .Append(" · valid ").Append(_validPointsCount)
              .Append('/').Append(_totalPointsCount);
            if (willPlant < _validPointsCount)
            {
                sb.Append(" · can afford ").Append(willPlant);
            }
            sb.Append("\nDonut · inner ").Append(_innerRadius.Value.ToString("0.##"))
              .Append("m · outer ").Append(_outerRadius.Value.ToString("0.##"))
              .Append("m · spacing ").Append(_spacing.Value.ToString("0.##")).Append('m');

            if (_activeResources != null)
            {
                for (int i = 0; i < _activeResources.Length; i++)
                {
                    var req = _activeResources[i];
                    if (req == null || req.m_resItem == null || req.m_amount <= 0) continue;
                    var shared = req.m_resItem.m_itemData?.m_shared;
                    if (shared == null) continue;
                    sb.Append('\n')
                      .Append("Need: ")
                      .Append(LocalizeOrRaw(shared.m_name))
                      .Append(" × ")
                      .Append(req.m_amount * willPlant);
                    var inv = Player.m_localPlayer != null ? Player.m_localPlayer.GetInventory() : null;
                    if (inv != null)
                    {
                        sb.Append(" (have ").Append(inv.CountItems(shared.m_name)).Append(')');
                    }
                }
            }

            if (_activeGrownPrefabs != null && _activeGrownPrefabs.Length > 0)
            {
                // First grown variant is a fine proxy — vanilla plants all yield the
                // same item across their growth variants. m_amount is the per-plant
                // base yield (Pickable handles bonus rolls elsewhere).
                var grown = _activeGrownPrefabs[0];
                var pickable = grown != null ? grown.GetComponent<Pickable>() : null;
                if (pickable != null && pickable.m_itemPrefab != null)
                {
                    var drop = pickable.m_itemPrefab.GetComponent<ItemDrop>();
                    var shared = drop != null ? drop.m_itemData?.m_shared : null;
                    if (shared != null)
                    {
                        sb.Append('\n')
                          .Append("Yield: ~")
                          .Append(pickable.m_amount * willPlant)
                          .Append(' ')
                          .Append(LocalizeOrRaw(shared.m_name));
                    }
                }
            }

            if (_overlayStyle == null)
            {
                _overlayStyle = new GUIStyle(GUI.skin.box)
                {
                    fontStyle = FontStyle.Bold,
                    fontSize = 14,
                    alignment = TextAnchor.MiddleCenter,
                    wordWrap = false,
                    padding = new RectOffset(12, 12, 8, 8),
                };
                _overlayStyle.normal.textColor = Color.white;
            }

            var text = sb.ToString();
            var content = new GUIContent(text);
            var size = _overlayStyle.CalcSize(content);
            var width = size.x + 8f;
            var height = _overlayStyle.CalcHeight(content, width);
            var x = (Screen.width - width) * 0.5f;
            var y = 10f;
            GUI.Box(new Rect(x, y, width, height), text, _overlayStyle);
        }

        private static string LocalizeOrRaw(string key)
        {
            if (string.IsNullOrEmpty(key)) return string.Empty;
            var loc = Localization.instance;
            return loc != null ? loc.Localize(key) : key;
        }

        private static string TryGetItemDisplayName(GameObject itemPrefab)
        {
            if (itemPrefab == null) return null;
            var drop = itemPrefab.GetComponent<ItemDrop>();
            var shared = drop != null ? drop.m_itemData?.m_shared : null;
            if (shared == null || string.IsNullOrEmpty(shared.m_name)) return itemPrefab.name;
            return LocalizeOrRaw(shared.m_name);
        }

        private void MassInteract(Player player)
        {
            // Scope is decided by the hovered object: mass-interact only affects
            // things of the same kind as the one you're aiming at. Prevents the
            // "I was filling a fire but it also harvested every plant nearby" bug.
            var hover = player.GetHoverObject();
            if (hover == null)
            {
                ShowHudMessage("Shift+E: no target");
                return;
            }

            var radius = _massInteractRadius.Value;
            var hits = Physics.OverlapSphere(player.transform.position, radius);
            var seen = new HashSet<int>();
            int count = 0;
            string label;

            var hoverPickable = hover.GetComponentInParent<Pickable>();
            if (hoverPickable != null)
            {
                // Restrict to the SAME item type as the hovered pickable, so picking
                // a carrot doesn't sweep nearby strawberries / mushrooms / etc.
                var targetItem = hoverPickable.m_itemPrefab;
                var typeName = TryGetItemDisplayName(targetItem);
                label = "picked " + (typeName ?? "items");
                Log.LogInfo($"Shift+E target: pickable '{(targetItem != null ? targetItem.name : "?")}' radius={radius}");
                for (int i = 0; i < hits.Length; i++)
                {
                    var p = hits[i].GetComponentInParent<Pickable>();
                    if (p == null || !seen.Add(p.GetInstanceID())) continue;
                    if (p.m_itemPrefab != targetItem) continue;
                    try { if (p.Interact(player, false, false)) count++; }
                    catch (Exception e) { Log.LogWarning($"MassInteract pickable: {e.Message}"); }
                }
            }
            else if (hover.GetComponentInParent<Fireplace>() is Fireplace fire)
            {
                // Stations are "mass" in the other axis: ONLY the one you aim at, but
                // fed repeatedly until it's full or you run out. (1.0.1 swept every
                // station in the radius, so aiming at the kiln fed the furnace.)
                // alt=true → refill path; plain interact toggles fires that can turn off.
                label = "fueled " + LocalizeOrRaw(fire.m_name);
                count = FeedUntilRefused(() => fire.Interact(player, false, true), "fireplace");
                Log.LogInfo($"Shift+E target: fireplace '{fire.m_name}' fuel={fire.m_fuelItem?.m_itemData?.m_shared?.m_name} added={count}");
            }
            else if (hover.GetComponentInParent<Smelter>() is Smelter smelter)
            {
                label = "fed " + LocalizeOrRaw(smelter.m_name);
                // Aiming at a specific switch (the ore slot vs the fuel slot) feeds just
                // that slot; aiming at the body feeds both.
                var hoveredSwitch = hover.GetComponentInParent<Switch>();
                var oreOnly = hoveredSwitch != null && hoveredSwitch == smelter.m_addOreSwitch;
                var fuelOnly = hoveredSwitch != null && hoveredSwitch == smelter.m_addWoodSwitch;
                int ore = 0, fuel = 0;
                if (smelter.m_addOreSwitch != null && !fuelOnly)
                {
                    var sw = smelter.m_addOreSwitch;
                    ore = FeedUntilRefused(() => sw.Interact(player, false, false), "smelter ore");
                }
                if (smelter.m_addWoodSwitch != null && !oreOnly)
                {
                    var sw = smelter.m_addWoodSwitch;
                    fuel = FeedUntilRefused(() => sw.Interact(player, false, false), "smelter fuel");
                }
                count = ore + fuel;
                Log.LogInfo($"Shift+E target: smelter '{smelter.m_name}' slot={(oreOnly ? "ore" : fuelOnly ? "fuel" : "both")} oreAdded={ore} fuelAdded={fuel} fuel={smelter.m_fuelItem?.m_itemData?.m_shared?.m_name}");
            }
            else
            {
                ShowHudMessage("Shift+E: target not supported");
                return;
            }

            var msg = $"Shift+E: {label} × {count}";
            Log.LogInfo(msg);
            ShowHudMessage(msg);
        }

        /// <summary>Calls <paramref name="feedOnce"/> until it returns false (full / out of
        /// stock / wrong item) or a hard cap, returning how many times it succeeded.</summary>
        private static int FeedUntilRefused(Func<bool> feedOnce, string what)
        {
            const int cap = 64;
            var n = 0;
            try
            {
                while (n < cap && feedOnce()) n++;
            }
            catch (Exception e)
            {
                Log.LogWarning($"MassInteract {what}: {e.Message}");
            }
            return n;
        }

        [HarmonyPatch(typeof(Player), nameof(Player.TryPlacePiece))]
        private static class TryPlacePiecePatch
        {
            [HarmonyPrefix]
            private static bool Prefix(Player __instance, Piece piece, ref bool __result)
            {
                if (_inDonutPlace) return true;
                if (Instance == null) return true;
                if (Instance._geometryMode.Value == GeometryMode.Off) return true;

                var ghost = PlacementGhostRef(__instance);
                if (ghost == null || ghost.GetComponent<Plant>() == null) return true;

                __result = Instance.PlaceDonut(__instance, piece, ghost);
                return false;
            }
        }

        [HarmonyPatch(typeof(Player), "Interact")]
        private static class InteractPatch
        {
            [HarmonyPrefix]
            private static bool Prefix(Player __instance, GameObject go, bool hold, bool alt)
            {
                if (Instance == null || !Instance._massInteractEnabled.Value) return true;
                if (hold || alt) return true;
                if (!Input.GetKey(KeyCode.LeftShift) && !Input.GetKey(KeyCode.RightShift)) return true;
                Instance.MassInteract(__instance);
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
