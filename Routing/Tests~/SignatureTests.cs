using System.Collections.Generic;
using NUnit.Framework;

namespace MartianMap.Routing.Tests
{
    /// <summary>
    /// What the calls return is read-only, like every C4 type: a list shared by two consumers
    /// cannot be reordered by one of them (docs/guide/chapters/tablet.md, "Tipos").
    /// </summary>
    public class SignatureTests
    {
        [Test]
        public void AlternativesReturnsAReadOnlyListOfRoutes()
        {
            Assert.That(typeof(Router).GetMethod(nameof(Router.Alternatives)).ReturnType, Is.EqualTo(typeof(IReadOnlyList<Route>)));
        }

        [Test]
        public void OrderStopsReturnsAReadOnlyListOfPlans()
        {
            Assert.That(typeof(Router).GetMethod(nameof(Router.OrderStops)).ReturnType, Is.EqualTo(typeof(IReadOnlyList<Plan>)));
        }
    }
}
