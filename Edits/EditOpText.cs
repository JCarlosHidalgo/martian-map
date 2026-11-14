using System;

namespace MartianMap.Edits
{
    /// <summary>
    /// The only table between <see cref="EditOp"/> and the text of <c>op</c> that travels in the
    /// log and in C3 (docs/guide/chapters/convenciones.md, "Catálogo de op"). It works on strings
    /// only: it reads no JSON. The comparison is exact and ordinal.
    /// </summary>
    public static class EditOpText
    {
        public const string HazardSet = "hazard.set";

        public const string ObstacleSet = "obstacle.set";

        public const string NoteSet = "note.set";

        /// <summary>The text of <paramref name="op"/>; throws if it is not a defined value.</summary>
        public static string ToWire(EditOp op)
        {
            switch (op)
            {
                case EditOp.HazardSet:
                    return HazardSet;
                case EditOp.ObstacleSet:
                    return ObstacleSet;
                case EditOp.NoteSet:
                    return NoteSet;
                default:
                    throw new ArgumentOutOfRangeException(nameof(op));
            }
        }

        /// <summary>
        /// False, with <paramref name="op"/> = <see cref="EditOp.None"/>, if <paramref name="wire"/> is null or
        /// is not one of the three texts. It never throws: an unknown <c>op</c> is bad data, not a
        /// bad argument.
        /// </summary>
        public static bool TryParse(string wire, out EditOp op)
        {
            switch (wire)
            {
                case HazardSet:
                    op = EditOp.HazardSet;
                    return true;
                case ObstacleSet:
                    op = EditOp.ObstacleSet;
                    return true;
                case NoteSet:
                    op = EditOp.NoteSet;
                    return true;
                default:
                    op = EditOp.None;
                    return false;
            }
        }
    }
}
