using System.Runtime.CompilerServices;

// The merge and the polygon test are internal: Apply and LiveEntities are the API. The tests call
// them instead of copying them.
[assembly: InternalsVisibleTo("MartianMap.Edits.Tests")]
