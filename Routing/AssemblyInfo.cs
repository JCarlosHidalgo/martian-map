using System.Runtime.CompilerServices;

// The cost formula is internal: only Routing computes cost. The tests and the fixture
// generator call it instead of copying it.
[assembly: InternalsVisibleTo("MartianMap.Routing.Tests")]
[assembly: InternalsVisibleTo("MartianMap.Routing.FixtureGen")]

// The laptop benchmark runner picks the quick scenario, which is not public.
[assembly: InternalsVisibleTo("MartianMap.Routing.BenchmarkRunner")]
