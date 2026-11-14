using System;
using NUnit.Framework;

namespace MartianMap.Edits.Tests
{
    /// <summary>The table between <see cref="EditOp"/> and the text that travels (docs/guide/chapters/convenciones.md, "Catálogo de op").</summary>
    public class EditOpTextTests
    {
        [TestCase(EditOp.HazardSet, "hazard.set")]
        [TestCase(EditOp.ObstacleSet, "obstacle.set")]
        [TestCase(EditOp.NoteSet, "note.set")]
        public void EachOpGoesToItsTextAndBack(EditOp op, string wire)
        {
            Assert.That(EditOpText.ToWire(op), Is.EqualTo(wire));
            Assert.That(EditOpText.TryParse(wire, out EditOp back), Is.True);
            Assert.That(back, Is.EqualTo(op));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("Hazard.Set")]
        [TestCase("hazard.set ")]
        [TestCase("hazard")]
        [TestCase("HazardSet")]
        [TestCase("0")]
        [TestCase("note.delete")]
        public void AnUnknownTextIsRejectedWithoutThrowing(string wire)
        {
            Assert.That(EditOpText.TryParse(wire, out _), Is.False);
        }

        [Test]
        public void AnUndefinedOpHasNoText()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => EditOpText.ToWire((EditOp)7));
        }
    }
}
