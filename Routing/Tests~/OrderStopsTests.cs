using System;
using System.Collections.Generic;
using System.Linq;
using MartianMap.Routing.FixtureGen;
using NUnit.Framework;

namespace MartianMap.Routing.Tests
{
    /// <summary>
    /// <see cref="Router.OrderStops"/> in TSP mode. The expected orders come from the oracle
    /// (~/scratch/c4-verify/oracle/cases_tsp.py, not committed), which tries every permutation over
    /// legs that are straight segments on flat ground; the synthetic mission's values are the ones
    /// computed by hand from the ramp (<see cref="SynthGoldenValuesTests"/>).
    /// </summary>
    public class OrderStopsTests
    {
        private const double Rounding = 1e-6;

        internal static readonly DateTime T = new DateTime(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc);

        private static CostParams P => Fixtures.CostParams;

        private static Conditions Calm => new Conditions(6.0, Array.Empty<ConditionModifier>());

        private static EnuPoint At(int i, int j) => new EnuPoint(2 * i, 2 * j);

        // The synthetic mission (convenciones.md, "Cómo se fija un contrato"): base [-100, -20];
        // s1 [0, -20] and s2 [100, -20], value 5 and 8, dwell 600 s; budget 14400 s and 50 uGy;
        // 6 uGy/h with a WSAEnlilSimulations modifier of 0.95, which counts as 1.
        internal static readonly EnuPoint Base = new EnuPoint(-100, -20);

        internal static Stop[] SynthStops => new[]
        {
            new Stop("s1", new EnuPoint(0, -20), 5, 600),
            new Stop("s2", new EnuPoint(100, -20), 8, 600),
        };

        internal static Conditions SynthConditions => new Conditions(6.0, new[]
        {
            new ConditionModifier(0.95, new DateTime(2026, 11, 14, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 11, 15, 0, 0, 0, DateTimeKind.Utc)),
        });

        internal static CostGrid Synth { get; } = SynthFixtures.BuildSynthGrid(Fixtures.CostParams);

        internal static Field SynthField { get; } = Router.ReturnField(Synth, Base, Fixtures.CostParams);

        [Test]
        public void TheSyntheticMissionIsTheHandComputedPlan()
        {
            IReadOnlyList<Plan> plans = Router.OrderStops(Synth, Base, SynthStops, SynthField, new Budget(14400, 50), SynthConditions, T, StopMode.Tsp, P);
            Assert.That(plans.Count, Is.EqualTo(3));
            foreach (Plan plan in plans)
            {
                Assert.That(plan.Order, Is.EqualTo(new[] { "s1", "s2" }));
                Assert.That(plan.Dropped, Is.Empty);
                Assert.That(plan.Legs.Count, Is.EqualTo(3));
                Assert.That(plan.TotalS.P10, Is.EqualTo(2099.269760629825).Within(Rounding));
                Assert.That(plan.TotalS.P50, Is.EqualTo(2407.590821417194).Within(Rounding));
                Assert.That(plan.TotalS.P90, Is.EqualTo(2709.4885267714926).Within(Rounding));
                Assert.That(plan.LimitS, Is.EqualTo(30000).Within(Rounding));
                Assert.That(plan.DoseUGy, Is.EqualTo(4.0126513690286565).Within(Rounding));
                Assert.That(plan.ReturnOk, Is.True);
            }
        }

        [Test]
        public void ItVisitsTheStopsInTheCheapestOrder()
        {
            // Oracle: from node (0, 0) to home at node (39, 0), the best order is b d c a e
            // (349.85 s against 363.81 s for the next).
            CostGrid g = Grids.Flat(40, 20);
            Field home = Router.ReturnField(g, At(39, 0), P);
            Stop[] stops =
            {
                new Stop("a", At(30, 15), 1, 0), new Stop("b", At(5, 2), 1, 0), new Stop("c", At(25, 3), 1, 0),
                new Stop("d", At(8, 17), 1, 0), new Stop("e", At(36, 8), 1, 0),
            };
            Plan plan = Router.OrderStops(g, At(0, 0), stops, home, new Budget(14400, 50), Calm, T, StopMode.Tsp, P)[0];
            Assert.That(plan.Order, Is.EqualTo(new[] { "b", "d", "c", "a", "e" }));
            Assert.That(plan.Legs.Count, Is.EqualTo(6));
            EnuPoint3 end = plan.Legs[5].PolylineEnuM[plan.Legs[5].PolylineEnuM.Count - 1];
            Assert.That((end.E, end.N), Is.EqualTo((78.0, 0.0)));
        }

        [Test]
        public void TheOrderIsTheFastestsInAllThree()
        {
            // Oracle (check_order.py; every leg a unique optimum): from (0, 4) and back, the fastest
            // order is a c b (143.7688 s against 143.8760 s for a b c), while the safest's own order
            // would be a b c (163.9412 s; a c b costs it 166.4095 s). All three plans keep a c b.
            CostGrid g = Grids.FromMap(Grids.AmberPatch15x9, Grids.TiltHeight);
            Field home = Router.ReturnField(g, At(0, 4), P);
            Stop[] stops = { new Stop("a", At(0, 5), 1, 0), new Stop("b", At(8, 5), 1, 0), new Stop("c", At(13, 3), 1, 0) };
            IReadOnlyList<Plan> plans = Router.OrderStops(g, At(0, 4), stops, home, new Budget(14400, 50), Calm, T, StopMode.Tsp, P);
            foreach (Plan plan in plans)
            {
                Assert.That(plan.Order, Is.EqualTo(new[] { "a", "c", "b" }));
            }
        }

        [TestCase(2709.488281940435, true)]
        [TestCase(2709.487, false)]
        public void ReturnOkIsTheP90ElapsedPlusTheFieldOnLeaving(double budgetS, bool expected)
        {
            // Oracle: on leaving s2, 1954.7442633857477 s walked at P90 plus dwell, and the field
            // there (float, summed edge by edge from the base) is 754.7440185546875 s.
            Plan plan = Router.OrderStops(Synth, Base, SynthStops, SynthField, new Budget(budgetS, 50), SynthConditions, T, StopMode.Tsp, P)[0];
            Assert.That(plan.ReturnOk, Is.EqualTo(expected));
        }

        [Test]
        public void AnUnreachableStopGoesLastWithANotFoundLeg()
        {
            // Wall9x7: the start and home are west of the wall, a is east of it and b is west.
            CostGrid g = Grids.FromMap(Grids.Wall9x7);
            Field home = Router.ReturnField(g, At(0, 3), P);
            Stop[] stops = { new Stop("a", At(8, 3), 1, 0), new Stop("b", At(2, 5), 1, 0) };
            IReadOnlyList<Plan> plans = Router.OrderStops(g, At(0, 3), stops, home, new Budget(14400, 50), Calm, T, StopMode.Tsp, P);
            foreach (Plan plan in plans)
            {
                Assert.That(plan.Order, Is.EqualTo(new[] { "b", "a" }));
                Assert.That(plan.Legs.Select(l => l.Found), Is.EqualTo(new[] { true, false, false }));
                Assert.That(plan.TotalS.P10, Is.EqualTo(double.PositiveInfinity));
                Assert.That(plan.TotalS.P50, Is.EqualTo(double.PositiveInfinity));
                Assert.That(plan.TotalS.P90, Is.EqualTo(double.PositiveInfinity));
                Assert.That(plan.DoseUGy, Is.EqualTo(double.PositiveInfinity));
                Assert.That(plan.ReturnOk, Is.False);
            }
        }

        [Test]
        public void AHomeThatCannotBeReachedOrdersTheOpenPath()
        {
            // Oracle: without the return, from (0, 3) the open path visits b1 (1, 0) before b2 (2, 6).
            CostGrid g = Grids.FromMap(Grids.Wall9x7);
            Field home = Router.ReturnField(g, At(8, 3), P);
            Stop[] stops = { new Stop("b2", At(2, 6), 1, 0), new Stop("b1", At(1, 0), 1, 0) };
            Plan plan = Router.OrderStops(g, At(0, 3), stops, home, new Budget(14400, 50), Calm, T, StopMode.Tsp, P)[0];
            Assert.That(plan.Order, Is.EqualTo(new[] { "b1", "b2" }));
            Assert.That(plan.Legs.Select(l => l.Found), Is.EqualTo(new[] { true, true, false }));
            Assert.That(plan.ReturnOk, Is.False);
        }

        [Test]
        public void StopsOnTheSameNodeGiveEmptyLegs()
        {
            // s is on the start's node, t and u share one: s t u is the cheapest and, among the
            // orders that tie with it, the first.
            CostGrid g = Grids.Flat(10, 5);
            Field home = Router.ReturnField(g, At(0, 0), P);
            Stop[] stops = { new Stop("s", At(0, 0), 1, 0), new Stop("t", At(6, 4), 1, 0), new Stop("u", new EnuPoint(12.4, 7.6), 1, 0) };
            Plan plan = Router.OrderStops(g, At(0, 0), stops, home, new Budget(14400, 50), Calm, T, StopMode.Tsp, P)[0];
            Assert.That(plan.Order, Is.EqualTo(new[] { "s", "t", "u" }));
            Assert.That(plan.Legs.All(l => l.Found), Is.True);
            foreach (int k in new[] { 0, 2 })
            {
                Assert.That(plan.Legs[k].Cells.Count, Is.EqualTo(1));
                Assert.That(plan.Legs[k].EtaS.P50, Is.EqualTo(0));
            }

            Assert.That(plan.ReturnOk, Is.True);
        }

        [Test]
        public void NoStopsIsOneLegHome()
        {
            CostGrid g = Grids.Flat(10, 5);
            Field home = Router.ReturnField(g, At(9, 4), P);
            Plan plan = Router.OrderStops(g, At(0, 0), Array.Empty<Stop>(), home, new Budget(14400, 50), Calm, T, StopMode.Tsp, P)[0];
            Route direct = Router.PlanRoute(g, At(0, 0), At(9, 4), P);
            Assert.That(plan.Order, Is.Empty);
            Assert.That(plan.Legs.Count, Is.EqualTo(1));
            Assert.That(plan.Legs[0].Cells, Is.EqualTo(direct.Cells));
            Assert.That(plan.TotalS, Is.EqualTo(direct.EtaS));
            Assert.That(plan.ReturnOk, Is.True);
        }

        [Test]
        public void OpAndMoreThanTwelveStopsGiveAPlan()
        {
            CostGrid g = Grids.Flat(20, 5);
            Field home = Router.ReturnField(g, At(0, 0), P);
            Stop[] one = { new Stop("a", At(3, 3), 1, 0) };
            Stop[] thirteen = Enumerable.Range(0, 13).Select(k => new Stop($"s{k}", At(k, 2), 1, 0)).ToArray();

            IReadOnlyList<Plan> op = Router.OrderStops(g, At(0, 0), one, home, new Budget(14400, 50), Calm, T, StopMode.Op, P);
            Assert.That(op, Is.Not.Empty);
            Assert.That(op[0].Order, Is.EqualTo(new[] { "a" }));
            Assert.That(op[0].Dropped, Is.Empty);

            IReadOnlyList<Plan> tsp = Router.OrderStops(g, At(0, 0), thirteen, home, new Budget(14400, 50), Calm, T, StopMode.Tsp, P);
            Assert.That(tsp, Is.Not.Empty);
            Assert.That(tsp[0].Order, Is.EquivalentTo(thirteen.Select(s => s.Id)));
        }

        [Test]
        public void RepeatedIdsAndNullParamsThrow()
        {
            CostGrid g = Grids.Flat(10, 5);
            Field home = Router.ReturnField(g, At(0, 0), P);
            Stop[] twice = { new Stop("a", At(3, 3), 1, 0), new Stop("a", At(5, 1), 1, 0) };
            Assert.Throws<ArgumentException>(() => Router.OrderStops(g, At(0, 0), twice, home, new Budget(14400, 50), Calm, T, StopMode.Tsp, P));
            Assert.Throws<ArgumentNullException>(() => Router.OrderStops(g, At(0, 0), twice.Take(1).ToArray(), home, new Budget(14400, 50), Calm, T, StopMode.Tsp, null));
        }
    }
}
