# Fuentes

Esta página dice de dónde sale cada afirmación que la guía cita con `S1…S13` o con
`frente A–D §n`, y qué URL se sondean en H0. Cómo se leen esas citas en el resto de la guía:
[Contexto del reto](./contexto.md), sección "Cómo leer las fuentes".

Ninguna otra página repite lo que hay aquí: citan la etiqueta y esta página la resuelve.

## Sondeos en vivo

Todos son peticiones GET de lectura, hechas con `curl` el **2026-11-12**. Un sondeo dice qué
devolvió la fuente ese día; si algo cambia después, lo detecta el sondeo de H0 (sección "URL de
sondeo de H0").

| # | Qué se sondeó | Qué devolvió |
|---|---|---|
| S1 | La búsqueda STAC de `mro_ctx_controlled_usgs_dtms` (bbox 137.37, −4.85, 137.41, −4.80) y el TiTiler de USGS, `/cog/point` con `coord_crs=IAU_2015:49900`, sobre el ítem CTX `T01_000815_1749_XN_05S222W__P22_009716_1773_XI_02S223W` y el HiRISE `DTEEC_019698_1750_019988_1750_U01`, en 3 puntos de Gale | En (137.38, −4.85): `dem` −3813,6 m, `geoid_adjusted_dem` −3243,6 m, HiRISE −3251,7 m. En (137.385, −4.81): −4157,6, −3590,1 y −3592,8. En (137.40, −4.83): −3903,6, −3334,6 y −3349,2. **El `dem` queda 570,1, 567,5 y 569,1 m por debajo del ajustado.** El ajustado menos HiRISE: +8,1, +2,7 y +14,6 m |
| S1b | El `provenance.txt` de ese ítem CTX | `point2dem … +R=3396190` para `-DEM.tif`, y `dem_geoid -DEM.tif … --geoid MOLA` para el ajustado: el `dem` está sobre la esfera y el ajustado sobre el areoide |
| S1c | La búsqueda STAC de CTX en Jezero (bbox 77.40, 18.40, 77.50, 18.48; 10 ítems) y el TiTiler en (77.45, 18.44) y (77.43, 18.45) | **Los 10 ítems CTX de Jezero no tienen `dem` ni `geoid_adjusted_dem`: solo `dtm`** (más `mola_csv`), con `datetime` 2023-04-18. Los 20 ítems de Gale (bbox 137.2, −5.0, 137.6, −4.6) sí traen los dos. El `dtm` CTX de Jezero frente a HiRISE (−2569,1 y −2557,1 m): +27,4, +37,6 y +11,0 m en un punto y +27,8, +46,4 y +12,4 m en el otro, según el ítem. Está sobre el areoide, sin el offset de ~570 m, pero con un sesgo de 10 a 47 m entre ítems |
| S2 | DONKI en `ccmc.gsfc.nasa.gov/DONKI/WS/get/…`, sin key: `FLR`, `SEP` y `WSAEnlilSimulations`, del 2026-10-13 al 2026-11-12 | 200 en los tres. `FLR` 13 registros, `SEP` 0, `WSAEnlilSimulations` 56, de ellos 13 con impacto `location: "Mars"`. La página de CCMC avisa de que el 2026-09-30 la API migró de `kauai.ccmc.gsfc.nasa.gov` a `ccmc.gsfc.nasa.gov` |
| S2b | `api.nasa.gov/DONKI` con `DEMO_KEY`: `FLR`, con y sin barra final, y `SEP` | 429 `OVER_RATE_LIMIT` en la primera llamada, con `x-ratelimit-limit: 10`, `x-ratelimit-remaining: 0` y `retry-after: 33888` s (~9,4 h). El cupo es de 10 y el bloqueo dura horas. El 404 de `FLR` no se pudo volver a sondear |
| S3 | RAD en PDS PPI: `ERRATA.TXT` y el listado de `DATA/` | `PUBLICATION_DATE = 2026-08-03`; release 42, "DATA COVERAGE: 2025-11-05 to 2026-03-22 (Sols 4710-4843)"; el último directorio es `SOL_04710_04843` |
| S4 | Las notas de Unity 6000.3.28f1 y el archivo de releases | "Released on Nov 3"; "Graphics: Fixed Vulkan rendering artifacts on Mali GPUs with some renderpass/subpass configurations" (UUM-147578); "Android: Backported crash fixes for Android optimized frame pacing (Swappy)" (UUM-153366). Es la última 6.3 del archivo: 29f1 a 31f1 dan 404 |
| S5 | `gilzoide/unity-sqlite-net` en la API de GitHub: tags, árbol, `package.json` y `.meta` | Tag **1.3.3** (commit del 2026-10-14), `"license": "MIT"`, `"unity": "2021.2"`. Trae `Plugins/lib/android/arm64/libgilzoide-sqlite-net.so` (2 031 976 B), con `CPU: ARM64` en su `.meta`. El repositorio publica tags, no "Releases" |
| S6 | `PriorityQueue<TElement,TPriority>` en Microsoft Learn (los monikers) y el manual de Unity 6.3 | Monikers: solo `net-6.0` a `net-11.0`; ningún `netstandard-*` ni `netframework-*`. Unity 6.3: ".NET Standard 2.1 … default" y ".NET Framework 4.8 … plus additional APIs from .NET Standard 2.1" |
| S7 | El DOI 10.17189/m23v-ae12 (mosaico CTX del Murray Lab) en la API de DataCite | `rightsList`: "work of the U.S. Government…" y "Creative Commons Zero v1.0 Universal" (`cc0-1.0`), 2023 |
| S8 | El record 4931348 en la API de Zenodo | "THEMIS-derived Thermal Inertia of Jezero Crater, Mars", `license: cc-by-4.0`, `Jezero_Thermal_Inertia_v1.tiff` (1 721 889 B) |
| S9 | `/collections` de la STAC de USGS | Las 5 colecciones de Marte declaran `CC0-1.0`; la descripción de `mo_themis_controlled_mosaics` contiene "8-bit" |
| S10 | Prism en la API de GitHub y en el registro npm | `v5.16.0` (2026-07-17); `@stoplight/prism-cli` 5.16.0, con `engines: node >=24.18.0` |
| S11 | Los ToS de Cesium ion, §2.2.2 | "You may not copy, store, or redistribute any portion of Cesium Data Output in, or for use in, an offline, disconnected, or local computer environment. However, client-side and proxy-based caching are allowed as long as it is a general caching mechanism…" |
| S12 | La documentación de `BackgroundTasks` de FastAPI | "You can define background tasks to be run after returning a response" |
| S13 | Los bytes 0–6000 del `.cub` de inercia térmica 30S120E | `Format = Tile`, `TileSamples = 128`, `TileLines = 128`, `Type = Real`, `SimpleCylindrical`, `EquatorialRadius 3396190` y `PolarRadius 3376200`, longitud en ±180. Es teselado, así que la lectura por rangos es viable, pero no se midió con GDAL. Su elipsoide no es la esfera de los COG de STAC |

## Secciones de la investigación

`frente A–D §n` es una sección de la investigación que precedió al diseño. La tabla lleva solo
las secciones que la guía cita: qué afirma cada una y su fuente primaria.

| Sección | Qué afirma | Fuente primaria |
|---|---|---|
| frente A §1 | La Galaxy Tab S9 FE lleva un Exynos 1380 con GPU Mali-G68 MP5, 6 u 8 GB de RAM y pantalla de 2304 × 1440. Corre Android 16 y expone Vulkan 1.3.219 con el driver r38p1. Da ~2970 puntos en 3DMark Wild Life (≈ 18 fps): gama media-baja | [gpuinfo #44864](https://vulkan.gpuinfo.org/displayreport.php?id=44864) |
| frente A §2 | No hay ningún frametime público de Unity 6 en esa GPU. La regresión de URP en Android es un hilo de usuarios sin causa raíz ni arreglo oficial; lo que les funcionó fue `vSyncCount = 0` con `targetFrameRate` explícito, menos post-proceso y menos render scale. 6000.3.28f1 corrige artefactos de Vulkan en Mali | [notas de 6000.3.28f1](https://unity.com/releases/editor/whats-new/6000.3.28f1); [hilo de la regresión](https://discussions.unity.com/t/unity-6-android-urp-baseline-frametime-50ms-20-fps/1566399) |
| frente A §3 | En Mali el coste lo domina el overdraw con blending, que anula la eliminación de superficies ocultas, y después las pasadas a pantalla completa. Los algoritmos fragment rinden más que los compute, y el driver limita a 512 invocaciones por workgroup. La tablet soporta R8, R16 y RG8 como attachment y como storage. Hay un crash de framebuffer fetch reportado en Mali con GLES3 | [Arm GPU Best Practices 4.0](https://documentation-service.arm.com/static/6ae1e4b40fefe512f5edb01a?token=); [gpuinfo #44864](https://vulkan.gpuinfo.org/displayreport.php?id=44864) |
| frente A §4 | No hay benchmarks publicados de A\* ni de Dijkstra sobre grillas de millones de celdas con IL2CPP en ARM64; el dato más cercano es A\* con Burst en 1000 × 1000 en "50+ ms". Burst no compila una librería netstandard2.1 tal cual. Los tiempos y la memoria que da la guía son inferencia. Propone el protocolo de medición de abajo | [hilo de A\* con Burst](https://discussions.unity.com/t/getting-better-performance-out-of-astar-using-burst/816531); [Burst 1.8, tipos de C#](https://docs.unity3d.com/Packages/com.unity.burst@1.8/manual/csharp-type-support.html) |
| frente B §6 | HiRISE, CTX y MOLA usan latitud planetocéntrica y longitud este. Los COG de USGS van en ±180 sobre la esfera de R = 3 396 190 m; el PDS de MOLA, en 0–360; los IDs de CTX, en °W. El asset `dem` de CTX está sobre la esfera y los demás sobre el areoide: ~570 m de diferencia en Gale. El CRS se lee del propio COG. La fecha de un DTM HiRISE es el `START_TIME` de sus RDR fuente | [ítem STAC de HiRISE](https://stac.astrogeology.usgs.gov/api/collections/mro_hirise_socet_dtms/items/DTEEC_019698_1750_019988_1750_U01); [provenance CTX](https://astrogeo-ard.s3-us-west-2.amazonaws.com/mars/mro/ctx/controlled/usgs/T01_000815_1749_XN_05S222W__P22_009716_1773_XI_02S223W/provenance.txt); [DTM CTX de USGS](https://stac.astrogeology.usgs.gov/docs/data/mars/ctxdtms/) |
| frente B §7 | No hay un producto de rocas programático a escala hiperlocal para Gale ni para Jezero, y HiRISE solo resuelve rocas de ≥ 1,5 m. La referencia citable para el umbral de obstáculo de una persona a pie son los parámetros de senderos del USFS: 24" (0,61 m) en clase 1 y 14" (0,36 m) en clase 2 | [USFS, copia de terceros](https://santabarbaratrails.org/docs/franklin/National-Forest-System-Trails-Design-Parameters_05_01_2011.pdf); [Golombek 2010](https://marsoweb.nas.nasa.gov/landingsites/msl/workshops/4th_workshop/talks/Golombek_Rocks_4th_Wkshp.pdf) |
| frente B §8 | A 0,4 g, en vuelo parabólico, la velocidad óptima de caminata es ~2,5 km/h, que coincide con el escalado de Froude (1,11 · √0,38 = 0,68 m/s). La transición a correr, en Fr ≈ 0,5, da un techo de ~1,3 m/s. No hay ninguna medición de b en gravedad reducida. De ahí v₀ P50 0,70, P90 0,56 y P10 0,94 m/s, y b 3,0 con sensibilidad 2,5–3,5 | [Cavagna 2000](https://pmc.ncbi.nlm.nih.gov/articles/PMC2270143); [Kram 1997](https://pubmed.ncbi.nlm.nih.gov/9076966/) |
| frente B §11 | La última release de RAD en PPI es la 42, con un archivo por sol y `TOT_DOSE_B` y `TOT_DOSE_E` en µGy/h, legibles por rangos HTTP. Las 5 observaciones leídas del sol 4710 dieron 6,33–6,69 µGy/h en B y 5,93–6,15 en E. DONKI responde sin key en CCMC. Para Marte importan las simulaciones WSA-ENLIL con impacto en "Mars" (descenso Forbush de ~5 %, hasta ~19 %) y las SEP (×2 en superficie en septiembre de 2017), que DONKI mide en la Tierra o en STEREO-A, nunca en Marte | [ERRATA RAD](https://pds-ppi.igpp.ucla.edu/data/MSL-M-RAD-3-RDR-V1.0/ERRATA.TXT); [CCMC DONKI](https://ccmc.gsfc.nasa.gov/tools/DONKI/); [Guo et al.](https://arxiv.org/pdf/1712.06885) |
| frente B §13 | GDAL es MIT, rasterio BSD-3-Clause y PROJ de estilo MIT. Ninguna obliga al proyecto: el pipeline corre en el portátil y no se distribuye en la tablet | [GDAL](https://raw.githubusercontent.com/OSGeo/gdal/master/LICENSE.TXT); [rasterio](https://raw.githubusercontent.com/rasterio/rasterio/main/LICENSE.txt); [PROJ](https://raw.githubusercontent.com/OSGeo/PROJ/master/COPYING) |
| frente C §14 | `gilzoide/unity-sqlite-net` 1.3.3 trae el binario de SQLite para Android ARM64. GeoPackage no sirve de caché en la tablet: sus blobs de tile deben ser PNG, JPEG o WebP, y escribir con índice espacial exige funciones `ST_*`. La caché son tiles como archivos más un SQLite pequeño | [gilzoide/unity-sqlite-net](https://github.com/gilzoide/unity-sqlite-net); [GeoPackage spec](https://www.geopackage.org/spec/) |
| frente C §15 | El bundle es un tar sin comprimir con un manifest JSON que lista cada archivo con su tamaño y su SHA-256, inspirado en BagIt sin adoptarlo entero. La cola es una carpeta spool en el mismo disco: Tierra escribe `.partial`, hace `fsync` y renombra de forma atómica; un bucle del hábitat importa los bundles cuyo `deliver_at` ya pasó y anota el `bundle_id` para ser idempotente | [RFC 8493](https://www.rfc-editor.org/rfc/rfc8493.html); [os.replace](https://docs.python.org/3/library/os.html#os.replace) |
| frente C §16 | `apply()` tiene una sola implementación, en C#: el hábitat solo guarda y fusiona el log, con reglas sobre metadatos, y la tablet aplica. La equivalencia entre dispositivos se sostiene con aritmética entera y vectores golden. Es una recomendación de diseño, no un hecho medido | [Clang, `-ffp-contract`](https://clang.llvm.org/docs/UsersManual.html#cmdoption-ffp-contract) |
| frente D §18 | Cesium for Unity funciona en Android ARM64 con Unity 6 y es Apache-2.0, pero los ToS de Cesium ion prohíben usar su contenido sin conexión. Cesium Mars queda fuera de la tablet | [Cesium ToS](https://cesium.com/legal/terms-of-service/) |
| frente D §19 | En Unity 6.3 WebGPU es experimental, así que un build Web va con WebGL2, y en Web no hay hilos de C# gestionados | [Unity 6.3, WebGPU](https://docs.unity.com/en-us/engine/6000.3/manual/platform-specific/webgl/develop/graphics/web-gpu) |

El protocolo de medición de frente A §4, del que salen los umbrales de E5
([Reparto del trabajo](./reparto.md), sección "P3: motor de rutas y ediciones"):

1. Grillas sintéticas con semilla fija, generadas por la propia librería: 1000², 2000² y 4000²,
   en cinco variantes (costo uniforme, ruido fractal, muro con hueco, laberinto y 30 % de celdas
   bloqueadas).
2. Métricas: mediana y p95 de 20 corridas tras 3 de calentamiento, con `Stopwatch`; celdas
   expandidas, bytes asignados y recolecciones del GC.
3. Entornos: `dotnet` en el portátil como línea base, y un APK IL2CPP ARM64 Release en la tablet,
   con una escena vacía que llama a la librería desde un botón y escribe los tiempos en pantalla
   y en logcat; lo mismo en un hilo de fondo.
4. Umbrales: tramo típico de 500 m con p95 ≤ 100 ms y campo completo ≤ 2 s en un hilo de fondo.
   Son criterios de decisión *(inferencia)*, no mediciones.

## URL de sondeo de H0

*(propuesta)*

Son las que P5 sondea en H0, con P1 ([Reparto del trabajo](./reparto.md), sección "Checklist del
equipo"). Cada respuesta JSON se guarda en `ingest/cache/h0/`
([Convenciones](./convenciones.md), sección "Repositorio y flujo de trabajo"). Las de DONKI y RAD
son también las de **E14**. Las fechas de los ejemplos se cambian por una ventana de ≤ 60 días
que acabe el día del sondeo.

| Fuente | URL | Qué se espera |
|---|---|---|
| STAC de USGS | `https://stac.astrogeology.usgs.gov/api/collections` | 200, con las colecciones de Marte |
| STAC, DTM CTX de Gale | `https://stac.astrogeology.usgs.gov/api/search?collections=mro_ctx_controlled_usgs_dtms&bbox=137.37,-4.85,137.41,-4.80&limit=3` | 200, con ítems que traen `geoid_adjusted_dem` |
| STAC, DTM HiRISE de Gale | `https://stac.astrogeology.usgs.gov/api/collections/mro_hirise_socet_dtms/items/DTEEC_019698_1750_019988_1750_U01` | 200, con el asset `dtm` |
| ODE, DTM HiRISE de Gale | `https://oderest.rsl.wustl.edu/live2/?query=product&results=m&output=JSON&target=mars&ihid=MRO&iid=HIRISE&pt=DTM&westernlon=137.37&easternlon=137.41&minlat=-4.85&maxlat=-4.80` | 200, con productos: es el plan B de STAC |
| ODE, fecha de un RDR | `https://oderest.rsl.wustl.edu/live2/?query=product&results=m&output=JSON&target=mars&ihid=MRO&iid=HIRISE&pt=RDRV11&productid=ESP_019988_1750_RED` | 200, con su `START_TIME` |
| Trek WMTS | `https://trek.nasa.gov/tiles/Mars/EQ/Mars_MGS_MOLA_ClrShade_merge_global_463m/1.0.0/WMTSCapabilities.xml` | 200, XML de capacidades |
| Feed raw de Curiosity | `https://mars.nasa.gov/api/v1/raw_image_items/?order=sol%20desc&per_page=1&page=0&condition_1=msl:mission` | 200, con el último ítem |
| Feed raw de Perseverance | `https://mars.nasa.gov/rss/api/?feed=raw_images&category=mars2020&feedtype=json&num=1` | 200, con el último ítem |
| Waypoints de Curiosity | `https://mars.nasa.gov/mmgis-maps/MSL/Layers/json/MSL_waypoints_current.json` | 200, GeoJSON |
| Waypoints de Perseverance | `https://mars.nasa.gov/mmgis-maps/M20/Layers/json/M20_waypoints_current.json` | 200, GeoJSON |
| DONKI, WSA-ENLIL | `https://ccmc.gsfc.nasa.gov/DONKI/WS/get/WSAEnlilSimulations?startDate=2026-09-14&endDate=2026-11-12` | 200 sin key |
| DONKI, SEP | `https://ccmc.gsfc.nasa.gov/DONKI/WS/get/SEP?startDate=2026-09-14&endDate=2026-11-12` | 200 sin key; puede venir vacío |
| DONKI, FLR | `https://ccmc.gsfc.nasa.gov/DONKI/WS/get/FLR?startDate=2026-09-14&endDate=2026-11-12` | 200 sin key |
| RAD, listado de la release | `https://pds-ppi.igpp.ucla.edu/data/MSL-M-RAD-3-RDR-V1.0/DATA/SOL_04710_04843/` | 200, con un `.TXT` y un `.LBL` por sol |
| RAD, etiqueta de un sol | `https://pds-ppi.igpp.ucla.edu/data/MSL-M-RAD-3-RDR-V1.0/DATA/SOL_04710_04843/RAD_RDR_2025_309_01_42_4710_V00.LBL` | 200; da los offsets de `TOT_DOSE_B` y `TOT_DOSE_E` |
| RAD, datos de un sol | `https://pds-ppi.igpp.ucla.edu/data/MSL-M-RAD-3-RDR-V1.0/DATA/SOL_04710_04843/RAD_RDR_2025_309_01_42_4710_V00.TXT` | 206 a una petición con cabecera `Range` |

Si una URL falla en H0, P5 lo anota en el PR de la caché y avisa a P1, que decide con el plan B
de su experimento o con el JSON cacheado.
