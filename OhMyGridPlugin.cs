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
        public const string PluginVersion = "1.0.0";

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

            while (_ghostClones.Count < points.Count)
            {
                // NOTE: Plant.Awake() throws NRE on the clone because the placement
                // ghost has no ZNetView, but Awake unconditionally calls m_nview.GetZDO().
                // The exception is benign — the visual still renders, the serialized
                // m_growRadius/m_spaceMask are intact, and Piece.SetInvalidPlacementHeightlight
                // works fine. We disable Plant on the clone to stop further per-frame
                // Plant logic from messing with the visual.
                var clone = UnityEngine.Object.Instantiate(ghost);
                foreach (var c in clone.GetComponentsInChildren<Collider>())
                {
                    c.enabled = false;
                }
                var clonePlant = clone.GetComponent<Plant>();
                if (clonePlant != null) clonePlant.enabled = false;
                _ghostClones.Add(clone);
                _ghostClonePieces.Add(clone.GetComponent<Piece>());
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
                if (!invalid) valid++;
                var clonePiece = _ghostClonePieces[i];
                if (clonePiece != null) clonePiece.SetInvalidPlacementHeightlight(invalid);
            }

            ghost.transform.position = originalSourcePos;
            ghost.transform.rotation = originalSourceRot;
            _validPointsCount = valid;
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
            sb.Append(_activePieceLabel ?? "Plant")
              .Append(" · valid ").Append(_validPointsCount)
              .Append('/').Append(_totalPointsCount);
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
                      .Append(req.m_amount * _validPointsCount);
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
                          .Append(pickable.m_amount * _validPointsCount)
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
                for (int i = 0; i < hits.Length; i++)
                {
                    var p = hits[i].GetComponentInParent<Pickable>();
                    if (p == null || !seen.Add(p.GetInstanceID())) continue;
                    if (p.m_itemPrefab != targetItem) continue;
                    try { if (p.Interact(player, false, false)) count++; }
                    catch (Exception e) { Log.LogWarning($"MassInteract pickable: {e.Message}"); }
                }
            }
            else if (hover.GetComponentInParent<Fireplace>() != null)
            {
                label = "fueled";
                for (int i = 0; i < hits.Length; i++)
                {
                    var f = hits[i].GetComponentInParent<Fireplace>();
                    if (f == null || !seen.Add(f.GetInstanceID())) continue;
                    try { if (f.Interact(player, false, false)) count++; }
                    catch (Exception e) { Log.LogWarning($"MassInteract fireplace: {e.Message}"); }
                }
            }
            else if (hover.GetComponentInParent<Smelter>() != null)
            {
                label = "smelters";
                for (int i = 0; i < hits.Length; i++)
                {
                    var s = hits[i].GetComponentInParent<Smelter>();
                    if (s == null || !seen.Add(s.GetInstanceID())) continue;
                    try
                    {
                        var any = false;
                        if (s.m_addOreSwitch != null && s.m_addOreSwitch.Interact(player, false, false)) any = true;
                        if (s.m_addWoodSwitch != null && s.m_addWoodSwitch.Interact(player, false, false)) any = true;
                        if (any) count++;
                    }
                    catch (Exception e) { Log.LogWarning($"MassInteract smelter: {e.Message}"); }
                }
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
