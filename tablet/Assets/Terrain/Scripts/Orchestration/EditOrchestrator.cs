using System;
using System.Collections.Generic;
using System.Linq;
using MartianMap.Edits;
using UnityEngine;

namespace MartianMap.Terrain
{
    /// <summary>
    /// Edit orchestration (Tablet, "Orquestación"): Terrain is the only caller of Edits.
    /// On <see cref="MapEvents.EditRequested"/> it completes <c>uuid</c>, <c>lamport</c>,
    /// <c>author</c>, <c>creator</c> and <c>zone_id</c>, appends the edit to the zone's local log,
    /// applies the log to the hazard of every z = 0 tile with <c>EditEngine.Apply</c> and emits
    /// <see cref="MapEvents.TileLoaded"/> (which re-plans the route) and
    /// <see cref="MapEvents.EntitiesChanged"/>. The edit waits in <see cref="Pending"/> for
    /// POST /edits (HabitatClient). A <c>rejected</c> ack removes it, re-applies and emits
    /// <see cref="MapEvents.EditRejected"/>. Winners from <c>changes</c> are merged as from the habitat.
    /// Until P3 implements EditEngine, edits are logged and queued but change no hazard.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class EditOrchestrator : MonoBehaviour
    {
        [SerializeField] private TerrainMap2D map;
        [SerializeField] private TerrainBootstrap bootstrap;

        private EditLog _log;
        private bool _warnedNotImplemented;

        /// <summary>Raised when an edit enters the outbox, so the client can send at once.</summary>
        public event Action PendingChanged;

        /// <summary>The active zone's edits not yet acked by the habitat, oldest first.</summary>
        public IReadOnlyList<Edit> Pending => _log != null ? _log.Pending : Array.Empty<Edit>();

        public string ZoneId => _log?.ZoneId;

        private void Awake()
        {
            if (map == null)
            {
                map = GetComponent<TerrainMap2D>();
            }

            if (bootstrap == null)
            {
                bootstrap = GetComponent<TerrainBootstrap>();
            }
        }

        private void OnEnable()
        {
            MapEvents.ZoneLoaded += OnZoneLoaded;
            MapEvents.EditRequested += OnEditRequested;
        }

        private void OnDisable()
        {
            MapEvents.ZoneLoaded -= OnZoneLoaded;
            MapEvents.EditRequested -= OnEditRequested;
        }

        private void OnZoneLoaded(ZoneLoaded e)
        {
            _log = EditLog.Open(ZoneFolder(e.Zone.ZoneId), e.Zone.ZoneId);
            if (_log.Count > 0)
            {
                Debug.Log($"[Terrain] edit log opened zone_id={e.Zone.ZoneId} edits={_log.Count} pending={_log.Pending.Count}");
                Reapply();
            }
            else
            {
                MapEvents.Emit(new EntitiesChanged(Array.Empty<LiveEntity>()));
            }
        }

        private void OnEditRequested(EditRequested request)
        {
            if (_log == null || map.Current == null)
            {
                Debug.LogWarning($"[Terrain] edit ignored entity_id={request.EntityId}: no zone on screen");
                return;
            }

            var author = AppConfig.Author;
            if (string.IsNullOrEmpty(author))
            {
                // The HUD should not allow editing before the author is saved (C5, "Configuración").
                Debug.LogWarning($"[Terrain] edit ignored entity_id={request.EntityId}: this tablet has no author yet");
                MapEvents.Emit(new EditRejected(request.EntityId, "author_missing"));
                return;
            }

            Edit edit;
            try
            {
                edit = new Edit(
                    Guid.NewGuid().ToString("D"),
                    _log.NextLamport,
                    author,
                    _log.CreatorOf(request.EntityId) ?? author,
                    _log.ZoneId,
                    request.EntityId,
                    request.Op,
                    request.Payload,
                    request.Tombstone,
                    false);
            }
            catch (ArgumentException invalid)
            {
                Debug.LogWarning($"[Terrain] edit ignored entity_id={request.EntityId} error={invalid.Message}");
                MapEvents.Emit(new EditRejected(request.EntityId, "invalid_edit"));
                return;
            }

            _log.Append(edit);
            Debug.Log($"[Terrain] edit logged zone_id={edit.ZoneId} uuid={edit.Uuid} op={EditOpText.ToWire(edit.Op)} lamport={edit.Lamport}");
            Reapply();
            PendingChanged?.Invoke();
        }

        /// <summary>Applies the acks of POST /edits for the active zone.</summary>
        public void ApplyAcks(IEnumerable<EditAck> acks)
        {
            if (_log == null)
            {
                return;
            }

            var changed = false;
            foreach (var ack in acks)
            {
                var rejected = _log.ApplyAck(ack.Uuid, ack.Status);
                if (rejected == null)
                {
                    continue;
                }

                changed = true;
                Debug.LogWarning($"[Terrain] edit rejected uuid={ack.Uuid} entity_id={rejected.EntityId} code={ack.Code}");
                MapEvents.Emit(new EditRejected(rejected.EntityId, ack.Code ?? string.Empty));
            }

            if (changed)
            {
                Reapply();
            }
        }

        /// <summary>Merges winners received through <c>changes</c> for a zone.</summary>
        public void ReceiveWinners(string zoneId, IReadOnlyList<Edit> winners)
        {
            if (_log == null || _log.ZoneId != zoneId || winners.Count == 0)
            {
                return;
            }

            if (_log.MergeWinners(winners))
            {
                Debug.Log($"[Terrain] edit winners merged zone_id={zoneId} count={winners.Count}");
                Reapply();
            }
        }

        /// <summary>
        /// Applies the whole log: hazard per z = 0 tile with EditEngine.Apply, then TileLoaded for the
        /// tiles that changed and EntitiesChanged with the live entities.
        /// </summary>
        private void Reapply()
        {
            var zone = map.Current;
            if (zone == null || _log == null)
            {
                return;
            }

            var edits = _log.Edits;
            try
            {
                foreach (var tile in zone.Tiles.Where(t => t.Info.Z == 0))
                {
                    var origin = new EnuMetres((int)Math.Round(tile.OriginEnuM.x), (int)Math.Round(tile.OriginEnuM.y));
                    var hazard = edits.Length == 0 ? null : EditEngine.Apply(tile.BaseHazard, origin, edits);
                    if (tile.SetEditHazard(hazard))
                    {
                        MapEvents.Emit(new TileLoaded(zone.Zone.ZoneId, tile.Info.Z, tile.Info.X, tile.Info.Y));
                    }
                }

                MapEvents.Emit(new EntitiesChanged(EditEngine.LiveEntities(edits)));
            }
            catch (NotImplementedException)
            {
                if (!_warnedNotImplemented)
                {
                    _warnedNotImplemented = true;
                    Debug.LogWarning("[Terrain] Edits.EditEngine is not implemented yet (P3): edits are logged and queued, the hazard does not change");
                }
            }
        }

        private string ZoneFolder(string zoneId)
        {
            var cache = bootstrap != null ? bootstrap.Cache : null;
            return cache != null
                ? cache.ZoneFolder(zoneId)
                : System.IO.Path.Combine(Application.persistentDataPath, "zones", zoneId);
        }
    }

    /// <summary>One ack of POST /edits (C3, <c>edits_ack.schema.json</c>).</summary>
    public readonly struct EditAck
    {
        public EditAck(string uuid, string status, string code)
        {
            Uuid = uuid;
            Status = status;
            Code = code;
        }

        public string Uuid { get; }

        /// <summary><c>accepted</c>, <c>duplicate</c> or <c>rejected</c>.</summary>
        public string Status { get; }

        /// <summary>Set when rejected, e.g. <c>confirmation_required</c>, <c>unknown_zone</c>.</summary>
        public string Code { get; }
    }
}
