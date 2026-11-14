using System.Collections.Generic;
using Newtonsoft.Json;

namespace MartianMap.Terrain
{
    /// <summary><c>zone.json</c> (C1, "zone.json").</summary>
    public sealed class ZoneInfo
    {
        [JsonProperty("schema_version")] public int SchemaVersion { get; set; }
        [JsonProperty("zone_id")] public string ZoneId { get; set; }
        [JsonProperty("name")] public string Name { get; set; }
        [JsonProperty("crs")] public string Crs { get; set; }
        [JsonProperty("origin_lat_deg")] public double OriginLatDeg { get; set; }
        [JsonProperty("origin_lon_deg")] public double OriginLonDeg { get; set; }
        [JsonProperty("origin_height_m")] public double OriginHeightM { get; set; }
        [JsonProperty("projection")] public string Projection { get; set; }
        [JsonProperty("radius_m")] public double RadiusM { get; set; } = 3396190.0;

        /// <summary>[e, n] of the habitat, in metres.</summary>
        [JsonProperty("habitat_enu_m")] public double[] HabitatEnuM { get; set; }

        /// <summary>[e_min, n_min, e_max, n_max], integer metres. SW corner is the x/y grid origin.</summary>
        [JsonProperty("extent_enu_m")] public double[] ExtentEnuM { get; set; }

        [JsonProperty("base_m_per_px")] public double BaseMPerPx { get; set; } = 1.0;
        [JsonProperty("tiles")] public List<ZoneTileRef> Tiles { get; set; } = new List<ZoneTileRef>();
        [JsonProperty("layers")] public List<ZoneLayerInfo> Layers { get; set; } = new List<ZoneLayerInfo>();
        [JsonProperty("pov")] public List<PovPin> Pov { get; set; } = new List<PovPin>();
    }

    public sealed class ZoneTileRef
    {
        [JsonProperty("z")] public int Z { get; set; }
        [JsonProperty("x")] public int X { get; set; }
        [JsonProperty("y")] public int Y { get; set; }
        [JsonProperty("version")] public int Version { get; set; }
        [JsonProperty("sha256")] public string Sha256 { get; set; }
    }

    /// <summary>A layer as described once per zone (C1, "Capas del tile").</summary>
    public sealed class ZoneLayerInfo
    {
        [JsonProperty("id")] public string Id { get; set; }
        [JsonProperty("dtype")] public string Dtype { get; set; } = "uint8";
        [JsonProperty("source_id")] public string SourceId { get; set; }
        [JsonProperty("qualitative")] public bool Qualitative { get; set; }
        [JsonProperty("unit")] public string Unit { get; set; }
        [JsonProperty("scale")] public double? Scale { get; set; }
        [JsonProperty("offset")] public double? Offset { get; set; }
    }

    public sealed class PovPin
    {
        [JsonProperty("id")] public string Id { get; set; }
        [JsonProperty("source_id")] public string SourceId { get; set; }
        [JsonProperty("enu_m")] public double[] EnuM { get; set; }
        [JsonProperty("t_utc")] public string TUtc { get; set; }
        [JsonProperty("sol")] public int? Sol { get; set; }
        [JsonProperty("file")] public string File { get; set; }
        [JsonProperty("media_type")] public string MediaType { get; set; }
        [JsonProperty("sha256")] public string Sha256 { get; set; }
        [JsonProperty("version")] public int Version { get; set; }
        [JsonProperty("credit")] public string Credit { get; set; }
        [JsonProperty("url")] public string Url { get; set; }
    }

    /// <summary><c>tile.json</c> (C1, "Tiles (C1)").</summary>
    public sealed class TileInfo
    {
        [JsonProperty("schema_version")] public int SchemaVersion { get; set; }
        [JsonProperty("zone_id")] public string ZoneId { get; set; }
        [JsonProperty("z")] public int Z { get; set; }
        [JsonProperty("x")] public int X { get; set; }
        [JsonProperty("y")] public int Y { get; set; }
        [JsonProperty("size_px")] public int SizePx { get; set; } = TileData.Size;
        [JsonProperty("m_per_px")] public double MPerPx { get; set; } = 1.0;

        /// <summary>[e, n] of the tile's SW corner, in metres.</summary>
        [JsonProperty("origin_enu_m")] public double[] OriginEnuM { get; set; }

        [JsonProperty("height")] public HeightSpec Height { get; set; }
        [JsonProperty("layers")] public List<TileLayerRef> Layers { get; set; } = new List<TileLayerRef>();
        [JsonProperty("version")] public int Version { get; set; }
        [JsonProperty("sha256")] public Dictionary<string, string> Sha256 { get; set; } = new Dictionary<string, string>();
        [JsonProperty("source_ids")] public List<string> SourceIds { get; set; } = new List<string>();
    }

    public sealed class HeightSpec
    {
        [JsonProperty("file")] public string File { get; set; } = "height.r16";
        [JsonProperty("dtype")] public string Dtype { get; set; } = "uint16";
        [JsonProperty("scale")] public double Scale { get; set; }
        [JsonProperty("offset")] public double Offset { get; set; }
        [JsonProperty("nodata")] public int Nodata { get; set; } = 65535;
        [JsonProperty("row_order")] public string RowOrder { get; set; } = "north_up";
        [JsonProperty("endian")] public string Endian { get; set; } = "le";
        [JsonProperty("vertical_datum")] public string VerticalDatum { get; set; } = "areoid";
    }

    public sealed class TileLayerRef
    {
        [JsonProperty("id")] public string Id { get; set; }
        [JsonProperty("file")] public string File { get; set; }
    }

    /// <summary><c>conditions.json</c> (C6, "Condiciones").</summary>
    public sealed class ConditionsInfo
    {
        [JsonProperty("schema_version")] public int SchemaVersion { get; set; }
        [JsonProperty("source_id")] public string SourceId { get; set; }
        [JsonProperty("t_utc")] public string TUtc { get; set; }
        [JsonProperty("sol")] public int? Sol { get; set; }
        [JsonProperty("t_valid_utc")] public string TValidUtc { get; set; }
        [JsonProperty("quantity")] public string Quantity { get; set; }
        [JsonProperty("value")] public double Value { get; set; }
        [JsonProperty("unit")] public string Unit { get; set; }
        [JsonProperty("uncertainty")] public double Uncertainty { get; set; }
        [JsonProperty("basis")] public string Basis { get; set; }
        [JsonProperty("derived_from")] public string DerivedFrom { get; set; }
        [JsonProperty("modifiers")] public List<ConditionsModifier> Modifiers { get; set; } = new List<ConditionsModifier>();
    }

    public sealed class ConditionsModifier
    {
        [JsonProperty("donki_activity_id")] public string DonkiActivityId { get; set; }
        [JsonProperty("type")] public string Type { get; set; }
        [JsonProperty("factor")] public double Factor { get; set; }
        [JsonProperty("valid_from")] public string ValidFrom { get; set; }
        [JsonProperty("valid_to")] public string ValidTo { get; set; }
    }
}
