# Gale real-data presentation zone

The committed `tablet/Assets/StreamingAssets/zones/gale/` is a 512 × 512 m
unpacked `zone_full` bundle centred at 4.81° S, 137.385° E. It imports into the
tablet cache at launch as `gale` (zone version 1). Four 257 × 257 tiles contain
real Mars topography and grayscale orthoimagery. The build is about 1.7 MB;
the source IMG and JP2 files are not part of the repository.

## Provenance

- DTM: HiRISE `DTEEC_009149_1750_009294_1750_U01`, derived from a stereo pair
  acquired on 9 and 20 July 2008.
- Orthoimage: HiRISE `PSP_009149_1750_RED_A_01_ORTHO`, acquired on 9 July 2008.
- Product page: <https://www.uahirise.org/dtm/PSP_009149_1750>
- Observation and requested credit: <https://www.uahirise.org/PSP_009149_1750>
  — **NASA/JPL-Caltech/UArizona**.
- [NASA Mars Trek](https://trek.nasa.gov/mars/) is useful to show the location
  and broader context. It was **not** used to calculate these four tiles; the
  numerical heights and image here come from the HiRISE products above.

Slope, roughness, and base hazard are *derived from the HiRISE DTM* using the
project's thresholds. They are not separately measured hazard products.
Thermal, radiation, and POV layers are absent from this real-data zone; the
synthetic contract fixtures remain separate. Do not present real-time or
unmeasured conditions as observations.

## Rebuild

Install `numpy` and `rasterio` in a Python environment. Download the DTM IMG
and matching `RED_A` JP2 through the product page, then run:

```bash
python ingest/build_gale_demo.py /path/to/DTEEC_009149_1750_009294_1750_U01.IMG /path/to/PSP_009149_1750_RED_A_01_ORTHO.JP2
```

Open `tablet/` in Unity. The normal `Cache` source imports `zones/gale/` on
startup. If another zone was previously selected, choose **Gale** from the
zone selector. The bundle can also be packed as a tar and imported into the
habitat if needed. It does not require a server for tablet display.
