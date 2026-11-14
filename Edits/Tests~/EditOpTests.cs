using System;
using NUnit.Framework;

namespace MartianMap.Edits.Tests
{
    /// <summary>
    /// 0 is <see cref="EditOp.None"/>, which no edit can carry: a forgotten or failed value never
    /// reads as <c>hazard.set</c> (docs/guide/chapters/tablet.md, "Edits.Apply").
    /// </summary>
    public class EditOpTests
    {
        [Test]
        public void ZeroIsNoneAndTheThreeOpsFollow()
        {
            Assert.That(default(EditOp), Is.EqualTo(EditOp.None));
            Assert.That((int)EditOp.HazardSet, Is.EqualTo(1));
            Assert.That((int)EditOp.ObstacleSet, Is.EqualTo(2));
            Assert.That((int)EditOp.NoteSet, Is.EqualTo(3));
        }

        [Test]
        public void AFailedParseLeavesNone()
        {
            Assert.That(EditOpText.TryParse("hazard.SET", out EditOp op), Is.False);
            Assert.That(op, Is.EqualTo(EditOp.None));
        }

        [Test]
        public void NoneHasNoTextAndNoEditOrEntity()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => EditOpText.ToWire(EditOp.None));
            Assert.Throws<ArgumentOutOfRangeException>(() => new Edit(
                "3f2b8c1e-9a4d-4e7f-8b2a-1c0d9e8f7a6b", 1, "ev1", "ev1", "gale", "a1b2c3d4-e5f6-4a7b-9c8d-0e1f2a3b4c5d",
                EditOp.None, EditPayload.Note(new FixedPoint(0, 0), "x"), false, false));
            Assert.Throws<ArgumentOutOfRangeException>(() => new LiveEntity(
                "a1b2c3d4-e5f6-4a7b-9c8d-0e1f2a3b4c5d", EditOp.None, "ev1", null, EditPayload.Note(new FixedPoint(0, 0), "x")));
        }
    }
}
