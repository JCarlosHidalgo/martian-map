using System;
using System.Collections.Generic;
using System.Linq;
using MartianMap.Edits;
using MartianMap.Terrain;
using UnityEngine;

namespace MartianMap.HUD
{
    /// <summary>HUD-owned edit drafts and explicit confirmation. Terrain owns the log and transport.</summary>
    [RequireComponent(typeof(SettingsHud))]
    [DisallowMultipleComponent]
    public sealed partial class EditHud : MonoBehaviour
    {
        private readonly List<FixedPoint> _vertices = new List<FixedPoint>();
        private readonly Dictionary<string, LiveEntity> _entities = new Dictionary<string, LiveEntity>();
        private readonly Dictionary<string, Submitted> _submitted = new Dictionary<string, Submitted>();
        private SettingsHud _settings;
        private ZoneInfo _zone;
        private LiveEntity _selected;
        private EditOp _operation = EditOp.HazardSet;
        private byte _level = 128;
        private string _entityId;
        private EditRequested? _pending;
        private LiveEntity _pendingTarget;
        private EditRequested? _retry;
        private LiveEntity[] _incoming;
        private int _incomingFrame = -1;

        public bool IsEditing { get; private set; }
        public bool AwaitingConfirmation => _pending.HasValue;
        public string Status { get; private set; } = HudStrings.EditReady;
        public IReadOnlyList<FixedPoint> DraftVertices => _vertices.AsReadOnly();
        public string SelectedEntityId => _selected?.EntityId;

        private bool CanOperate => IsEditing && _zone != null && _settings.CanEdit && !_settings.IsOpen;

        private sealed class Submitted
        {
            public EditRequested Request;
            public bool Ambiguous;
        }

        private void Awake()
        {
            _settings = GetComponent<SettingsHud>();
            BuildView();
            RefreshView();
        }

        private void OnEnable()
        {
            UnityEngine.InputSystem.EnhancedTouch.EnhancedTouchSupport.Enable();
            MapEvents.ZoneLoaded += OnZoneLoaded;
            MapEvents.EntitiesChanged += OnEntitiesChanged;
            MapEvents.EditRejected += OnEditRejected;
        }

        private void OnDisable()
        {
            MapEvents.ZoneLoaded -= OnZoneLoaded;
            MapEvents.EntitiesChanged -= OnEntitiesChanged;
            MapEvents.EditRejected -= OnEditRejected;
            UnityEngine.InputSystem.EnhancedTouch.EnhancedTouchSupport.Disable();
            ExitEditMode();
        }

        public bool EnterEditMode()
        {
            if (IsEditing)
            {
                return CanOperate;
            }

            if (_zone == null || _zone.ExtentEnuM == null || _zone.ExtentEnuM.Length != 4 ||
                _zone.ExtentEnuM.Any(value => double.IsNaN(value) || double.IsInfinity(value)) ||
                _zone.ExtentEnuM[0] >= _zone.ExtentEnuM[2] || _zone.ExtentEnuM[1] >= _zone.ExtentEnuM[3])
            {
                SetStatus(HudStrings.EditNoZone);
                return false;
            }

            if (!_settings.CanEdit)
            {
                _settings.Open();
                SetStatus(HudStrings.AuthorRequired);
                return false;
            }

            if (_settings.IsOpen)
            {
                return false;
            }

            IsEditing = true;
            _panel.SetActive(true);
            SetTool(EditOp.HazardSet);
            MapEvents.Emit(new EditModeChanged(true));
            TryReopenConfirmation();
            return true;
        }

        public void ExitEditMode()
        {
            bool wasEditing = IsEditing;
            IsEditing = false;
            _pointerActive = false;
            _pending = null;
            _pendingTarget = null;
            _selected = null;
            _vertices.Clear();
            if (_panel != null)
            {
                _panel.SetActive(false);
                _confirmation.SetActive(false);
                RefreshView();
            }

            if (wasEditing)
            {
                MapEvents.Emit(new EditModeChanged(false));
            }
        }

        public bool SetTool(EditOp operation)
        {
            if (!CanOperate || AwaitingConfirmation || (operation != EditOp.HazardSet &&
                operation != EditOp.ObstacleSet && operation != EditOp.NoteSet))
            {
                return false;
            }

            _operation = operation;
            _entityId = Guid.NewGuid().ToString("D");
            _selected = null;
            _level = 128;
            _vertices.Clear();
            _note.SetTextWithoutNotify(string.Empty);
            SetStatus(HudStrings.EditReady);
            RefreshView();
            return true;
        }

        public bool AddPoint(Vector2 enuM)
        {
            if (!CanOperate || AwaitingConfirmation || !EditDraftRules.TryQuantize(enuM, out FixedPoint point))
            {
                return false;
            }

            double[] bounds = _zone.ExtentEnuM;
            double east = point.E / 256.0;
            double north = point.N / 256.0;
            if (east < bounds[0] || north < bounds[1] || east > bounds[2] || north > bounds[3])
            {
                SetStatus(HudStrings.EditOutsideZone);
                return false;
            }

            if (_operation == EditOp.NoteSet)
            {
                _vertices.Clear();
            }

            if (_vertices.Count >= 128 || _vertices.Any(vertex => vertex.E == point.E && vertex.N == point.N))
            {
                SetStatus(HudStrings.EditDuplicateVertex);
                return false;
            }

            _vertices.Add(point);
            RefreshView();
            return true;
        }

        public void UndoPoint()
        {
            if (CanOperate && !AwaitingConfirmation && _vertices.Count > 0)
            {
                _vertices.RemoveAt(_vertices.Count - 1);
                RefreshView();
            }
        }

        public void Redraw()
        {
            if (CanOperate && !AwaitingConfirmation)
            {
                _vertices.Clear();
                RefreshView();
            }
        }

        public void SetLevel(byte level)
        {
            if (CanOperate && !AwaitingConfirmation && _operation == EditOp.HazardSet && (level == 128 || level == 255))
            {
                _level = level;
                RefreshView();
            }
        }

        public bool SelectEntity(string entityId)
        {
            if (!CanOperate || AwaitingConfirmation || !_entities.TryGetValue(entityId, out LiveEntity entity))
            {
                return false;
            }

            _selected = entity;
            _entityId = entity.EntityId;
            _operation = entity.Op;
            _level = entity.Level ?? 128;
            _vertices.Clear();
            if (entity.Op == EditOp.NoteSet)
            {
                _vertices.Add(entity.Payload.Point.Value);
                _note.SetTextWithoutNotify(entity.Payload.Text);
            }
            else
            {
                _vertices.AddRange(entity.Payload.Polygon);
                _note.SetTextWithoutNotify(string.Empty);
            }

            SetStatus(HudStrings.EditEntitySelected);
            RefreshView();
            return true;
        }

        public bool Submit() => Prepare(false);
        public bool DeleteSelected() => Prepare(true);

        private bool Prepare(bool delete)
        {
            ApplyIncomingEntities();
            if (!CanOperate || AwaitingConfirmation || (delete && _selected == null))
            {
                return false;
            }

            EditPayload payload;
            if (delete)
            {
                payload = EditDraftRules.CopyPayload(_selected.Payload, null);
            }
            else if (_operation == EditOp.NoteSet)
            {
                if (_vertices.Count != 1 || string.IsNullOrWhiteSpace(_note.text))
                {
                    SetStatus(HudStrings.EditNoteRequired);
                    return false;
                }

                payload = EditPayload.Note(_vertices[0], _note.text);
            }
            else
            {
                if (_vertices.Count > 128 || !EditDraftRules.IsSimpleRing(_vertices))
                {
                    SetStatus(HudStrings.EditInvalidPolygon);
                    return false;
                }

                payload = _operation == EditOp.HazardSet ? EditPayload.Hazard(_vertices.ToArray(), _level, null)
                    : EditPayload.Obstacle(_vertices.ToArray(), null);
            }

            var request = new EditRequested(_operation, _entityId, payload, delete);
            if (EditDraftRules.RequiresConfirmation(_selected, _settings.RegisteredAuthor, request))
            {
                ShowConfirmation(request, _selected);
            }
            else
            {
                Send(request);
            }

            return true;
        }

        public bool ConfirmPending()
        {
            ApplyIncomingEntities();
            if (!CanOperate || !_pending.HasValue)
            {
                return false;
            }

            EditRequested request = _pending.Value;
            if (!_entities.TryGetValue(request.EntityId, out LiveEntity current) || _pendingTarget == null ||
                current.Creator != _pendingTarget.Creator || current.Op != _pendingTarget.Op ||
                !EditDraftRules.SamePayload(current.Payload, _pendingTarget.Payload))
            {
                CancelConfirmation();
                SetStatus(HudStrings.EditChanged);
                return false;
            }

            Send(new EditRequested(request.Op, request.EntityId, EditDraftRules.CopyPayload(request.Payload, true), request.Tombstone));
            return true;
        }

        public void CancelConfirmation()
        {
            _pending = null;
            _pendingTarget = null;
            _confirmation.SetActive(false);
            SetStatus(HudStrings.EditConfirmationCancelled);
            RefreshView();
        }

        private void ShowConfirmation(EditRequested request, LiveEntity target)
        {
            _pending = request;
            _pendingTarget = target;
            _selected = target;
            _entityId = request.EntityId;
            _operation = request.Op;
            _level = request.Payload.Level ?? 255;
            _vertices.Clear();
            _vertices.AddRange(request.Payload.Polygon);
            _confirmationText.text = HudFormatting.Format(HudStrings.EditConfirmation,
                ShortId(request.EntityId), target.Creator, target.Level,
                request.Tombstone ? HudStrings.EditDelete : request.Payload.Level?.ToString() ?? HudStrings.EditObstacle);
            _confirmation.SetActive(true);
            SetStatus(HudStrings.EditAwaitingConfirmation);
            RefreshView();
        }

        private void Send(EditRequested request)
        {
            bool ambiguous = _submitted.TryGetValue(request.EntityId, out Submitted previous) &&
                (previous.Ambiguous || previous.Request.Tombstone != request.Tombstone ||
                !EditDraftRules.SamePayload(previous.Request.Payload, request.Payload));
            _submitted[request.EntityId] = new Submitted { Request = request, Ambiguous = ambiguous };
            _pending = null;
            _pendingTarget = null;
            _confirmation.SetActive(false);
            SetTool(_operation);
            SetStatus(HudStrings.EditSent);
            MapEvents.Emit(request);
        }

        private void OnZoneLoaded(ZoneLoaded message)
        {
            ExitEditMode();
            _zone = message.Zone;
            _entities.Clear();
            _submitted.Clear();
            _retry = null;
            if (_incomingFrame != Time.frameCount)
            {
                _incoming = null;
            }

            SyncNotes();
            RefreshView();
        }

        private void OnEntitiesChanged(EntitiesChanged message)
        {
            _incoming = message.Entities.ToArray();
            _incomingFrame = Time.frameCount;
        }

        private void ApplyIncomingEntities()
        {
            if (_incoming == null || _zone == null)
            {
                return;
            }

            _entities.Clear();
            foreach (LiveEntity entity in _incoming)
            {
                _entities[entity.EntityId] = entity;
            }

            _incoming = null;
            if (_pending.HasValue && (!_entities.TryGetValue(_pending.Value.EntityId, out LiveEntity target) ||
                target.Creator != _pendingTarget.Creator || target.Op != _pendingTarget.Op ||
                !EditDraftRules.SamePayload(target.Payload, _pendingTarget.Payload)))
            {
                CancelConfirmation();
                SetStatus(HudStrings.EditChanged);
            }

            if (_selected != null && (!_entities.TryGetValue(_selected.EntityId, out LiveEntity selected) ||
                !EditDraftRules.SamePayload(selected.Payload, _selected.Payload) || selected.Creator != _selected.Creator))
            {
                string entityId = _selected.EntityId;
                SetTool(_operation);
                SelectEntity(entityId);
                SetStatus(HudStrings.EditChanged);
            }

            SyncNotes();
            RefreshView();
            TryReopenConfirmation();
        }

        private void OnEditRejected(EditRejected message)
        {
            if (message.Code == "confirmation_required" && _submitted.TryGetValue(message.EntityId, out Submitted submitted) &&
                !submitted.Ambiguous && submitted.Request.Op != EditOp.NoteSet && !_pending.HasValue)
            {
                _retry = submitted.Request;
                SetStatus(HudStrings.EditReviewRequired);
                RefreshView();
                TryReopenConfirmation();
                return;
            }

            SetStatus(message.Code == "author_missing" ? HudStrings.AuthorRequired
                : message.Code == "confirmation_required" ? HudStrings.EditReviewRequired : HudStrings.EditRejected);
        }

        private void TryReopenConfirmation()
        {
            if (!_retry.HasValue || !CanOperate || _pending.HasValue)
            {
                return;
            }

            EditRequested request = _retry.Value;
            if (_entities.TryGetValue(request.EntityId, out LiveEntity entity) && entity.Op == request.Op)
            {
                _retry = null;
                ShowConfirmation(request, entity);
            }
            else
            {
                SetStatus(HudStrings.EditReviewRequired);
            }
        }

        private void SetStatus(string message)
        {
            Status = message;
            if (_status != null)
            {
                _status.text = message;
            }
        }

        private static string ShortId(string entityId) => entityId.Substring(0, Math.Min(8, entityId.Length));
    }
}
