using System.Collections.Generic;
using System.Linq;
using MartianMap.Edits;
using MartianMap.Terrain;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;

namespace MartianMap.HUD
{
    public sealed partial class EditHud
    {
        [SerializeField] private TMP_FontAsset _font;

        private readonly List<RaycastResult> _hits = new List<RaycastResult>();
        private readonly Dictionary<string, TMP_Text> _notes = new Dictionary<string, TMP_Text>();
        private GameObject _panel;
        private GameObject _confirmation;
        private TMP_Text _confirmationText;
        private TMP_Text _status;
        private TMP_Text _selection;
        private TMP_Text _launcherLabel;
        private Button _editLauncher;
        private TMP_InputField _note;
        private Button _send;
        private Button _delete;
        private Button _amber;
        private Button _red;
        private EditMapGraphic _graphic;
        private bool _pointerActive;
        private int _pointerId;
        private Vector2 _pressedPosition;
        private float _pressedAt;

        private void Update()
        {
            ApplyIncomingEntities();
            if (IsEditing && (!CanOperate || _settings.IsOpen))
            {
                ExitEditMode();
            }

            if (!CanOperate || AwaitingConfirmation)
            {
                _pointerActive = false;
                return;
            }

            var touches = Touch.activeTouches;
            if (touches.Count > 0)
            {
                if (touches.Count != 1)
                {
                    _pointerActive = false;
                    return;
                }

                var touch = touches[0];
                if (touch.phase == UnityEngine.InputSystem.TouchPhase.Canceled)
                {
                    _pointerActive = false;
                }
                else
                {
                    ProcessEditPointer(touch.touchId, touch.screenPosition,
                        touch.phase == UnityEngine.InputSystem.TouchPhase.Began,
                        touch.phase == UnityEngine.InputSystem.TouchPhase.Ended);
                }

                return;
            }

            Mouse mouse = Mouse.current;
            if (mouse == null || mouse.middleButton.isPressed || mouse.rightButton.isPressed || mouse.scroll.ReadValue().sqrMagnitude > 0)
            {
                _pointerActive = false;
                return;
            }

            ProcessEditPointer(-1, mouse.position.ReadValue(), mouse.leftButton.wasPressedThisFrame, mouse.leftButton.wasReleasedThisFrame);
            if (!mouse.leftButton.isPressed)
            {
                _pointerActive = false;
            }
        }

        private void OnApplicationFocus(bool focused)
        {
            if (!focused)
            {
                _pointerActive = false;
            }
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused)
            {
                _pointerActive = false;
            }
        }

        private void ProcessEditPointer(int id, Vector2 screen, bool began, bool ended)
        {
            if (IsOverEditControls(screen))
            {
                _pointerActive = false;
                return;
            }

            if (began)
            {
                _pointerActive = MapQuery.TryPick(screen, out _);
                _pointerId = id;
                _pressedPosition = screen;
                _pressedAt = Time.unscaledTime;
            }

            float tolerance = 18 * Mathf.Max(1, Screen.dpi / 160f);
            if (id != _pointerId || (screen - _pressedPosition).sqrMagnitude > tolerance * tolerance || Time.unscaledTime - _pressedAt > 0.45f)
            {
                _pointerActive = false;
            }

            if (ended && _pointerActive)
            {
                _pointerActive = false;
                if (MapQuery.TryPick(screen, out Vector2 enu))
                {
                    AddPoint(enu);
                }
            }
        }

        private bool IsOverEditControls(Vector2 position)
        {
            if (MapCamera2D.ScreenBlocker != null && MapCamera2D.ScreenBlocker(position))
            {
                return true;
            }

            if (EventSystem.current == null)
            {
                return false;
            }

            _hits.Clear();
            EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = position }, _hits);
            return _hits.Any(hit => hit.module is GraphicRaycaster);
        }

        private void LateUpdate()
        {
            ApplyIncomingEntities();
            foreach (var pair in _notes)
            {
                bool visible = _entities.TryGetValue(pair.Key, out LiveEntity entity) &&
                    _graphic.Project(entity.Payload.Point.Value, out _);
                pair.Value.gameObject.SetActive(visible);
                if (visible && _graphic.Project(entity.Payload.Point.Value, out Vector2 point))
                {
                    pair.Value.rectTransform.anchoredPosition = point + new Vector2(0, 35);
                }
            }
        }

        private void CycleEntity(int direction)
        {
            string[] ids = _entities.Keys.OrderBy(value => value, System.StringComparer.Ordinal).ToArray();
            if (ids.Length == 0)
            {
                SetStatus(HudStrings.EditNoEntities);
                return;
            }

            int current = System.Array.IndexOf(ids, _selected?.EntityId);
            int next = current < 0 ? (direction > 0 ? 0 : ids.Length - 1) : (current + direction + ids.Length) % ids.Length;
            SelectEntity(ids[next]);
        }

        private void RefreshView()
        {
            if (_graphic == null)
            {
                return;
            }

            string level = _operation == EditOp.HazardSet ? _level.ToString() : _operation == EditOp.ObstacleSet ? "255" : HudStrings.EditNoLevel;
            _selection.text = HudFormatting.Format(HudStrings.EditSelection, ToolName(_operation), level,
                _vertices.Count, _selected == null ? HudStrings.EditNewEntity : ShortId(_selected.EntityId) + " / " + _selected.Creator,
                _entities.Count);
            _launcherLabel.text = _retry.HasValue ? HudStrings.EditMapReview : HudStrings.EditMap;
            _editLauncher.gameObject.SetActive(!IsEditing);
            _note.interactable = _operation == EditOp.NoteSet;
            _send.interactable = CanOperate && !AwaitingConfirmation;
            _delete.interactable = CanOperate && !AwaitingConfirmation && _selected != null;
            _amber.interactable = _red.interactable = CanOperate && !AwaitingConfirmation && _operation == EditOp.HazardSet;
            _graphic.SetGeometry(_entities.Values.ToArray(), IsEditing ? _vertices : System.Array.Empty<FixedPoint>(), _selected?.EntityId);
        }

        private void SyncNotes()
        {
            foreach (TMP_Text note in _notes.Values)
            {
                Destroy(note.gameObject);
            }

            _notes.Clear();
            foreach (LiveEntity entity in _entities.Values.Where(value => value.Op == EditOp.NoteSet))
            {
                TMP_Text text = EditLabel("Entity note " + entity.EntityId, _graphic.transform.parent, entity.Payload.Text, 18, 0);
                text.rectTransform.sizeDelta = new Vector2(260, 70);
                text.alignment = TextAlignmentOptions.Center;
                text.overflowMode = TextOverflowModes.Ellipsis;
                _notes[entity.EntityId] = text;
            }
        }

        private void BuildView()
        {
            RectTransform overlay = EditCanvas("Edit outlines Canvas", 17, false);
            var drawing = new GameObject("Edit outlines", typeof(RectTransform), typeof(EditMapGraphic));
            drawing.transform.SetParent(overlay, false);
            StretchEdit((RectTransform)drawing.transform, 0);
            _graphic = drawing.GetComponent<EditMapGraphic>();
            _graphic.raycastTarget = false;
            RectTransform canvas = EditCanvas("Edit controls Canvas", 45, true);
            _editLauncher = EditButton("Edit map", canvas, HudStrings.EditMap, () => EnterEditMode(), 58, out _launcherLabel);
            var launcherRect = (RectTransform)_editLauncher.transform;
            launcherRect.anchorMin = launcherRect.anchorMax = new Vector2(0.5f, 0);
            launcherRect.anchoredPosition = new Vector2(0, 180);
            launcherRect.sizeDelta = new Vector2(330, 58);
            RectTransform panel = EditPanel("Edit panel", canvas);
            panel.anchorMin = Vector2.zero;
            panel.anchorMax = new Vector2(0, 1);
            panel.offsetMin = new Vector2(20, 20);
            panel.offsetMax = new Vector2(560, -110);
            _panel = panel.gameObject;
            AddEditLayout(panel, 20);
            EditLabel("Edit heading", panel, HudStrings.EditTitle, 28, 38);
            EditLabel("Edit help", panel, HudStrings.EditHelp, 20, 75);
            _status = EditLabel("Edit status", panel, Status, 20, 90);
            _selection = EditLabel("Edit selection", panel, string.Empty, 20, 95);
            RectTransform tools = EditRow("Edit tools", panel);
            EditButton("New hazard", tools, HudStrings.EditHazard, () => SetTool(EditOp.HazardSet), 54, out _);
            EditButton("New obstacle", tools, HudStrings.EditObstacle, () => SetTool(EditOp.ObstacleSet), 54, out _);
            EditButton("New note", tools, HudStrings.EditNote, () => SetTool(EditOp.NoteSet), 54, out _);
            RectTransform levels = EditRow("Hazard levels", panel);
            _amber = EditButton("Amber hazard", levels, HudStrings.EditAmber, () => SetLevel(128), 54, out _);
            _red = EditButton("Red hazard", levels, HudStrings.EditRed, () => SetLevel(255), 54, out _);
            _note = EditInput(panel);
            RectTransform geometry = EditRow("Edit geometry", panel);
            EditButton("Undo vertex", geometry, HudStrings.EditUndo, UndoPoint, 54, out _);
            EditButton("Redraw entity", geometry, HudStrings.EditRedraw, Redraw, 54, out _);
            RectTransform entities = EditRow("Browse entities", panel);
            EditButton("Previous entity", entities, HudStrings.EditPrevious, () => CycleEntity(-1), 54, out _);
            EditButton("Next entity", entities, HudStrings.EditNext, () => CycleEntity(1), 54, out _);
            RectTransform actions = EditRow("Edit actions", panel);
            _send = EditButton("Send edit", actions, HudStrings.EditSend, () => Submit(), 54, out _);
            _delete = EditButton("Delete entity", actions, HudStrings.EditDelete, () => DeleteSelected(), 54, out _);
            EditButton("Exit editing", panel, HudStrings.EditExit, ExitEditMode, 54, out _);
            _panel.SetActive(false);

            RectTransform shade = EditPanel("Edit confirmation modal", canvas);
            StretchEdit(shade, 0);
            shade.GetComponent<Image>().color = new Color(0, 0, 0, 0.8f);
            _confirmation = shade.gameObject;
            RectTransform card = EditPanel("Edit confirmation card", shade);
            card.anchorMin = card.anchorMax = new Vector2(0.5f, 0.5f);
            card.sizeDelta = new Vector2(800, 440);
            AddEditLayout(card, 28);
            EditLabel("Confirmation title", card, HudStrings.EditConfirmTitle, 27, 42);
            _confirmationText = EditLabel("Confirmation details", card, string.Empty, 22, 170);
            EditButton("Confirm shared edit", card, HudStrings.EditConfirm, () => ConfirmPending(), 58, out _);
            EditButton("Cancel shared edit", card, HudStrings.EditCancel, CancelConfirmation, 58, out _);
            _confirmation.SetActive(false);
        }

        private RectTransform EditCanvas(string name, int order, bool raycaster)
        {
            var root = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            root.transform.SetParent(transform, false);
            root.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            root.GetComponent<Canvas>().sortingOrder = order;
            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            if (raycaster)
            {
                root.AddComponent<GraphicRaycaster>();
            }

            return (RectTransform)root.transform;
        }

        private static RectTransform EditPanel(string name, Transform parent)
        {
            var root = new GameObject(name, typeof(RectTransform), typeof(Image));
            root.transform.SetParent(parent, false);
            root.GetComponent<Image>().color = new Color(0.045f, 0.065f, 0.085f);
            return (RectTransform)root.transform;
        }

        private static void AddEditLayout(RectTransform panel, int padding)
        {
            var layout = panel.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(padding, padding, padding, padding);
            layout.spacing = 10;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
        }

        private static RectTransform EditRow(string name, Transform parent)
        {
            var root = new GameObject(name, typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            root.transform.SetParent(parent, false);
            root.GetComponent<LayoutElement>().preferredHeight = 54;
            var layout = root.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = 8;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            return (RectTransform)root.transform;
        }

        private Button EditButton(string name, Transform parent, string caption, UnityEngine.Events.UnityAction action, float height, out TMP_Text text)
        {
            var root = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            root.transform.SetParent(parent, false);
            root.GetComponent<LayoutElement>().preferredHeight = height;
            root.GetComponent<Image>().color = new Color(0.25f, 0.35f, 0.4f);
            var button = root.GetComponent<Button>();
            button.targetGraphic = root.GetComponent<Image>();
            button.onClick.AddListener(action);
            text = EditLabel("Label", root.transform, caption, 19, 0);
            text.alignment = TextAlignmentOptions.Center;
            StretchEdit(text.rectTransform, 6);
            return button;
        }

        private TMP_InputField EditInput(Transform parent)
        {
            var root = new GameObject("Edit note", typeof(RectTransform), typeof(Image), typeof(TMP_InputField), typeof(LayoutElement));
            root.transform.SetParent(parent, false);
            root.GetComponent<LayoutElement>().preferredHeight = 85;
            root.GetComponent<Image>().color = new Color(0.12f, 0.16f, 0.2f);
            var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
            viewport.transform.SetParent(root.transform, false);
            StretchEdit((RectTransform)viewport.transform, 8);
            TMP_Text text = EditLabel("Text", viewport.transform, string.Empty, 21, 0);
            StretchEdit(text.rectTransform, 0);
            TMP_Text placeholder = EditLabel("Placeholder", viewport.transform, HudStrings.EditNotePlaceholder, 21, 0);
            StretchEdit(placeholder.rectTransform, 0);
            var input = root.GetComponent<TMP_InputField>();
            input.textViewport = (RectTransform)viewport.transform;
            input.textComponent = text;
            input.placeholder = placeholder;
            input.targetGraphic = root.GetComponent<Image>();
            input.richText = false;
            input.lineType = TMP_InputField.LineType.MultiLineNewline;
            return input;
        }

        private TMP_Text EditLabel(string name, Transform parent, string caption, int size, float height)
        {
            var root = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            root.transform.SetParent(parent, false);
            var text = root.GetComponent<TextMeshProUGUI>();
            text.font = _font != null ? _font : TMP_Settings.defaultFontAsset;
            text.fontSize = size;
            text.color = new Color(0.91f, 0.94f, 0.96f);
            text.richText = false;
            text.raycastTarget = false;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.text = caption;
            if (height > 0)
            {
                root.AddComponent<LayoutElement>().preferredHeight = height;
            }

            return text;
        }

        private static void StretchEdit(RectTransform rect, float inset)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(inset, inset);
            rect.offsetMax = new Vector2(-inset, -inset);
        }

        private static string ToolName(EditOp operation) => operation == EditOp.HazardSet ? HudStrings.EditHazard
            : operation == EditOp.ObstacleSet ? HudStrings.EditObstacle : HudStrings.EditNote;
    }
}
