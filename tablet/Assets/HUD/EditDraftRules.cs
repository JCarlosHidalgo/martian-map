using System;
using System.Collections.Generic;
using MartianMap.Edits;
using MartianMap.Terrain;
using UnityEngine;

namespace MartianMap.HUD
{
    /// <summary>HUD input validation and confirmation policy; never applies edits to rasters.</summary>
    public static class EditDraftRules
    {
        public static bool TryQuantize(Vector2 metres, out FixedPoint point)
        {
            point = default;
            double east = Math.Round((double)metres.x * FixedPoint.UnitsPerMetre, MidpointRounding.AwayFromZero);
            double north = Math.Round((double)metres.y * FixedPoint.UnitsPerMetre, MidpointRounding.AwayFromZero);
            if (double.IsNaN(east) || double.IsNaN(north) || east < int.MinValue || east > int.MaxValue ||
                north < int.MinValue || north > int.MaxValue)
            {
                return false;
            }

            point = new FixedPoint((int)east, (int)north);
            return true;
        }

        public static bool IsSimpleRing(IReadOnlyList<FixedPoint> ring)
        {
            if (ring == null || ring.Count < 3)
            {
                return false;
            }

            var seen = new HashSet<(int, int)>();
            decimal area = 0;
            for (int i = 0; i < ring.Count; i++)
            {
                FixedPoint a = ring[i];
                FixedPoint b = ring[(i + 1) % ring.Count];
                FixedPoint c = ring[(i + 2) % ring.Count];
                if (!seen.Add((a.E, a.N)))
                {
                    return false;
                }

                area += (decimal)a.E * b.N - (decimal)a.N * b.E;
                if (Cross(a, b, c) == 0 &&
                    ((decimal)a.E - b.E) * ((decimal)c.E - b.E) + ((decimal)a.N - b.N) * ((decimal)c.N - b.N) > 0)
                {
                    return false;
                }

                for (int j = i + 1; j < ring.Count; j++)
                {
                    if (j == i + 1 || (i == 0 && j == ring.Count - 1))
                    {
                        continue;
                    }

                    if (Intersects(a, b, ring[j], ring[(j + 1) % ring.Count]))
                    {
                        return false;
                    }
                }
            }

            return area != 0;
        }

        public static bool RequiresConfirmation(LiveEntity existing, string author, EditRequested request)
        {
            if (existing == null)
            {
                return false;
            }

            if (existing.Op != request.Op)
            {
                throw new ArgumentException("An entity cannot change its operation.", nameof(request));
            }

            return existing.Creator != author && existing.Op != EditOp.NoteSet &&
                (request.Tombstone || (existing.Op == EditOp.HazardSet && request.Payload.Level < existing.Level));
        }

        public static EditPayload CopyPayload(EditPayload source, bool? confirmed)
        {
            if (source.Op == EditOp.NoteSet)
            {
                return EditPayload.Note(source.Point.Value, source.Text);
            }

            var ring = new FixedPoint[source.Polygon.Count];
            for (int i = 0; i < ring.Length; i++)
            {
                ring[i] = source.Polygon[i];
            }

            return source.Op == EditOp.HazardSet ? EditPayload.Hazard(ring, source.Level.Value, confirmed)
                : EditPayload.Obstacle(ring, confirmed);
        }

        public static bool SamePayload(EditPayload a, EditPayload b)
        {
            if (a.Op != b.Op || a.Level != b.Level || a.Text != b.Text || a.Point?.E != b.Point?.E || a.Point?.N != b.Point?.N)
            {
                return false;
            }

            if (a.Polygon == null || b.Polygon == null)
            {
                return a.Polygon == b.Polygon;
            }

            if (a.Polygon.Count != b.Polygon.Count)
            {
                return false;
            }

            for (int i = 0; i < a.Polygon.Count; i++)
            {
                if (a.Polygon[i].E != b.Polygon[i].E || a.Polygon[i].N != b.Polygon[i].N)
                {
                    return false;
                }
            }

            return true;
        }

        private static decimal Cross(FixedPoint a, FixedPoint b, FixedPoint c) =>
            ((decimal)b.E - a.E) * ((decimal)c.N - a.N) - ((decimal)b.N - a.N) * ((decimal)c.E - a.E);

        private static bool OnSegment(FixedPoint a, FixedPoint b, FixedPoint p) =>
            p.E >= Math.Min(a.E, b.E) && p.E <= Math.Max(a.E, b.E) &&
            p.N >= Math.Min(a.N, b.N) && p.N <= Math.Max(a.N, b.N);

        private static bool Intersects(FixedPoint a, FixedPoint b, FixedPoint c, FixedPoint d)
        {
            decimal abC = Cross(a, b, c);
            decimal abD = Cross(a, b, d);
            decimal cdA = Cross(c, d, a);
            decimal cdB = Cross(c, d, b);
            return (Math.Sign(abC) * Math.Sign(abD) < 0 && Math.Sign(cdA) * Math.Sign(cdB) < 0) ||
                (abC == 0 && OnSegment(a, b, c)) || (abD == 0 && OnSegment(a, b, d)) ||
                (cdA == 0 && OnSegment(c, d, a)) || (cdB == 0 && OnSegment(c, d, b));
        }
    }
}
