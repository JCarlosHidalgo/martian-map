using System;
using MartianMap.Edits;
using MartianMap.Terrain;
using NUnit.Framework;
using UnityEngine;

namespace MartianMap.HUD
{
    public sealed class EditDraftRulesTests
    {
        [TestCase(1.25f, -2.5f, 320, -640)]
        [TestCase(0.001953125f, -0.001953125f, 1, -1)]
        [TestCase(0, 0, 0, 0)]
        public void FixedPointsUseSigned256thsAndRoundMidpointsAwayFromZero(float east, float north, int e, int n)
        {
            Assert.That(EditDraftRules.TryQuantize(new Vector2(east, north), out FixedPoint point), Is.True);
            Assert.That(point.E, Is.EqualTo(e));
            Assert.That(point.N, Is.EqualTo(n));
        }

        [Test]
        public void InvalidOrOverflowingCoordinatesAreRejected()
        {
            foreach (float value in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, float.MaxValue, float.MinValue })
            {
                Assert.That(EditDraftRules.TryQuantize(new Vector2(value, 0), out _), Is.False);
                Assert.That(EditDraftRules.TryQuantize(new Vector2(0, value), out _), Is.False);
            }
        }

        [Test]
        public void SimpleConcaveAndFullIntegerRangeRingsAreAccepted()
        {
            Assert.That(EditDraftRules.IsSimpleRing(new[] { P(0, 0), P(4, 0), P(4, 4), P(2, 2), P(0, 4) }), Is.True);
            Assert.That(EditDraftRules.IsSimpleRing(new[] { P(0, 0), P(2, 0), P(4, 0), P(4, 4), P(0, 4) }), Is.True);
            Assert.That(EditDraftRules.IsSimpleRing(new[] { P(int.MinValue, int.MinValue), P(int.MaxValue, int.MinValue), P(int.MaxValue, int.MaxValue), P(int.MinValue, int.MaxValue) }), Is.True);
        }

        [Test]
        public void RepeatedCrossingTouchingOverlappingAndZeroAreaRingsAreRejected()
        {
            foreach (FixedPoint[] ring in new[]
            {
                Array.Empty<FixedPoint>(), new[] { P(0, 0), P(1, 1) },
                new[] { P(0, 0), P(4, 0), P(0, 4), P(0, 0) },
                new[] { P(0, 0), P(4, 4), P(0, 4), P(4, 0) },
                new[] { P(0, 0), P(4, 0), P(2, 0), P(2, 4), P(0, 4) },
                new[] { P(0, 0), P(4, 0), P(4, 4), P(2, 0), P(0, 4) },
                new[] { P(0, 0), P(1, 1), P(2, 2) },
            })
            {
                Assert.That(EditDraftRules.IsSimpleRing(ring), Is.False);
            }
        }

        [Test]
        public void ForeignHazardsRequireConfirmationOnlyForReductionOrDeletion()
        {
            FixedPoint[] polygon = { P(0, 0), P(256, 0), P(0, 256) };
            var hazard = new LiveEntity("entity", EditOp.HazardSet, "ev2", 255, EditPayload.Hazard(polygon, 255, true));
            var reduce = new EditRequested(EditOp.HazardSet, "entity", EditPayload.Hazard(polygon, 128, null), false);
            var same = new EditRequested(EditOp.HazardSet, "entity", EditPayload.Hazard(polygon, 255, null), false);
            Assert.That(EditDraftRules.RequiresConfirmation(hazard, "ev1", reduce), Is.True);
            Assert.That(EditDraftRules.RequiresConfirmation(hazard, "ev2", reduce), Is.False);
            Assert.That(EditDraftRules.RequiresConfirmation(hazard, "ev1", same), Is.False);
            Assert.That(EditDraftRules.RequiresConfirmation(hazard, "ev1", new EditRequested(same.Op, same.EntityId, same.Payload, true)), Is.True);
            var obstacle = new LiveEntity("obstacle", EditOp.ObstacleSet, "ev2", 255, EditPayload.Obstacle(polygon, true));
            Assert.That(EditDraftRules.RequiresConfirmation(obstacle, "ev1", new EditRequested(obstacle.Op, obstacle.EntityId, obstacle.Payload, true)), Is.True);
            var note = new LiveEntity("note", EditOp.NoteSet, "ev2", null, EditPayload.Note(P(0, 0), "Sample"));
            Assert.That(EditDraftRules.RequiresConfirmation(note, "ev1", new EditRequested(note.Op, note.EntityId, note.Payload, true)), Is.False);
            Assert.Throws<ArgumentException>(() => EditDraftRules.RequiresConfirmation(hazard, "ev1", new EditRequested(note.Op, hazard.EntityId, note.Payload, false)));
            Assert.That(EditDraftRules.CopyPayload(hazard.Payload, null).Confirmed, Is.Null);
        }

        private static FixedPoint P(int east, int north) => new FixedPoint(east, north);
    }
}
