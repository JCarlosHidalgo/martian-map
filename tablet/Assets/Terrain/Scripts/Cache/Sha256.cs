using System;
using System.Security.Cryptography;
using System.Text;

namespace MartianMap.Terrain
{
    /// <summary>Lower-case hex SHA-256, the form C1 writes in <c>tile.json</c> and <c>zone.json</c>.</summary>
    public static class Sha256
    {
        public static string Hex(byte[] data)
        {
            using var sha = SHA256.Create();
            var hash = sha.ComputeHash(data);
            var text = new StringBuilder(hash.Length * 2);
            foreach (var b in hash)
            {
                text.Append(b.ToString("x2"));
            }

            return text.ToString();
        }

        /// <summary>True when <paramref name="expected"/> is empty (nothing to check) or equals the hash.</summary>
        public static bool Matches(byte[] data, string expected)
        {
            return string.IsNullOrEmpty(expected) || string.Equals(Hex(data), expected.Trim(), StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>A file whose content does not match its declared sha256, or a tile missing a file.</summary>
    public sealed class CacheIntegrityException : Exception
    {
        public CacheIntegrityException(string message) : base(message)
        {
        }
    }
}
