using System;
using System.Globalization;
using System.Text.RegularExpressions;
using MartianMap.Terrain;
using UnityEngine;

namespace MartianMap.HUD
{
    /// <summary>Immutable copy of one credited C1 POV item, safe to retain during asynchronous reads.</summary>
    public sealed class PovPhoto
    {
        private PovPhoto(PovPin pin, DateTime captured)
        {
            Id = pin.Id;
            SourceId = pin.SourceId;
            EnuM = new Vector2((float)pin.EnuM[0], (float)pin.EnuM[1]);
            CapturedUtc = captured;
            Sol = pin.Sol;
            File = pin.File;
            Sha256 = pin.Sha256;
            Version = pin.Version;
            Credit = pin.Credit;
            SourceUrl = TrySourceUrl(pin.Url, out Uri uri) ? uri.AbsoluteUri : string.Empty;
        }

        public string Id { get; }
        public string SourceId { get; }
        public Vector2 EnuM { get; }
        public DateTime CapturedUtc { get; }
        public int? Sol { get; }
        public string File { get; }
        public string Sha256 { get; }
        public int Version { get; }
        public string Credit { get; }
        public string SourceUrl { get; }

        public static bool TryCreate(PovPin pin, double[] bounds, out PovPhoto photo)
        {
            photo = null;
            if (pin == null || bounds == null || bounds.Length != 4 ||
                !Finite(bounds[0]) || !Finite(bounds[1]) || !Finite(bounds[2]) || !Finite(bounds[3]) ||
                bounds[0] >= bounds[2] || bounds[1] >= bounds[3] || string.IsNullOrWhiteSpace(pin.Id) ||
                string.IsNullOrWhiteSpace(pin.SourceId) || string.IsNullOrWhiteSpace(pin.Credit) ||
                pin.EnuM == null || pin.EnuM.Length != 2 || !Finite(pin.EnuM[0]) || !Finite(pin.EnuM[1]) ||
                Math.Abs(pin.EnuM[0]) > float.MaxValue || Math.Abs(pin.EnuM[1]) > float.MaxValue ||
                pin.EnuM[0] < bounds[0] || pin.EnuM[1] < bounds[1] || pin.EnuM[0] > bounds[2] || pin.EnuM[1] > bounds[3] ||
                pin.MediaType != "image/jpeg" || pin.Version < 1 || pin.Sol < 0 ||
                !IsFileName(pin.File) ||
                pin.Sha256 == null || !Regex.IsMatch(pin.Sha256, @"\A[0-9a-f]{64}\z") ||
                !DateTime.TryParseExact(pin.TUtc, "yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTime captured))
            {
                return false;
            }

            photo = new PovPhoto(pin, captured);
            return true;
        }

        public static bool TrySourceUrl(string value, out Uri uri) =>
            Uri.TryCreate(value, UriKind.Absolute, out uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);

        public static bool TryLocalPath(string folder, string file, out string path)
        {
            path = string.Empty;
            if (string.IsNullOrWhiteSpace(folder) || !IsFileName(file) ||
                (folder.Contains("://", StringComparison.Ordinal) &&
                !folder.StartsWith("file://", StringComparison.OrdinalIgnoreCase) &&
                !folder.StartsWith("jar:file://", StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            path = LocalFiles.Combine(folder.TrimEnd('/', '\\'), file);
            return true;
        }

        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

        private static bool IsFileName(string file)
        {
            if (string.IsNullOrWhiteSpace(file) || file == "." || file == "..")
            {
                return false;
            }

            foreach (char character in file)
            {
                if (char.IsControl(character) || "/\\:%?#<>\"|*".IndexOf(character) >= 0)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
