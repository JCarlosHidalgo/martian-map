using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace MartianMap.Edits.Tests
{
    /// <summary>The three factories of <see cref="EditPayload"/> (docs/guide/chapters/tablet.md, "apply() (D5)").</summary>
    public class EditPayloadTests
    {
        internal static readonly FixedPoint[] Triangle = { new FixedPoint(0, 0), new FixedPoint(256, 0), new FixedPoint(0, 256) };

        [Test]
        public void HazardFillsPolygonLevelAndConfirmedOnly()
        {
            EditPayload p = EditPayload.Hazard(Triangle, 7, false);
            Assert.That(p.Op, Is.EqualTo(EditOp.HazardSet));
            Assert.That(p.Polygon, Is.EqualTo(Triangle));
            Assert.That(p.Level, Is.EqualTo(7));
            Assert.That(p.Confirmed, Is.False);
            Assert.That(p.Point, Is.Null);
            Assert.That(p.Text, Is.Null);
        }

        [Test]
        public void ObstacleHasNoLevel()
        {
            EditPayload p = EditPayload.Obstacle(Triangle, null);
            Assert.That(p.Op, Is.EqualTo(EditOp.ObstacleSet));
            Assert.That(p.Level, Is.Null);
            Assert.That(p.Confirmed, Is.Null);
            Assert.That(p.Point, Is.Null);
            Assert.That(p.Text, Is.Null);
        }

        [Test]
        public void NoteHasNoPolygonLevelOrConfirmed()
        {
            EditPayload p = EditPayload.Note(new FixedPoint(1, 2), "");
            Assert.That(p.Op, Is.EqualTo(EditOp.NoteSet));
            Assert.That(p.Point, Is.EqualTo(new FixedPoint(1, 2)));
            Assert.That(p.Text, Is.EqualTo(""));
            Assert.That(p.Polygon, Is.Null);
            Assert.That(p.Level, Is.Null);
            Assert.That(p.Confirmed, Is.Null);
        }

        [Test]
        public void ThePolygonIsACopyThatCannotBeChanged()
        {
            var ring = (FixedPoint[])Triangle.Clone();
            EditPayload p = EditPayload.Hazard(ring, 1, null);
            ring[0] = new FixedPoint(9, 9);
            Assert.That(p.Polygon[0], Is.EqualTo(new FixedPoint(0, 0)));
            Assert.That(p.Polygon, Is.Not.InstanceOf<FixedPoint[]>());
            Assert.That(((IList<FixedPoint>)p.Polygon).IsReadOnly, Is.True);
        }

        [Test]
        public void ABadPolygonThrows()
        {
            Assert.Throws<ArgumentNullException>(() => EditPayload.Hazard(null, 1, null));
            Assert.Throws<ArgumentException>(() => EditPayload.Obstacle(new[] { Triangle[0], Triangle[1] }, null));
            Assert.Throws<ArgumentException>(() => EditPayload.Hazard(new[] { Triangle[0], Triangle[1], Triangle[2], Triangle[0] }, 1, null));
        }

        [Test]
        public void ANullNoteTextThrows()
        {
            Assert.Throws<ArgumentNullException>(() => EditPayload.Note(new FixedPoint(0, 0), null));
        }
    }
}
