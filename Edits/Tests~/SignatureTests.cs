using System.Collections.Generic;
using NUnit.Framework;

namespace MartianMap.Edits.Tests
{
    /// <summary>
    /// What the calls return is read-only: EntitiesChanged hands the same list to FX and the HUD,
    /// and neither can reorder it for the other (docs/guide/chapters/tablet.md, "Edits.Apply").
    /// </summary>
    public class SignatureTests
    {
        [Test]
        public void LiveEntitiesReturnsAReadOnlyList()
        {
            Assert.That(typeof(EditEngine).GetMethod(nameof(EditEngine.LiveEntities)).ReturnType, Is.EqualTo(typeof(IReadOnlyList<LiveEntity>)));
        }
    }
}
