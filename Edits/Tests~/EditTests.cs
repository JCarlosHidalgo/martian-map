using System;
using NUnit.Framework;

namespace MartianMap.Edits.Tests
{
    /// <summary>The checks of the <see cref="Edit"/> constructor (docs/guide/chapters/convenciones.md, "Identificadores").</summary>
    public class EditTests
    {
        private const string Uuid = "3f2b8c1e-9a4d-4e7f-8b2a-1c0d9e8f7a6b";
        private const string EntityId = "a1b2c3d4-e5f6-4a7b-9c8d-0e1f2a3b4c5d";

        [Test]
        public void AValidEditOfEachOpIsBuilt()
        {
            Assert.DoesNotThrow(() => Make());
            Assert.DoesNotThrow(() => Make(op: EditOp.ObstacleSet, payload: EditPayload.Obstacle(EditPayloadTests.Triangle, true)));
            Assert.DoesNotThrow(() => Make(op: EditOp.NoteSet, payload: EditPayload.Note(new FixedPoint(1, 2), "")));
        }

        [Test]
        public void APayloadBuiltForAnotherOpThrows()
        {
            Assert.Throws<ArgumentException>(() => Make(op: EditOp.ObstacleSet));
            Assert.Throws<ArgumentException>(() => Make(payload: EditPayload.Note(new FixedPoint(0, 0), "x")));
            Assert.Throws<ArgumentNullException>(() => new Edit(Uuid, 1, "ev1", "ev1", "gale", EntityId, EditOp.HazardSet, null, false, false));
        }

        [Test]
        public void AnUndefinedOpThrows()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => Make(op: (EditOp)7));
        }

        [TestCase("3F2B8C1E-9A4D-4E7F-8B2A-1C0D9E8F7A6B")]
        [TestCase("3f2b8c1e-9a4d-1e7f-8b2a-1c0d9e8f7a6b")]
        [TestCase("3f2b8c1e-9a4d-4e7f-cb2a-1c0d9e8f7a6b")]
        [TestCase("3f2b8c1e9a4d4e7f8b2a1c0d9e8f7a6b")]
        [TestCase("zone-1")]
        public void UuidAndEntityIdMustBeLowercaseUuidV4(string bad)
        {
            Assert.Throws<ArgumentException>(() => Make(uuid: bad));
            Assert.Throws<ArgumentException>(() => Make(entityId: bad));
        }

        [Test]
        public void ANullUuidThrows()
        {
            Assert.Throws<ArgumentNullException>(() => Make(uuid: null));
        }

        [Test]
        public void TheLamportClockStartsAtOne()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => Make(lamport: 0));
            Assert.DoesNotThrow(() => Make(lamport: 1));
        }

        [TestCase("e")]
        [TestCase("aaaaaaaaaaaaaaaaa")]
        [TestCase("EV1")]
        [TestCase("ev_1")]
        public void AuthorAndCreatorFollowTheSlug(string bad)
        {
            Assert.Throws<ArgumentException>(() => Make(author: bad));
            Assert.Throws<ArgumentException>(() => Make(creator: bad));
        }

        [Test]
        public void AnAuthorOfSixteenCharactersIsValid()
        {
            Assert.DoesNotThrow(() => Make(author: "aaaaaaaaaaaaaaaa"));
        }

        [TestCase("1gale")]
        [TestCase("g")]
        [TestCase("gaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
        public void ZoneIdFollowsTheSlug(string bad)
        {
            Assert.Throws<ArgumentException>(() => Make(zoneId: bad));
        }

        [Test]
        public void AZoneIdOfThirtyTwoCharactersIsValid()
        {
            Assert.DoesNotThrow(() => Make(zoneId: "gaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"));
        }

        private static Edit Make(
            string uuid = Uuid,
            long lamport = 1,
            string author = "ev1",
            string creator = "ev1",
            string zoneId = "gale",
            string entityId = EntityId,
            EditOp op = EditOp.HazardSet,
            EditPayload payload = null) =>
            new Edit(uuid, lamport, author, creator, zoneId, entityId, op, payload ?? EditPayload.Hazard(EditPayloadTests.Triangle, 128, null), false, false);
    }
}
