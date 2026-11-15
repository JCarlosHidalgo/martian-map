using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MartianMap.Edits;
using MartianMap.Terrain;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;

namespace MartianMap.HUD
{
    public sealed class EditHudTests
    {
        private const string HazardId = "11111111-1111-4111-8111-111111111111";
        private static readonly JArray ExportedRequests = new JArray();
        private static readonly string ExportPath = Path.Combine(Path.GetTempPath(), "martian-map-hud-edit-" + Guid.NewGuid().ToString("N") + ".json");
        private GameObject _root;
        private EditHud _hud;
        private SettingsHud _settings;
        private readonly List<EditRequested> _requests = new List<EditRequested>();

        private sealed class Configuration : IHudConfiguration
        {
            public string HabitatUrl => string.Empty;
            public string FilesUrl => string.Empty;
            public string Author { get; set; } = "ev1";
            public void Save(string habitatUrl, string filesUrl, string author) => Author = author;
        }

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _requests.Clear();
            _root = new GameObject("Edit HUD test");
            _root.SetActive(false);
            _settings = _root.AddComponent<SettingsHud>();
            _settings.UseConfiguration(new Configuration());
            _hud = _root.AddComponent<EditHud>();
            MapEvents.EditRequested += Record;
            _root.SetActive(true);
            yield return null;
            MapEvents.Emit(new ZoneLoaded(MissionDefinitionTests.Zone(), string.Empty, "ev1"));
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            MapEvents.EditRequested -= Record;
            UnityEngine.Object.Destroy(_root);
            yield return null;
        }

        private void Record(EditRequested request)
        {
            _requests.Add(request);
            var edit = new Edit(Guid.NewGuid().ToString("D"), 1, "ev1", "ev1", "synth", request.EntityId,
                request.Op, request.Payload, request.Tombstone, false);
            ExportedRequests.Add(EditJson.Write(edit));
#if UNITY_EDITOR
            File.WriteAllText(ExportPath, ExportedRequests.ToString() + "\n", new System.Text.UTF8Encoding(false));
            if (ExportedRequests.Count == 1)
            {
                Debug.Log("[HUD] edit contract examples path=" + ExportPath);
            }
#endif
        }

        private static FixedPoint[] Ring(int offset = 0) => new[]
        {
            new FixedPoint(offset, 0), new FixedPoint(2560 + offset, 0), new FixedPoint(offset, 2560),
        };

        private static LiveEntity Hazard(byte level = 255, int offset = 0, string creator = "ev2") =>
            new LiveEntity(HazardId, EditOp.HazardSet, creator, level, EditPayload.Hazard(Ring(offset), level, true));

        private void AddTriangle()
        {
            Assert.That(_hud.AddPoint(new Vector2(0, 0)), Is.True);
            Assert.That(_hud.AddPoint(new Vector2(10, 0)), Is.True);
            Assert.That(_hud.AddPoint(new Vector2(0, 10)), Is.True);
        }

        [UnityTest]
        public IEnumerator RegistrationAndZoneChangesGateEveryEdit()
        {
            _settings.UseConfiguration(new Configuration { Author = string.Empty });
            Assert.That(_hud.EnterEditMode(), Is.False);
            Assert.That(_hud.AddPoint(Vector2.zero), Is.False);
            Assert.That(_hud.Submit(), Is.False);
            Assert.That(_settings.IsOpen, Is.True);
            _settings.UseConfiguration(new Configuration());
            _settings.Close();
            Assert.That(_hud.EnterEditMode(), Is.True);
            AddTriangle();
            MapEvents.Emit(new ZoneLoaded(MissionDefinitionTests.Zone(), string.Empty, "ev1"));
            Assert.That(_hud.IsEditing, Is.False);
            Assert.That(_hud.DraftVertices, Is.Empty);
            Assert.That(_hud.Submit(), Is.False);
            Assert.That(_requests, Is.Empty);
            yield return null;
        }

        [UnityTest]
        public IEnumerator CreatesAllThreeTypedPayloadsWithFreshEntityIds()
        {
            Assert.That(_hud.EnterEditMode(), Is.True);
            AddTriangle();
            _hud.SetLevel(255);
            Assert.That(_hud.Submit(), Is.True);
            Assert.That(_requests[0].Op, Is.EqualTo(EditOp.HazardSet));
            Assert.That(_requests[0].Payload.Level, Is.EqualTo(255));
            Assert.That(_requests[0].Payload.Polygon[1].E, Is.EqualTo(2560));
            Assert.That(_requests[0].Payload.Confirmed, Is.Null);
            _hud.SetTool(EditOp.ObstacleSet);
            AddTriangle();
            Assert.That(_hud.Submit(), Is.True);
            Assert.That(_requests[1].Payload.Level, Is.Null);
            _hud.SetTool(EditOp.NoteSet);
            Assert.That(_hud.AddPoint(new Vector2(0.5f, -1.25f)), Is.True);
            _root.GetComponentsInChildren<TMP_InputField>(true).Single(field => field.name == "Edit note").text = "<b>Rock sample</b>";
            Assert.That(_hud.Submit(), Is.True);
            Assert.That(_requests[2].Payload.Point.Value.E, Is.EqualTo(128));
            Assert.That(_requests[2].Payload.Point.Value.N, Is.EqualTo(-320));
            Assert.That(_requests[2].Payload.Text, Is.EqualTo("<b>Rock sample</b>"));
            Assert.That(_requests.Select(request => request.EntityId).Distinct().Count(), Is.EqualTo(3));
            Assert.That(_requests.All(request => Guid.Parse(request.EntityId).ToString("D") == request.EntityId), Is.True);
            yield return null;
        }

        [UnityTest]
        public IEnumerator InvalidGeometryDuplicateVerticesAndEmptyNotesAreNotSent()
        {
            _hud.EnterEditMode();
            _hud.AddPoint(Vector2.zero);
            Assert.That(_hud.AddPoint(Vector2.zero), Is.False);
            Assert.That(_hud.AddPoint(new Vector2(float.NaN, 0)), Is.False);
            Assert.That(_hud.AddPoint(new Vector2(200, 0)), Is.False);
            Assert.That(_hud.Submit(), Is.False);
            _hud.Redraw();
            foreach (Vector2 point in new[] { Vector2.zero, new Vector2(10, 10), new Vector2(0, 10), new Vector2(10, 0) })
            {
                _hud.AddPoint(point);
            }

            Assert.That(_hud.Submit(), Is.False);
            _hud.UndoPoint();
            Assert.That(_hud.DraftVertices.Count, Is.EqualTo(3));
            _hud.SetTool(EditOp.NoteSet);
            _hud.AddPoint(Vector2.zero);
            Assert.That(_hud.Submit(), Is.False);
            Assert.That(_requests, Is.Empty);
            yield return null;
        }

        [UnityTest]
        public IEnumerator ForeignReductionWaitsForExplicitConfirmationAndNeverInheritsIt()
        {
            _hud.EnterEditMode();
            MapEvents.Emit(new EntitiesChanged(new[] { Hazard() }));
            yield return null;
            Assert.That(_hud.SelectEntity(HazardId), Is.True);
            _hud.SetLevel(128);
            Assert.That(_hud.Submit(), Is.True);
            Assert.That(_hud.AwaitingConfirmation, Is.True);
            Assert.That(_requests, Is.Empty);
            _hud.CancelConfirmation();
            Assert.That(_requests, Is.Empty);
            _hud.Submit();
            Assert.That(_hud.ConfirmPending(), Is.True);
            Assert.That(_requests.Count, Is.EqualTo(1));
            Assert.That(_requests[0].EntityId, Is.EqualTo(HazardId));
            Assert.That(_requests[0].Payload.Level, Is.EqualTo(128));
            Assert.That(_requests[0].Payload.Confirmed, Is.True);
        }

        [UnityTest]
        public IEnumerator TombstonesKeepTheLastPayloadAndOperationIncludingUnsavedDraftChanges()
        {
            _hud.EnterEditMode();
            var entities = new[]
            {
                Hazard(),
                new LiveEntity("22222222-2222-4222-8222-222222222222", EditOp.ObstacleSet, "ev2", 255, EditPayload.Obstacle(Ring(), true)),
                new LiveEntity("33333333-3333-4333-8333-333333333333", EditOp.NoteSet, "ev2", null, EditPayload.Note(new FixedPoint(0, 0), "Original note")),
            };
            MapEvents.Emit(new EntitiesChanged(entities));
            yield return null;
            foreach (LiveEntity entity in entities)
            {
                _hud.SelectEntity(entity.EntityId);
                _hud.Redraw();
                _hud.SetLevel(128);
                _root.GetComponentsInChildren<TMP_InputField>(true).Single(field => field.name == "Edit note").text = "Unsaved text";
                int count = _requests.Count;
                Assert.That(_hud.DeleteSelected(), Is.True);
                if (entity.Op != EditOp.NoteSet)
                {
                    Assert.That(_requests.Count, Is.EqualTo(count));
                    Assert.That(_hud.ConfirmPending(), Is.True);
                }

                EditRequested request = _requests.Last();
                Assert.That(request.Tombstone, Is.True);
                Assert.That(request.Op, Is.EqualTo(entity.Op));
                Assert.That(EditDraftRules.SamePayload(request.Payload, entity.Payload), Is.True);
                Assert.That(request.Payload.Confirmed, Is.EqualTo(entity.Op == EditOp.NoteSet ? (bool?)null : true));
            }
        }

        [UnityTest]
        public IEnumerator ConcurrentEntityChangesInvalidateAnOpenConfirmationBeforeItCanSend()
        {
            _hud.EnterEditMode();
            MapEvents.Emit(new EntitiesChanged(new[] { Hazard() }));
            yield return null;
            _hud.SelectEntity(HazardId);
            _hud.SetLevel(128);
            _hud.Submit();
            MapEvents.Emit(new EntitiesChanged(new[] { Hazard(offset: 128) }));
            Assert.That(_hud.ConfirmPending(), Is.False);
            Assert.That(_hud.AwaitingConfirmation, Is.False);
            Assert.That(_hud.Status, Is.EqualTo(HudStrings.EditChanged));
            Assert.That(_requests, Is.Empty);
        }

        [UnityTest]
        public IEnumerator SynchronousConfirmationRejectionReopensTheExactRequest()
        {
            _hud.EnterEditMode();
            MapEvents.Emit(new EntitiesChanged(new[] { Hazard(128) }));
            yield return null;
            _hud.SelectEntity(HazardId);
            _hud.Redraw();
            _hud.AddPoint(new Vector2(-10, -10));
            _hud.AddPoint(new Vector2(20, -10));
            _hud.AddPoint(new Vector2(-10, 20));
            _hud.SetLevel(255);
            bool reject = true;
            Action<EditRequested> response = request =>
            {
                if (reject)
                {
                    reject = false;
                    MapEvents.Emit(new EditRejected(request.EntityId, "confirmation_required"));
                }
            };
            MapEvents.EditRequested += response;
            try
            {
                _hud.Submit();
                Assert.That(_hud.AwaitingConfirmation, Is.True);
                Assert.That(_hud.DraftVertices[0].E, Is.EqualTo(-2560));
                Assert.That(_requests.Count, Is.EqualTo(1));
                Assert.That(_hud.ConfirmPending(), Is.True);
                Assert.That(_requests.Count, Is.EqualTo(2));
                Assert.That(_requests[1].Payload.Level, Is.EqualTo(255));
                Assert.That(_requests[1].Payload.Confirmed, Is.True);
            }
            finally
            {
                MapEvents.EditRequested -= response;
            }
        }

        [UnityTest]
        public IEnumerator AmbiguousRejectionsNeverConfirmAnUnidentifiedRequest()
        {
            _hud.EnterEditMode();
            MapEvents.Emit(new EntitiesChanged(new[] { Hazard(128) }));
            yield return null;
            _hud.SelectEntity(HazardId);
            _hud.Submit();
            _hud.SelectEntity(HazardId);
            _hud.SetLevel(255);
            _hud.Submit();
            MapEvents.Emit(new EditRejected(HazardId, "confirmation_required"));
            Assert.That(_hud.AwaitingConfirmation, Is.False);
            Assert.That(_hud.ConfirmPending(), Is.False);
            Assert.That(_hud.Status, Is.EqualTo(HudStrings.EditReviewRequired));
            Assert.That(_requests.Count, Is.EqualTo(2));
        }

        [UnityTest]
        public IEnumerator NotesAndOutlinesAreRenderedWithoutChangingTheHazardOrInterpretingMarkup()
        {
            Camera previous = MapQuery.Camera;
            var camera = new GameObject("Edit graphic test camera", typeof(Camera)).GetComponent<Camera>();
            camera.transform.SetParent(_root.transform);
            camera.transform.position = new Vector3(0, 1000, 0);
            camera.transform.rotation = Quaternion.Euler(90, 0, 0);
            camera.orthographic = true;
            camera.orthographicSize = 128;
            MapQuery.Camera = camera;
            try
            {
                MapEvents.Emit(new EntitiesChanged(new[]
                {
                    Hazard(), new LiveEntity("note", EditOp.NoteSet, "ev2", null, EditPayload.Note(new FixedPoint(0, 0), "<b>Literal note</b>")),
                }));
                yield return null;
                Canvas.ForceUpdateCanvases();
                var graphic = _root.GetComponentInChildren<EditMapGraphic>();
                Assert.That(graphic.canvasRenderer.GetMesh().vertexCount, Is.GreaterThanOrEqualTo(20));
                TMP_Text label = _root.GetComponentsInChildren<TMP_Text>().Single(text => text.name == "Entity note note");
                Assert.That(label.richText, Is.False);
                Assert.That(label.text, Is.EqualTo("<b>Literal note</b>"));
                Assert.That(_requests, Is.Empty);
                _hud.EnterEditMode();
                Canvas.ForceUpdateCanvases();
                foreach (TMP_Text text in _root.GetComponentsInChildren<TMP_Text>().Where(value => !value.name.StartsWith("Entity note", StringComparison.Ordinal)))
                {
                    text.ForceMeshUpdate();
                    Assert.That(text.isTextOverflowing, Is.False, text.name);
                }
            }
            finally
            {
                MapQuery.Camera = previous;
            }
        }
    }
}
