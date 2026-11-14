using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace MartianMap.Routing.Tests
{
    /// <summary>
    /// Every C4 type copies what it receives and exposes no array (docs/guide/chapters/tablet.md,
    /// "Contrato de rutas (C4)"); a <see cref="Field"/> belongs to one grid.
    /// </summary>
    public class ImmutabilityTests
    {
        private static readonly DateTime T = new DateTime(2026, 11, 14, 0, 0, 0, DateTimeKind.Utc);

        [Test]
        public void ARouteCopiesItsArrays()
        {
            var cells = new[] { 1, 2, 3 };
            var line = new[] { new EnuPoint3(0, 0, 0), new EnuPoint3(1, 0, 0) };
            var r = new Route(true, cells, line, 1, 0, 0, default, 0);
            cells[0] = 99;
            line[0] = new EnuPoint3(9, 9, 9);
            Assert.That(r.Cells[0], Is.EqualTo(1));
            Assert.That(r.PolylineEnuM[0].E, Is.EqualTo(0));
            AssertFrozen(r.Cells);
            AssertFrozen(r.PolylineEnuM);
        }

        [Test]
        public void APlanCopiesItsArraysAndRejectsNulls()
        {
            var order = new[] { "s1", "s2" };
            var legs = new[] { Route.NotFound };
            var plan = new Plan(order, Array.Empty<string>(), legs, default, 0, 0, true);
            order[0] = "x";
            Assert.That(plan.Order[0], Is.EqualTo("s1"));
            AssertFrozen(plan.Order);
            AssertFrozen(plan.Dropped);
            AssertFrozen(plan.Legs);
            Assert.Throws<ArgumentException>(() => new Plan(order, Array.Empty<string>(), new Route[] { null }, default, 0, 0, true));
            Assert.Throws<ArgumentException>(() => new Plan(new string[] { null }, Array.Empty<string>(), legs, default, 0, 0, true));
        }

        [Test]
        public void ConditionsCopyTheirModifiers()
        {
            var mods = new[] { new ConditionModifier(2, T, T.AddHours(1)) };
            var c = new Conditions(1, mods);
            mods[0] = new ConditionModifier(5, T, T.AddHours(1));
            Assert.That(c.Modifiers[0].Factor, Is.EqualTo(2));
            AssertFrozen(c.Modifiers);
        }

        [Test]
        public void TileRastersCopyTheirArrays()
        {
            const int n = TileRasters.SizePx * TileRasters.SizePx;
            var height = new ushort[n];
            var tile = new TileRasters(new EnuPoint(0, 0), height, 1, 0, new byte[n], new byte[n], new byte[n]);
            height[0] = 5;
            Assert.That(tile.Height[0], Is.EqualTo(0));
            Assert.Throws<ArgumentException>(() => new TileRasters(new EnuPoint(0, 0), new ushort[3], 1, 0, new byte[n], new byte[n], new byte[n]));
        }

        [Test]
        public void AFieldCopiesItsTimesAndKnowsItsGridAndHome()
        {
            CostGrid g = Grid();
            var times = new float[] { 1, 2 };
            var f = new Field(g, new EnuPoint(3, 4), times);
            times[0] = 7;
            Assert.That(f.TimeS[0], Is.EqualTo(1));
            Assert.That(f.Grid, Is.SameAs(g));
            Assert.That(f.Home, Is.EqualTo(new EnuPoint(3, 4)));
            Assert.That(typeof(Field).GetConstructors(), Is.Empty, "Only ReturnField makes a Field.");
            Assert.Throws<ArgumentException>(() => new Field(g, default, new float[3]));
        }

        [Test]
        public void OrderStopsRejectsAFieldOfAnotherGrid()
        {
            CostGrid g = Grid();
            var stops = new[] { new Stop("s1", new EnuPoint(1, 0), 5, 0) };
            var c = new Conditions(1, Array.Empty<ConditionModifier>());
            var twin = new Field(Grid(), default, new float[2]);
            Assert.Throws<ArgumentException>(() => Router.OrderStops(g, default, stops, twin, new Budget(1, 1), c, T, StopMode.Tsp, Fixtures.CostParams));
            Assert.Throws<ArgumentNullException>(() => Router.OrderStops(g, default, stops, null, new Budget(1, 1), c, T, StopMode.Tsp, Fixtures.CostParams));
        }

        private static CostGrid Grid() => new CostGrid(2, 1, 2f, new EnuPoint(0, 0), new float[2], new byte[2], new byte[2], new byte[2]);

        private static void AssertFrozen<TItem>(IReadOnlyList<TItem> list)
        {
            Assert.That(list, Is.Not.InstanceOf<TItem[]>());
            Assert.That(list is IList<TItem> writable && !writable.IsReadOnly, Is.False);
        }
    }
}
