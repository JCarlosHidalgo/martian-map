using NUnit.Framework;

namespace MartianMap.Routing.Tests
{
    /// <summary>The <c>return_ok</c> rule (docs/guide/chapters/tablet.md, "API de Routing").</summary>
    public class ReturnFitsTests
    {
        [Test]
        public void ExactlyFillingTheBudgetFits()
        {
            Assert.That(Router.ReturnFits(10000, 4400, 14400), Is.True);
        }

        [Test]
        public void OneSecondOverDoesNotFit()
        {
            Assert.That(Router.ReturnFits(10000, 4401, 14400), Is.False);
        }

        [Test]
        public void TheElapsedTimeIsTheP90One()
        {
            // A stop reached at P50 9000 s / P90 11000 s, with the return at P90 4000 s, in 14400 s:
            // it would fit with the P50 elapsed time (13000 s), not with the P90 one (15000 s).
            Assert.That(Router.ReturnFits(9000, 4000, 14400), Is.True);
            Assert.That(Router.ReturnFits(11000, 4000, 14400), Is.False);
        }
    }
}
