using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using static MartianMap.Edits.Tests.Log;

namespace MartianMap.Edits.Tests
{
    /// <summary>
    /// <see cref="EditEngine.LiveEntities"/>: one walk in <c>(lamport, author, uuid)</c> order with the
    /// confirmation rule (docs/guide/chapters/convenciones.md, "Bajar un peligro"; plan A6–A10). Each
    /// expected list is "the index in the log of each live winner, in order", printed by
    /// ~/scratch/c4-verify/oracle/cases_apply.py t5 from the same logs (not committed).
    /// </summary>
    public class LiveEntitiesTests
    {
        [Test]
        public void TheLastWriteWinsInLamportAuthorUuidOrder()
        {
            Edit[] log =
            {
                E(2, "ev1", "1", H(20)), E(3, "ev1", "1", H(30)), E(1, "ev1", "1", H(10)),
                E(1, "ev1", "2", H(50)), E(5, "ev2", "2", H(100), creator: "ev1"), E(5, "ev10", "2", H(100), creator: "ev1"),
                E(7, "ev1", "3", H(40), uuid: "c2"), E(7, "ev1", "3", H(40), uuid: "c1"),
            };

            // Oracle: [1, 4, 6]: lamport 3; "ev2" > "ev10" byte by byte; uuid …c2 > …c1.
            AssertLive(log, 1, 4, 6);
        }

        [Test]
        public void AnotherAuthorCannotLowerWithoutConfirmed()
        {
            Edit[] log = { E(1, "ev1", "1", H(255)), E(2, "ev2", "1", H(128), creator: "ev1"), E(3, "ev2", "1", H(128, false), creator: "ev1") };
            Edit[] confirmed = { log[0], log[1], E(3, "ev2", "1", H(128, true), creator: "ev1") };

            // Oracle: [0] for the first two and for all three; [2] with confirmed; [3] when the creator lowers.
            AssertLive(log.Take(2).ToArray(), 0);
            AssertLive(log, 0);
            AssertLive(confirmed, 2);
            AssertLive(confirmed.Append(E(4, "ev1", "1", H(64))).ToArray(), 3);
        }

        [Test]
        public void TheRuleComparesTheAuthorWithTheEditsOwnCreator()
        {
            // Q1 / A6 (b): the author is compared with the creator the edit carries, not with the
            // winner's. ev2's lowering carries creator "ev2", so it is the creator lowering: LWW.
            Edit[] log = { E(1, "ev1", "1", H(200)), E(2, "ev2", "1", H(50), creator: "ev2") };

            // Oracle (cases_apply.py q1): [1].
            AssertLive(log, 1);
        }

        [Test]
        public void RaisingOrMovingIsPlainLww()
        {
            Edit[] log =
            {
                E(1, "ev1", "1", H(100)), E(2, "ev2", "1", H(200), creator: "ev1"),
                E(1, "ev1", "2", H(100)), E(2, "ev2", "2", H(100, ring: Moved), creator: "ev1"),
                E(1, "ev1", "3", Ob()), E(2, "ev2", "3", Ob(ring: Moved), creator: "ev1"),
            };

            // Oracle: [1, 3, 5].
            AssertLive(log, 1, 3, 5);
        }

        [Test]
        public void ATombstoneByAnotherAuthorNeedsConfirmed()
        {
            // Oracle: [0], [] and [0].
            AssertLive(new[] { E(1, "ev1", "1", H(100)), E(2, "ev2", "1", H(100), creator: "ev1", tombstone: true) }, 0);
            AssertLive(new[] { E(1, "ev1", "1", H(100)), E(2, "ev2", "1", H(100, true), creator: "ev1", tombstone: true) });
            AssertLive(new[] { E(1, "ev1", "1", Ob()), E(2, "ev2", "1", Ob(), creator: "ev1", tombstone: true) }, 0);
        }

        [Test]
        public void AWinnerFromTheHabitatIsNotEvaluated()
        {
            Edit[] log =
            {
                E(1, "ev1", "1", H(200)), E(2, "ev2", "1", H(50), creator: "ev1", fromHabitat: true),
                E(3, "ev3", "1", H(20), creator: "ev1"), E(4, "ev3", "1", H(80), creator: "ev1"),
            };

            // Oracle: [1] for the first three (the local lowering to 20 is weighed against 50); [3] for all four.
            AssertLive(log.Take(3).ToArray(), 1);
            AssertLive(log, 3);
        }

        [Test]
        public void AfterATombstoneAnyoneRecreates()
        {
            Edit[] log = { E(1, "ev1", "1", H(200)), E(2, "ev1", "1", H(200), tombstone: true), E(3, "ev2", "1", H(10), creator: "ev1") };

            // Oracle: [2]: a dead entity has no level, so 10 does not lower it (A7).
            AssertLive(log, 2);
        }

        [Test]
        public void AnEditWithAnotherOpIsIgnored()
        {
            Edit[] log =
            {
                E(1, "ev1", "1", H(10)), E(2, "ev1", "1", Ob()), E(3, "ev1", "1", Note("x")),
                E(1, "ev1", "2", H(10)), E(2, "ev1", "2", H(10), tombstone: true), E(3, "ev1", "2", Ob()),
            };

            // Oracle: [0]: the obstacle and the note on entity 1 are ignored, and the obstacle does not
            // bring the dead entity 2 back with another op (A8).
            AssertLive(log, 0);
        }

        [Test]
        public void NotesAreAlwaysLww()
        {
            Edit[] log =
            {
                E(1, "ev1", "1", Note("a")), E(2, "ev2", "1", Note("a"), creator: "ev1", tombstone: true),
                E(1, "ev1", "2", Note("a")), E(2, "ev2", "2", Note("b"), creator: "ev1"),
            };

            // Oracle: [3].
            AssertLive(log, 3);
        }

        [Test]
        public void LevelAndCreatorComeFromTheWinner()
        {
            Edit[] log = { E(1, "ev1", "a", H(77)), E(2, "ev2", "b", Ob()), E(3, "ev3", "c", Note("n")), E(9, "ev1", "a", H(78)), E(5, "ev4", "b", Ob(), creator: "ev2") };

            // Oracle: [2 note ev3 null, 4 obstacle ev2 255, 3 hazard ev1 78], in the winners' order.
            IReadOnlyList<LiveEntity> live = AssertLive(log, 2, 4, 3);
            Assert.That(live.Select(l => l.Level), Is.EqualTo(new byte?[] { null, 255, 78 }));
            Assert.That(live.Select(l => l.Creator), Is.EqualTo(new[] { "ev3", "ev2", "ev1" }));
            Assert.That(live.Select(l => l.Op), Is.EqualTo(new[] { EditOp.NoteSet, EditOp.ObstacleSet, EditOp.HazardSet }));
            Assert.That(((IList<LiveEntity>)live).IsReadOnly, Is.True);
        }

        [Test]
        public void ArgumentsAreChecked()
        {
            Assert.Throws<ArgumentNullException>(() => EditEngine.LiveEntities(null));
            Assert.Throws<ArgumentNullException>(() => EditEngine.LiveEntities(new[] { E(1, "ev1", "1", H(1)), null }));
            Assert.Throws<ArgumentException>(() => EditEngine.LiveEntities(new[] { E(1, "ev1", "1", H(1), uuid: "d"), E(2, "ev1", "2", H(1), uuid: "d") }));
            Assert.That(EditEngine.LiveEntities(new Edit[0]), Is.Empty);
        }

        private static IReadOnlyList<LiveEntity> AssertLive(Edit[] log, params int[] winners)
        {
            IReadOnlyList<LiveEntity> live = EditEngine.LiveEntities(log);
            Assert.That(live, Has.Count.EqualTo(winners.Length));
            for (int k = 0; k < winners.Length; k++)
            {
                Edit w = log[winners[k]];
                Assert.That(live[k].Payload, Is.SameAs(w.Payload), $"entity {k}");
                Assert.That(live[k].EntityId, Is.EqualTo(w.EntityId));
                Assert.That(live[k].Op, Is.EqualTo(w.Op));
                Assert.That(live[k].Creator, Is.EqualTo(w.Creator));
            }

            return live;
        }
    }
}
