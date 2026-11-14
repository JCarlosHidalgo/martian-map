using System;
using NUnit.Framework;

namespace MartianMap.Routing.Tests
{
    /// <summary>
    /// The points of the API (<c>start</c>, <c>goal</c>, <c>home</c>, a stop's <c>enu_m</c>): a
    /// non-finite one throws <see cref="ArgumentException"/>, a finite one outside the box of the
    /// grid's nodes throws <see cref="ArgumentOutOfRangeException"/> (docs/guide/chapters/tablet.md,
    /// "Tipos"). Both are checked before the body, which still throws NotImplementedException.
    /// </summary>
    public class PointTests
    {
        private static readonly DateTime T = new DateTime(2026, 11, 14, 0, 0, 0, DateTimeKind.Utc);

        // 3 × 2 nodes of 2 m from (10, 20): the nodes cover e 10…14 and n 20…22.
        private static readonly CostGrid G = new CostGrid(3, 2, 2f, new EnuPoint(10, 20), new float[6], new byte[6], new byte[6], new byte[6]);

        private static readonly EnuPoint In = new EnuPoint(12, 21);

        private static CostParams P => Fixtures.CostParams;

        [Test]
        public void ANonFinitePointThrows()
        {
            Assert.Throws<ArgumentException>(() => Router.PlanRoute(G, new EnuPoint(double.NaN, 21), In, P));
            Assert.Throws<ArgumentException>(() => Router.PlanRoute(G, In, new EnuPoint(12, double.PositiveInfinity), P));
            Assert.Throws<ArgumentException>(() => Router.ReturnField(G, new EnuPoint(double.NaN, double.NaN), P));
            Assert.Throws<ArgumentException>(() => Router.Alternatives(G, In, new EnuPoint(double.NegativeInfinity, 21), P));
        }

        [Test]
        public void AFinitePointOutsideTheGridThrows()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => Router.PlanRoute(G, new EnuPoint(9.99, 21), In, P));
            Assert.Throws<ArgumentOutOfRangeException>(() => Router.PlanRoute(G, In, new EnuPoint(12, 22.01), P));
            Assert.Throws<ArgumentOutOfRangeException>(() => Router.ReturnField(G, new EnuPoint(5000, 21), P));
            Assert.Throws<ArgumentOutOfRangeException>(() => Router.Alternatives(G, new EnuPoint(14.01, 20), In, P));
        }

        [Test]
        public void APointOnTheEdgeOfTheGridIsValid()
        {
            Assert.Throws<NotImplementedException>(() => Router.PlanRoute(G, new EnuPoint(10, 20), new EnuPoint(14, 22), P));
            Assert.Throws<NotImplementedException>(() => Router.ReturnField(G, new EnuPoint(14, 20), P));
            Assert.Throws<NotImplementedException>(() => Router.Alternatives(G, new EnuPoint(10, 22), In, P));
        }

        [Test]
        public void OrderStopsChecksTheStartAndEveryStop()
        {
            var field = new Field(G, In, new float[6]);
            var c = new Conditions(6, Array.Empty<ConditionModifier>());
            var budget = new Budget(14400, 50);
            Stop[] With(EnuPoint p) => new[] { new Stop("s1", In, 5, 0), new Stop("s2", p, 8, 0) };

            Assert.Throws<ArgumentOutOfRangeException>(() => Router.OrderStops(G, new EnuPoint(0, 0), With(In), field, budget, c, T, StopMode.Tsp, P));
            Assert.Throws<ArgumentOutOfRangeException>(() => Router.OrderStops(G, In, With(new EnuPoint(30, 21)), field, budget, c, T, StopMode.Op, P));
            Assert.Throws<ArgumentException>(() => Router.OrderStops(G, In, With(new EnuPoint(double.NaN, 21)), field, budget, c, T, StopMode.Tsp, P));
            Assert.Throws<ArgumentNullException>(() => Router.OrderStops(G, In, null, field, budget, c, T, StopMode.Tsp, P));
            Assert.Throws<ArgumentException>(() => Router.OrderStops(G, In, new Stop[] { null }, field, budget, c, T, StopMode.Tsp, P));
            Assert.Throws<NotImplementedException>(() => Router.OrderStops(G, In, With(new EnuPoint(14, 22)), field, budget, c, T, StopMode.Tsp, P));
        }
    }
}
