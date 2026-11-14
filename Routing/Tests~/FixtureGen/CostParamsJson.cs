using System.IO;
using System.Text.Json;

namespace MartianMap.Routing.FixtureGen
{
    /// <summary>Reads <c>cost_params.json</c>. Routing itself reads no JSON: Terrain does this in the app.</summary>
    public static class CostParamsJson
    {
        public static CostParams Read(string path)
        {
            using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
            JsonElement r = doc.RootElement;
            JsonElement v0 = r.GetProperty("v0_mps");
            JsonElement b = r.GetProperty("b");
            return new CostParams(
                r.GetProperty("schema_version").GetInt32(),
                new Percentiles(v0.GetProperty("p10").GetDouble(), v0.GetProperty("p50").GetDouble(), v0.GetProperty("p90").GetDouble()),
                new SensitivityRange(b.GetProperty("p50").GetDouble(), b.GetProperty("low").GetDouble(), b.GetProperty("high").GetDouble()),
                r.GetProperty("c").GetDouble(),
                r.GetProperty("walk_ceiling_mps").GetDouble(),
                r.GetProperty("max_slope_deg").GetDouble(),
                r.GetProperty("relief_block_cm").GetDouble(),
                r.GetProperty("relief_penalty_from_cm").GetDouble(),
                r.GetProperty("hazard_red_min").GetByte(),
                r.GetProperty("hazard_amber_min").GetByte(),
                r.GetProperty("f_terreno").GetDouble(),
                r.GetProperty("f_rug_min").GetDouble(),
                r.GetProperty("amber_penalty_s_per_m").GetDouble(),
                r.GetProperty("alt_safest_amber_mult").GetDouble(),
                r.GetProperty("alt_gentlest_s_per_m").GetDouble());
        }
    }
}
