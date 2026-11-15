using System;
using NUnit.Framework;

namespace MartianMap.Routing.Tests
{
    /// <summary>
    /// <see cref="Router.ReturnField"/>: the P90 field of the node nearest to <c>home</c>. The
    /// expected values are the oracle's (~/scratch/c4-verify/oracle/cases.py, dijkstra with
    /// reverse=True, not committed), as in <see cref="FieldCoreTests"/>.
    /// </summary>
    public class ReturnFieldTests
    {
        private const double Rel = 1e-4;

        private static CostParams P => Fixtures.CostParams;

        [Test]
        public void ReturnFieldIsTheP90FieldOfTheNearestNode()
        {
            CostGrid g = Grids.FromMap(Grids.WallGap9x7);
            var home = new EnuPoint(0.9, 6.2);
            Field f = Router.ReturnField(g, home, P);
            Assert.That(f.Grid, Is.SameAs(g));
            Assert.That(f.Home, Is.EqualTo(home));
            Assert.That(f.TimeS[g.Index(0, 3)], Is.EqualTo(0));
            Assert.That(f.TimeS[g.Index(8, 3)], Is.EqualTo(53.23971374999499).Within(Rel).Percent);
        }

        [Test]
        public void AHomeOnABlockedNodeHasNoReturnAnywhere()
        {
            CostGrid g = Grids.FromMap(Grids.Nodata8x6);
            Field f = Router.ReturnField(g, new EnuPoint(8, 6), P);
            Assert.That(f.TimeS.ToArray(), Has.All.EqualTo(float.PositiveInfinity));
        }

        [Test]
        public void ReturnFieldAllocatesOnlyItsResult()
        {
            // 120²: the result is 57.6 KB; a copy of it, or a workspace per call (~17 B a node), would pass the bound.
            CostGrid g = Grids.Flat(120, 120);
            Router.ReturnField(g, new EnuPoint(120, 120), P);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            long bytes = GC.GetAllocatedBytesForCurrentThread();
            Field first = Router.ReturnField(g, new EnuPoint(0, 0), P);
            Assert.That(GC.GetAllocatedBytesForCurrentThread() - bytes, Is.LessThan((4 * 120 * 120) + 40_000));

            float corner = first.TimeS[g.Index(119, 119)];
            Router.ReturnField(g, new EnuPoint(238, 238), P);
            Assert.That(first.TimeS[g.Index(119, 119)], Is.EqualTo(corner));
            Assert.That(first.TimeS[g.Index(0, 0)], Is.EqualTo(0));
        }

        [Test]
        public void NullParamsThrow()
        {
            Assert.Throws<ArgumentNullException>(() => Router.ReturnField(Grids.Flat(3, 3), new EnuPoint(0, 0), null));
        }
    }
}
