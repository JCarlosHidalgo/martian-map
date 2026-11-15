using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace MartianMap.Routing
{
    /// <summary>
    /// The copies behind the immutable types: every C4 type copies what it receives, so a caller
    /// that keeps or changes its array changes nothing here.
    /// </summary>
    internal static class ReadOnly
    {
        /// <summary>A copy, read-only even through a cast: a <see cref="ReadOnlyCollection{T}"/>, not an array.</summary>
        internal static IReadOnlyList<T> List<T>(T[] source, string name)
        {
            if (source == null)
            {
                throw new ArgumentNullException(name);
            }

            var copy = (T[])source.Clone();
            if (default(T) == null)
            {
                foreach (T item in copy)
                {
                    if (item == null)
                    {
                        throw new ArgumentException("No element may be null.", name);
                    }
                }
            }

            return new ReadOnlyCollection<T>(copy);
        }

        /// <summary>A copy of exactly <paramref name="count"/> values.</summary>
        internal static T[] Array<T>(T[] source, int count, string name)
        {
            return (T[])Exactly(source, count, name).Clone();
        }

        /// <summary><paramref name="source"/> itself, not a copy, once it is known to hold exactly <paramref name="count"/> values.</summary>
        internal static T[] Exactly<T>(T[] source, int count, string name)
        {
            if (source == null)
            {
                throw new ArgumentNullException(name);
            }

            if (source.Length != count)
            {
                throw new ArgumentException($"Expected {count} values.", name);
            }

            return source;
        }
    }
}
