using System;
using System.Collections.Generic;

namespace MartianMap.Terrain
{
    /// <summary>A zone ready to draw: its <c>zone.json</c>, its tiles and its conditions.</summary>
    public sealed class LoadedZone : IDisposable
    {
        public LoadedZone(ZoneInfo zone, IReadOnlyList<TileData> tiles, ConditionsInfo conditions, string povFolder)
        {
            Zone = zone ?? throw new ArgumentNullException(nameof(zone));
            Tiles = tiles ?? throw new ArgumentNullException(nameof(tiles));
            Conditions = conditions;
            PovFolder = povFolder ?? string.Empty;
        }

        public ZoneInfo Zone { get; }
        public IReadOnlyList<TileData> Tiles { get; }
        public ConditionsInfo Conditions { get; }
        public string PovFolder { get; }

        public void Dispose()
        {
            foreach (var tile in Tiles)
            {
                tile.Dispose();
            }
        }
    }
}
