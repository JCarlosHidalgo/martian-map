using UnityEngine;

namespace MartianMap.Terrain
{
    /// <summary>One constant per shader property of C5. The 2D-only ones are marked.</summary>
    public static class ShaderIds
    {
        // Per tile (MaterialPropertyBlock).
        public static readonly int HeightTex = Shader.PropertyToID("_HeightTex");
        public static readonly int HeightScale = Shader.PropertyToID("_HeightScale");
        public static readonly int HeightOffset = Shader.PropertyToID("_HeightOffset");
        public static readonly int HazardTex = Shader.PropertyToID("_HazardTex");
        public static readonly int LayerTex0 = Shader.PropertyToID("_LayerTex0");
        public static readonly int LayerTex1 = Shader.PropertyToID("_LayerTex1");
        public static readonly int LayerTex2 = Shader.PropertyToID("_LayerTex2");

        /// <summary>2D: R8 mask, 1 where the height sample is nodata.</summary>
        public static readonly int NodataTex = Shader.PropertyToID("_NodataTex");

        /// <summary>2D: metres per sample of this tile, for the hillshade slope.</summary>
        public static readonly int MPerPx = Shader.PropertyToID("_MPerPx");

        // Global (Shader.SetGlobal...).
        public static readonly int ZoneOriginHeightM = Shader.PropertyToID("_ZoneOriginHeightM");
        public static readonly int LayerOn0 = Shader.PropertyToID("_LayerOn0");
        public static readonly int LayerOn1 = Shader.PropertyToID("_LayerOn1");
        public static readonly int LayerOn2 = Shader.PropertyToID("_LayerOn2");
        public static readonly int HeatTex = Shader.PropertyToID("_HeatTex");
        public static readonly int HeatMaxS = Shader.PropertyToID("_HeatMaxS");
        public static readonly int HazardZoneTex = Shader.PropertyToID("_HazardZoneTex");
        public static readonly int HeatTexRect = Shader.PropertyToID("_HeatTexRect");
        public static readonly int FogFarColor = Shader.PropertyToID("_FogFarColor");
        public static readonly int FogFarStrength = Shader.PropertyToID("_FogFarStrength");
        public static readonly int FogFarAmberMin = Shader.PropertyToID("_FogFarAmberMin");
        public static readonly int FogFarRedMin = Shader.PropertyToID("_FogFarRedMin");

        /// <summary>2D: (min, max) scene Y of the zone, for the elevation colour ramp.</summary>
        public static readonly int ZoneHeightRange = Shader.PropertyToID("_ZoneHeightRange");

        /// <summary>2D: 0 or 1 for the layers that have no texture slot: topo, hazard and heat.</summary>
        public static readonly int TopoOn = Shader.PropertyToID("_TopoOn");
        public static readonly int HazardOn = Shader.PropertyToID("_HazardOn");
        public static readonly int HeatOn = Shader.PropertyToID("_HeatOn");

        /// <summary>2D: contour interval in metres.</summary>
        public static readonly int ContourIntervalM = Shader.PropertyToID("_ContourIntervalM");
    }
}
