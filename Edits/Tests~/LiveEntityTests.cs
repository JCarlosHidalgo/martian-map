using System;
using NUnit.Framework;

namespace MartianMap.Edits.Tests
{
    /// <summary>
    /// The checks of the <see cref="LiveEntity"/> constructor: its <c>op</c>, its payload and its
    /// <c>level</c> say the same thing (docs/guide/chapters/tablet.md, "<c>Edits.Apply</c>").
    /// </summary>
    public class LiveEntityTests
    {
        private const string EntityId = "a1b2c3d4-e5f6-4a7b-9c8d-0e1f2a3b4c5d";

        private static EditPayload Hazard128 => EditPayload.Hazard(EditPayloadTests.Triangle, 128, null);

        private static EditPayload Obstacle => EditPayload.Obstacle(EditPayloadTests.Triangle, null);

        private static EditPayload Note => EditPayload.Note(new FixedPoint(1, 2), "x");

        [Test]
        public void AValidLiveEntityOfEachOpIsBuilt()
        {
            Assert.DoesNotThrow(() => new LiveEntity(EntityId, EditOp.HazardSet, "ev1", 128, Hazard128));
            Assert.DoesNotThrow(() => new LiveEntity(EntityId, EditOp.ObstacleSet, "ev1", 255, Obstacle));
            Assert.DoesNotThrow(() => new LiveEntity(EntityId, EditOp.NoteSet, "ev1", null, Note));
        }

        [Test]
        public void AnUndefinedOpThrows()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new LiveEntity(EntityId, (EditOp)7, "ev1", null, Note));
        }

        [Test]
        public void APayloadBuiltForAnotherOpThrows()
        {
            Assert.Throws<ArgumentException>(() => new LiveEntity(EntityId, EditOp.NoteSet, "ev1", null, Hazard128));
            Assert.Throws<ArgumentException>(() => new LiveEntity(EntityId, EditOp.HazardSet, "ev1", 128, Obstacle));
        }

        [Test]
        public void ALevelThatDoesNotMatchTheOpThrows()
        {
            // hazard.set: the payload's level; obstacle.set: 255; note.set: none.
            Assert.Throws<ArgumentException>(() => new LiveEntity(EntityId, EditOp.HazardSet, "ev1", 200, Hazard128));
            Assert.Throws<ArgumentException>(() => new LiveEntity(EntityId, EditOp.HazardSet, "ev1", null, Hazard128));
            Assert.Throws<ArgumentException>(() => new LiveEntity(EntityId, EditOp.ObstacleSet, "ev1", 128, Obstacle));
            Assert.Throws<ArgumentException>(() => new LiveEntity(EntityId, EditOp.ObstacleSet, "ev1", null, Obstacle));
            Assert.Throws<ArgumentException>(() => new LiveEntity(EntityId, EditOp.NoteSet, "ev1", 0, Note));
        }
    }
}
