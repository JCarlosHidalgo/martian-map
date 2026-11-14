# Tablet

La tablet es la app que usa el astronauta. Es offline-first: dibuja el terreno desde su caché,
calcula las rutas y aplica las ediciones sin depender del hábitat ni de la Tierra. Sus cinco
componentes (`Routing`, `Edits`, `Terrain`, `FX`, `HUD`) y su lugar entre los otros ambientes están
en [Los ambientes](./ambientes.md).

Los experimentos que esta página nombra (E1 a E9) están en
[Reparto del trabajo](./reparto.md), con su criterio y su plan B.

## Motor y ficha del dispositivo (D1)

**Unity 6.3 LTS + URP, build nativo Android.** La tablet de la demo es una **Samsung Galaxy Tab S9
FE**. No hay iPad.

| Ficha | Valor |
|---|---|
| SoC | Exynos 1380 |
| GPU | Mali-G68 MP5 |
| Vulkan | 1.3.219, driver r38p1 |
| Sistema | Android 16 |
| Referencia de rendimiento | 3DMark Wild Life ≈ 2970, 18 fps (gama media-baja) |

Fuentes de la ficha: [gpuinfo #44864](https://vulkan.gpuinfo.org/displayreport.php?id=44864),
frente A §1–2.

Por qué:

- En Unity, el C# móvil va por IL2CPP (la ruta estándar), lo que deja la librería de rutas como C#
  portable, probado fuera del motor.
- Unity Personal es gratis (< USD 200 k).
- Con una Mali de esa gama, la guía de referencia es la de Arm; con 3,3 Mpx nativos hay que
  trabajar con render scale < 1 *(inferencia)*.
- **La versión es Unity 6000.3.28f1 exacta**: corrige artefactos Vulkan en Mali (UUM-147578) y
  retroporta arreglos de Swappy
  ([notas 6000.3.28f1](https://unity.com/releases/editor/whats-new/6000.3.28f1), S4). Es el último
  parche 6.3 a 2026-11-12 y no se sube de parche durante las 48 h.

Cómo:

- App nativa offline. Vulkan principal + GLES3 de respaldo.
- `vSyncCount = 0` + `targetFrameRate` explícito.
- Perfil URP de arranque *(inferencia, se ajusta en E1)*: render scale 0,6–0,75 con UI nativa, MSAA
  2x o FXAA, HDR solo si el bloom lo exige y a 32 bit, bloom a ½, sin sombras en tiempo real.

Factibilidad en 48 h: alta (≥2 personas saben Unity).

Pendientes y riesgos:

- No hay frametimes públicos de Unity 6 en esta GPU (**NO VERIFICADO** → **E1**).
- La regresión de URP en Android no tiene causa raíz ni arreglo oficial; el síntoma apunta a pacing
  ([hilo](https://discussions.unity.com/t/unity-6-android-urp-baseline-frametime-50ms-20-fps/1566399)).
- La variante de RAM (6/8 GB) y la versión de Android de la unidad real → **E2**.

### Build Web

Hay un build Web solo si el núcleo Android está listo en H36, y con un tope de 2–3 h: WebGL2
single-thread con un snapshot estático. WebGPU es experimental en 6.3 y no hay hilos C# gestionados en Web
([Unity 6.3 WebGPU](https://docs.unity.com/en-us/engine/6000.3/manual/platform-specific/webgl/develop/graphics/web-gpu),
frente D §19).

## Mundo y tiles (D2)

**Cada zona es un mundo ENU/equirectangular local, con su origen en `zone.json`. El terreno son
tiles 257² `uint16` con offset y escala por tile, borde compartido y skirts.**

- El heightmap regular comparte celdas con el costo y con las ediciones (U3).
- float32 en coordenadas planetocéntricas da pasos de ~0,25 m; a 10 km de un origen local, ~1 mm.
- `uint16` con escala por tile cuantiza < 1 cm.
- `zone.json` lleva el CRS IAU_2015:49900, la lat/lon del origen y la proyección; sus campos están
  en [Tierra](./tierra.md), sección "Tiles (C1)".
- El LOD es un quadtree propio; CDLOD es una mejora.

El formato del tile es C1, en [Tierra](./tierra.md), sección "Tiles (C1)".

**Vista de contexto global:** una esfera MOLA propia de baja resolución (opcional). Cesium Mars
solo se usa con conexión y **fuera del build de la tablet**: los ToS de ion §2.2.2 prohíben copiar
o almacenar su contenido para uso offline; el plugin es Apache-2.0
([Cesium ToS](https://cesium.com/legal/terms-of-service/), S11; frente D §18).

Factibilidad en 48 h: media-alta (los skirts son ≈ ½ día).

Pendientes y riesgos:

- Bugs de costura y de LOD.
- Cesium 1.26.1 en Unity 6.3 es **NO VERIFICADO**, e irrelevante: el terreno no depende de Cesium.
  Si se usa en el video, el logo "Cesium ion" va visible.

## Caché local

La tablet guarda **los tiles C1 como archivos** en `persistentDataPath/zones/…` y **el catálogo y
el log de ediciones en un SQLite local** (`gilzoide/unity-sqlite-net` 1.3.3).

- Un tile se vuelve a descargar cuando su `?v=` difiere del cacheado, y se verifica su `sha256`.
- Al recibir `changes`, `Terrain` llama a `apply()` y refresca `_HazardTex`.
- `apply()` trabaja sobre los tiles de `z = 0`. Para que una edición se vea de lejos, `Terrain`
  recalcula el `_HazardTex` de los tiles de `z > 0` que la cubren, con la regla de reducción de
  [Convenciones](./convenciones.md), sección "Datos (C0)" *(equipo)*.
- **Solo `Terrain` habla HTTP con el hábitat.** `HUD` y `Routing` reciben de `Terrain` lo que
  necesitan (sección "Orquestación").
- Las imágenes POV se guardan en `persistentDataPath/zones/{zone}/pov/`, con la misma regla de
  `?v=` y `sha256`.
- `gilzoide/unity-sqlite-net` 1.3.3 no tiene ningún reporte con Unity 6000.3 → **E8**.

Los endpoints que alimentan la caché son C3, en [Hábitat](./habitat.md), sección "Los endpoints
(C3)".

### Gale precargada

*(equipo)*

**Gale viaja dentro del APK**, en `tablet/Assets/StreamingAssets/zones/gale/`.

- El contenido es el bundle `zone_full` de Gale, sin empaquetar: `bundle.json`, que trae el
  `zone_version`, y `data/` con `zone.json`, `conditions.json`, `tiles/` y `pov/`.
- **P1 lo entrega y P4 lo mete en el build.** P1 lo sube con un PR a esa carpeta, con los binarios
  en LFS, en el hito H24 ([Reparto del trabajo](./reparto.md), sección "Hitos"). Hasta entonces el
  APK lleva la zona `synth` de `contracts/fixtures/` *(equipo)*.
- En el primer arranque, `Terrain` copia la zona a `persistentDataPath/zones/gale/` y la anota en
  el catálogo con su `zone_version`. En Android, StreamingAssets está dentro del APK: se lee con
  `UnityWebRequest`, no con `System.IO`.
- Es el mismo bundle que Tierra deja en el spool del hábitat, con las mismas versiones y los
  mismos `sha256`: al conectarse, la tablet no vuelve a descargar nada de Gale.
- Tamaño: ~100 MB para 3×3 km *(inferencia: 144 tiles de `z = 0` a ~0,5 MB, más los niveles
  superiores)*.
- Jezero no va en el APK: llega por el hábitat.

## Costo de caminata (D6)

**El costo de una ruta es solo tiempo.** La radiación es una **restricción** (un límite de
duración), no un costo.

| Parámetro | Valor | Fuente |
|---|---|---|
| Función | Tobler simétrica (c = 0) | |
| v₀ | **P50 0,70 / P90 0,56 / P10 0,94 m/s** | medición a 0,4 g en vuelo parabólico (≈ 2,5 km/h, n = 3), que coincide con Froude (1,11·√0,38 = 0,68): [Cavagna 2000](https://pmc.ncbi.nlm.nih.gov/articles/PMC2270143) |
| b | 3,0 (sensibilidad 2,5–3,5) | **sin ningún dato en gravedad reducida (NO VERIFICADO)** |
| Techo de caminata | ~1,3 m/s: es una cota de v₀, no un tope de la fórmula; `cost_params` es inválido si `v0_mps.p10` la supera *(propuesta)* | Fr ≈ 0,5, [Kram 1997](https://pubmed.ncbi.nlm.nih.gov/9076966/), *cálculo propio* |
| Bloqueo por pendiente | > 15° | MoonPath |
| Bloqueo por relieve local | > 0,61 m | analogía con senderos peatonales USFS clase 1 y 2 ([USFS, copia de terceros](https://santabarbaratrails.org/docs/franklin/National-Forest-System-Trails-Design-Parameters_05_01_2011.pdf)) |
| Penalización de rugosidad | desde 0,36 m | misma analogía USFS |
| Otros bloqueos duros | peligro rojo; fuera de cobertura DTM | |

Fuente común: frente B §7–8.

La fórmula, con los parámetros en `cost_params.json`:

`t(e) = longitud_3D / [v₀·exp(−b·|s + c|)·f_rug·f_terreno] + penalización por peligro ámbar`

- **`c` = 0 da la Tobler simétrica** *(propuesta)*. `c` desplaza la pendiente óptima, como en la
  Tobler original (c = 0,05); se deja en `cost_params.json` para la sensibilidad. Cada factor es
  ≤ 1, así que la velocidad nunca pasa de v₀: el techo de caminata no actúa en la fórmula.

- **Un tramo es un segmento recto entre dos nodos**, sea una arista de A\* (cualquiera de los 16
  movimientos) o un atajo del suavizado, y todos siguen la misma regla *(propuesta)*:
  - Cada nodo es dueño de su **celda**, el cuadrado cerrado de lado `cell_m` centrado en él. El
    segmento se corta exactamente donde cruza el borde de una celda, y cada pieza toma el
    `roughness_cm` y el `hazard` del nodo de su celda.
  - **El segmento está bloqueado si toca una celda bloqueada** (R4). Pasar justo por una esquina
    toca las cuatro celdas de esa esquina: una diagonal no corta la esquina de un nodo bloqueado,
    y un salto (2, 1) no cruza una pared de un nodo de ancho.
  - Entre nodos, la altura es la **interpolación bilineal** de `height_m`. El segmento se corta
    también donde cruza una línea de nodos, donde la interpolación se dobla.
  - **`s` es la pendiente de cada pieza**: su desnivel dividido por su longitud horizontal, sin
    unidad. No es el raster `slope`, que solo decide el bloqueo por pendiente. `longitud_3D` es la
    raíz de la longitud horizontal al cuadrado más el desnivel al cuadrado. La penalización y
    `amber_m` cuentan los metros de las piezas en celda ámbar.
  - Sobre una fila o una columna esto es media arista por nodo con la pendiente de la arista
    entera. Un salto (2, 1) da un cuarto a cada uno de los cuatro nodos que toca, y una diagonal
    da la mitad a cada extremo.
- La ETA del plan usa **P50**. `ReturnField` y el punto de no-retorno usan **P90**.
- `f_terreno` no se aplica dos veces: Cavagna midió en plataforma lisa.
- **La fórmula tiene una sola implementación**, en `Routing`. Ningún otro ambiente calcula ni
  guarda costo *(equipo)*: Tierra no produce un raster de costo, y la validación de la fórmula
  son los goldens de la sección "Contrato de rutas (C4)".

Factibilidad en 48 h: alta.

Límites que se declaran:

- No existe un modelo de caminata validado a 0,38 g. **b se presenta como sensibilidad.**
- P10 es una predicción (Cavagna 1998), no una medición.
- Los umbrales USFS son una analogía terrestre declarada.
- El bloqueo es de rugosidad, no de roca: HiRISE no resuelve rocas < 1,5 m
  ([Golombek 2010](https://marsoweb.nas.nasa.gov/landingsites/msl/workshops/4th_workshop/talks/Golombek_Rocks_4th_Wkshp.pdf)),
  y bajo ~0,3 m el DTM es ruido.

## Rutas (D7)

| Qué | Algoritmo |
|---|---|
| Tramo | **A\* 16-conexo + suavizado**: string-pulling que solo acepta un atajo si no empeora el costo integrado, que es el de la regla de los tramos aplicada al atajo entero *(propuesta)* |
| Regreso | **campo Dijkstra inverso al hábitat**: da radio de regreso, punto de no-retorno y heatmap en una corrida |
| Alternativas | 3, por suma ponderada |
| Orden de paradas | **Held-Karp (k ≤ 12) o greedy + 2-opt**, en modos TSP con depósito y orienteering |
| Opcionales | HPA\*, D\* Lite y OR-Tools |

- Theta\* básico no garantiza el óptimo y cruza zonas de peligro si se implementa mal (contra R4).
- **El heap binario es propio y obligatorio**, con decrease-key por `heapIndex[celda]`:
  `PriorityQueue<TElement,TPriority>` solo existe en .NET 6+, y Unity 6.3 solo ofrece .NET Standard
  2.1 y .NET Framework 4.8
  ([Microsoft Learn](https://learn.microsoft.com/en-us/dotnet/api/system.collections.generic.priorityqueue-2?view=net-9.0),
  [Unity .NET profiles](https://docs.unity.com/en-us/engine/6000.3/manual/programming-environment/overview-of-dot-net-in-unity/dotnet-profile-support),
  S6). **E6** lo comprobó: `PriorityQueue` no compila en ningún perfil de API de Unity ni en
  netstandard2.1.

Cómo:

- Librería C# propia, ~800–1500 líneas, netstandard2.1 y sin `UnityEngine`.
- Arrays planos (SoA), sin `class Node`, sin LINQ.
- Llamadas thread-safe; el campo corre en un hilo de fondo.
- Planificación a 2 m: hasta 4 M de celdas (zona de 4×4 km; la de 3×3 km son 2,25 M).
- Un CI compila la librería **solo** contra netstandard2.1.

Esfuerzo estimado: ~4–6 h el tramo, ~1–2 h el campo, ~3–4 h las paradas. En qué orden se
construye y qué parte mide el benchmark de H5: [Reparto del trabajo](./reparto.md), sección "P3:
motor de rutas y ediciones".

**El rendimiento es NO VERIFICADO**: A\* esquina a esquina 0,3–2 s, campo 1–4 s, memoria 60–90 MB,
todo *inferencia* (frente A §4) → **E5**, **E7**.

Orden de los planes de rendimiento:

1. **Plan A:** C# con IL2CPP y arrays planos, grilla de 2 m.
2. **Plan B:** grilla de 4 m, weighted A\* ε = 1,5 y campo precalculado en el hábitat.
3. **Plan C:** Burst, con un wrapper HPC# aparte. Burst no admite clases ni arrays gestionados y
   no compila la librería netstandard2.1 tal cual
   ([Burst 1.8](https://docs.unity3d.com/Packages/com.unity.burst@1.8/manual/csharp-type-support.html)).

## Contrato de rutas (C4)

C4 tiene tres partes: `cost_params.json`, la API de `Routing` y `Edits.Apply`. Lo que esta sección
marca *(equipo)* ya está decidido: P3 lo transcribe al fijar el contrato.

### `cost_params.json`

| Campo | Valor |
|---|---|
| `schema_version` | `1` |
| `v0_mps` | `{p10:0.94, p50:0.70, p90:0.56}` |
| `b` | `{p50:3.0, low:2.5, high:3.5}` |
| `c` | `0` |
| `walk_ceiling_mps` | `1.3` |
| `max_slope_deg` | `15` |
| `relief_block_cm` | `61` |
| `relief_penalty_from_cm` | `36` |
| `hazard_red_min`, `hazard_amber_min` | `uint8`; valores en [Convenciones](./convenciones.md), sección "Valores fijados" |
| `f_terreno`, `f_rug_min`, `amber_penalty_s_per_m` | `f_terreno`, el suelo de `f_rug` y la penalización ámbar; valores en la misma sección |
| `alt_safest_amber_mult`, `alt_gentlest_s_per_m` | `4` y `10`: los pesos de las alternativas *(equipo)* |

### Tipos

*(equipo)*

`Routing` no lee archivos, red ni JSON: recibe structs y arrays y devuelve resultados. `Terrain`
lee `cost_params.json` y `conditions.json` y se los pasa ya convertidos; la misión la recibe del
`HUD` en `RouteRequested`, ya en estos tipos. Quién lo
llama, en la sección "Orquestación".

**Todos los tipos de C4 son inmutables** *(propuesta)*: el constructor copia lo que recibe, y
nada de lo que exponen es un array. Los arrays por nodo o por muestra (los de `CostGrid` y
`TileRasters`, y `Field.time_s`) se leen como `ReadOnlySpan<T>`; las listas (`cells`,
`polyline_enu_m`, `order`, `dropped`, `legs` y `modifiers`), y lo que devuelven `Alternatives`,
`OrderStops` y `LiveEntities`, como `IReadOnlyList<T>`. Un
`ReadOnlySpan` no se guarda en una lambda ni cruza un `await`: se guarda el objeto y se lee el span
dentro de un método síncrono.

| Tipo | Contenido |
|---|---|
| `TileRasters` | un tile de `z = 0`: `origin_enu_m` (su esquina suroeste, de `tile.json`), `height` (`ushort[]`), `scale`, `offset`, y `slope`, `roughness` y `hazard` (`byte[]`), tal como vienen en C1 |
| `CostGrid` | la grilla de planificación: `width` y `height` en nodos, `cell_m` (2), `origin_enu_m` (el nodo `(0, 0)`, la esquina suroeste) y cuatro arrays planos de `width × height`: `height_m` (`float32`), `slope_deg`, `roughness_cm` y `hazard` (`uint8`). El nodo `(i, j)` está en el índice `j · width + i`; `i` crece al este y `j` al norte |
| punto: `start`, `goal`, `home`, `enu_m` de una parada | `(e, n)` en metros ENU de la zona, `float64`. Tiene que ser finito y caer en la caja de los nodos de la `CostGrid`, bordes incluidos; si no, la llamada lanza *(propuesta de P3)*. `Routing` lo lleva al nodo más cercano, `i = floor((e − origin_e) / cell_m + 0,5)` e ídem `j`: el punto medio entre dos nodos va al de índice mayor, al este o al norte *(propuesta de P3)*; si ese nodo está bloqueado, el resultado es "no hay ruta". Si `start` y `goal` caen en el mismo nodo libre, la ruta se encuentra y se queda en él: `cells` = ese nodo, un punto, y todo lo demás a 0 *(propuesta de P3)* |
| `Route` | `{found (bool), cells[] (índices de nodo), polyline_enu_m[] ([e, n, u]), length_m, ascent_m, amber_m, eta_s{p10,p50,p90}, max_hazard (uint8)}` |
| `Field` | `{grid, home, time_s[]}`: un `float32` por nodo, el tiempo de regreso a `home` con v₀ P90; `+∞` donde no hay regreso. El regreso se elige como la ruta, con la penalización ámbar, y `time_s` guarda su tiempo P90 sin ella, igual que `eta_s` *(propuesta de P3)*. Solo lo crea `ReturnField`, y lleva la `CostGrid` y el `home` con que se calculó *(propuesta)* |
| `Alternative` | un enum: `Fastest`, `Safest`, `Gentlest`, con los valores 0, 1 y 2 *(propuesta)*. Toda lista de `Route` o de `Plan` de alternativas trae siempre las tres, en ese orden, aunque dos coincidan |
| `CostParams` | los campos de `cost_params.json`, uno a uno: es el `params` de todas las llamadas *(propuesta)* |
| `Stop` | `{id, enu_m, science_value, dwell_s}`; `science_value` es un entero de 0 a 10 |
| `Budget` | `{t_s, dose_uGy}` |
| `Conditions` | `{value_uGy_h, modifiers[]{factor, valid_from, valid_to}}`, de `conditions.json`. Toda fecha, aquí y en `t_utc`, es un `DateTime` en UTC *(propuesta)* |
| `Plan` | `{order[] (los `id` en orden de visita), dropped[] (los `id` que no caben; solo en modo OP), legs[] (una `Route` por tramo, con el regreso a `home`), total_s{p10,p50,p90} (caminata más `dwell_s`), dose_uGy, limit_s, return_ok (bool)}` |

- `height_m` y la `u` de `polyline_enu_m` son altura sobre el areoide menos `origin_height_m`: la
  Y de la escena.
- `cells`, `polyline_enu_m`, `length_m`, `ascent_m`, `amber_m` y `max_hazard` salen del mismo
  recorrido que el costo, la regla de los tramos de la sección "Costo de caminata (D6)", aplicado a la ruta ya
  suavizada *(propuesta)*:
  - `cells` son los nodos cuya celda atraviesa la ruta, en orden y sin repetir seguidos. No son
    los nodos de A\* ni los waypoints del suavizado.
  - `polyline_enu_m` lleva el inicio, cada cruce con una línea de nodos, cada waypoint y el
    final, con la altura bilineal. Entre dos líneas de nodos la bilineal no se dobla, así que
    esa es la resolución de la grilla; el espaciado máximo es `cell_m · √2`. Sobre una fila, un
    punto por nodo.
  - `max_hazard` es el máximo `hazard` de `cells`.
- `ascent_m` es el desnivel acumulado, subido más bajado. `amber_m` son los metros recorridos en
  celda ámbar.
- `eta_s` nunca incluye penalizaciones ([Convenciones](./convenciones.md), sección "Valores
  fijados").
- `Conditions` lanza si un modificador es `default(ConditionModifier)`: se saltó las
  comprobaciones de su constructor *(propuesta)*.

### API de `Routing`

| Llamada | Devuelve |
|---|---|
| `BuildGrid(tiles, origin_height_m, params)` *(equipo)* | la `CostGrid`, armada desde los `TileRasters`, ya con las ediciones aplicadas |
| `PlanRoute(grid, start, goal, params)` | `Route` |
| `ReturnField(grid, home, params)` | `Field` (usa P90) |
| `Alternatives(grid, start, goal, params)` | `IReadOnlyList<Route>`, una por alternativa, en el orden de `Alternative` |
| `DurationLimit(conditions, t_utc, dose_uGy)` *(equipo)* | `limit_s`: el límite de duración por radiación |
| `OrderStops(grid, start, stops, home_field, budget, conditions, t_utc, mode, params)` | `IReadOnlyList<Plan>`, una por alternativa, en el orden de `Alternative`; `mode` es TSP u OP. `home_field` es el `Field` de `ReturnField`, el mismo del heatmap; lanza si se calculó sobre otra `CostGrid`. La ruta sale de `start` y termina en `home_field.home` *(propuesta)* |
| `Benchmark.Run(log)` *(equipo)* | el texto de `Routing/BENCHMARK.md`: genera las grillas con semilla de E5, corre sus casos y devuelve la tabla de resultados. `log` recibe cada línea según sale |

- Las llamadas son thread-safe y reentrantes.
- **`BuildGrid`** *(equipo)*: el nodo `(i, j)` está en `origin_enu_m + (2i, 2j)` metros. En el
  tile de `z = 0` que lo contiene es la muestra de la columna `c = 2i` y la fila `r = 256 − 2j`,
  con `i` y `j` contados desde la esquina suroeste de ese tile: en C0 la fila 0 es el borde
  norte. `height_m` toma esa muestra; `slope_deg`, `roughness_cm` y `hazard` toman el máximo de
  las 3 × 3 muestras centradas en ella, que es el lado seguro (R4). Un nodo sobre una muestra
  `nodata` lleva `height_m` = 0 y `hazard` = 255 *(propuesta)*. Los bordes *(propuesta)*:
  - La grilla cubre la caja de los tiles recibidos: su `origin_enu_m` es la esquina suroeste de
    la caja, y tiene `128 · nₑ + 1` × `128 · nₙ + 1` nodos. `BuildGrid` lanza si un tile se
    repite o si su `origin_enu_m` no cae en la retícula de 256 m de los demás.
  - Un nodo que no cae en ningún tile (una zona en L, un tile sin cargar) es `nodata`: fuera de
    cobertura DTM.
  - Cada muestra de la ventana 3 × 3 se lee del tile que la tiene: dos tiles vecinos comparten
    sus muestras y sus nodos de borde. Una muestra que no está en ningún tile se ignora, no
    cuenta como `nodata`: en el perímetro de la grilla no hay terreno al otro lado.
  - Una muestra compartida que dos tiles traen distinta: `slope`, `roughness` y `hazard` toman
    el máximo; `height` es `nodata` si lo es en alguno y, si no, la del tile más al suroeste
    (menor `n`, luego menor `e`). El resultado no depende del orden de `tiles`.
  - `BuildGrid` lanza *(propuesta de P3)*: `ArgumentNullException` con `tiles`, un tile o `params`
    nulos; `ArgumentException` con `tiles` vacío, `origin_height_m` o el origen de un tile no
    finitos, un tile repetido, un origen fuera de la retícula de 256 m, o una caja con más nodos de
    los que cuenta un `int`. `params` no cambia la grilla: guarda los rasters sin aplicar el costo.
- **`Benchmark.Run`** *(equipo)* es C# puro, dentro del paquete `Routing` y no en `Tests~/`, para
  que lo llamen igual `dotnet` en el portátil y la escena de benchmark de la tablet. Entra en la
  firma de H0:45, con el cuerpo vacío, y P3 lo rellena en H2–4, con el núcleo. Se corre solo en
  el portátil, con `dotnet run -c Release --project "Routing/Tests~/BenchmarkRunner"`: la tablet no
  se prueba *(decisión de P3, 2026-11-14)*. Corre, de menor a mayor tamaño, los 5 terrenos en 1000²
  y 2000²: tramos de A\* de 500 m con 1 corrida de calentamiento y 5 medidas, y el campo con 1 + 1.
  La memoria que da es el heap gestionado, con la grilla y la memoria de trabajo de la búsqueda
  vivas (`GC.GetTotalMemory(true)`) *(propuesta de P3)*. Quién escribe la
  escena y el APK: [Reparto del trabajo](./reparto.md), sección "P3: motor de rutas y ediciones".
- **`DurationLimit`** *(equipo)*: `limit_s` es el primer `t` en que la dosis absorbida desde
  `t_utc` llega a `dose_uGy`; con una tasa constante, `dose_uGy / tasa · 3600`. La tasa es
  constante a trozos: en cada instante, `value` por el producto de los `factor` vigentes, contando
  como 1 todo factor < 1 (la regla Forbush de [Tierra](./tierra.md), sección "Condiciones (C6)").
  Un modificador vale en `[valid_from, valid_to)`, y cuenta aunque empiece o acabe a mitad de la
  EVA: un `SEP` que empieza una hora después de salir acorta el límite *(propuesta)*.
- **`t_utc` es la hora de salida de la EVA**: la hora UTC a la que `Terrain` procesa
  `RouteRequested` *(propuesta de P3)*. Si algún día `mission.json` trae una hora de salida,
  manda esa, y es un campo nuevo de C6.
- **`OrderStops`** *(equipo)* planifica contra `min(budget.t_s, limit_s)`, con
  `limit_s = DurationLimit(conditions, t_utc, budget.dose_uGy)`: la tasa y el límite salen del
  mismo `t_utc`. En TSP visita todas las paradas; en OP maximiza la suma de `science_value` y deja
  el resto en `dropped`. El orden se decide con la alternativa más rápida y es el mismo en las tres
  `Plan`. En TSP, una parada a la que no se llega sigue en `order`: su tramo es un `Route` con
  `found` falso, `total_s` es +∞ y `return_ok` es falso *(propuesta de P3)*.
- `Plan.dose_uGy` es la integral de esa misma tasa de `t_utc` a `t_utc + total_s.p50` *(propuesta)*. `return_ok` es falso si,
  al salir de alguna parada, lo transcurrido a P90 (la caminata P90 acumulada más los `dwell_s`)
  más el regreso a `home` con P90 del `home_field` supera `min(budget.t_s, limit_s)`; la igualdad
  cabe *(propuesta)*. Es el punto de no-retorno: si todo va a P90, ¿se vuelve?

Lo que lanza `ArgumentException` en `Routing` (un argumento inválido,
[Convenciones](./convenciones.md), sección "Logs y errores") *(propuesta de P3)*:

- **Cualquier número que no es finito** (NaN o ±∞): en los puntos, en `TileRasters`, `CostGrid`
  (`cell_m`, `origin_enu_m` y cada `height_m`), `Conditions`, `Stop`, `Budget` y
  `DurationLimit`. `nodata` es `height_m` = 0, nunca NaN. Así no entra ningún NaN en el cálculo,
  y el único +∞ que sale significa "no hay camino": `Field.time_s` donde no hay regreso y el
  `total_s` de un plan con una parada inalcanzable.
- **Un punto fuera de la grilla** (`ArgumentOutOfRangeException`), también una parada en modo OP.
- **Una fecha que no es UTC**: `ConditionModifier`, `DurationLimit` y `OrderStops`.
  `DateTime.Parse` devuelve hora local; se lee con `DateTimeStyles.AdjustToUniversal`.
- `ConditionModifier`: `factor` ≤ 0, o `valid_from` ≥ `valid_to`. `Conditions`: `value` ≤ 0, o un
  `default(ConditionModifier)`, que se saltó su constructor.
- `Budget`: `t_s` o `dose_uGy` < 0. `Stop`: un `id` vacío, `science_value` fuera de 0…10, o
  `dwell_s` < 0. `DurationLimit`: `dose_uGy` < 0.
- `TileRasters` y `CostGrid`: arrays nulos o de otra longitud, y `scale` o `cell_m` ≤ 0.
  `Route`: `found` falso con algo que no está vacío o a 0. `Plan`: una lista nula o con un elemento nulo.
- `CostParams`: los rangos de `cost_params.schema.json` (`b` > 0, 0 ≤ `max_slope_deg` ≤ 90,
  `f_terreno` y `f_rug_min` en (0, 1]…), más lo que el esquema no puede comparar:
  `v0.p90` ≤ `p50` ≤ `p10` ≤ `walk_ceiling_mps`, `relief_penalty_from_cm` < `relief_block_cm` y
  `hazard_amber_min` < `hazard_red_min`.
- `OrderStops`: un `home_field` de otra `CostGrid`, unas `conditions` nulas, o un `stops` nulo o con una parada nula.

Las tres alternativas *(equipo)*:

| Alternativa | Qué minimiza |
|---|---|
| `fastest`, más rápida | la fórmula del costo, tal cual |
| `safest`, más segura | la fórmula, con la penalización ámbar multiplicada por `alt_safest_amber_mult` |
| `gentlest`, menos desnivel | la fórmula, más `alt_gentlest_s_per_m` segundos por metro de desnivel |

La tercera mide desnivel y no esfuerzo: el costo metabólico está fuera de alcance
([Contexto del reto](./contexto.md), sección "Fuera de alcance"). Dos alternativas pueden dar la
misma ruta.

### `Edits.Apply`

`Apply(baseHazard, origin_enu_m, edits[])` *(equipo)* devuelve el `hazard` de un tile de `z = 0`
con las ediciones aplicadas. `origin_enu_m` es la esquina suroeste del tile, de `tile.json`: sitúa
los vértices, que van relativos al origen de la zona. Se pasa en metros enteros *(equipo)*:
`extent_enu_m` empieza en un metro entero, así que ningún float entra en `apply()`. `baseHazard` es el `byte[]` de C1 y `edits[]` son las ediciones ya
leídas, que `Terrain` convierte desde el log. `Edits` no usa ningún tipo de `Routing`: los dos
ensamblados no se referencian.

`LiveEntities(edits[])` *(equipo)* devuelve las entidades vivas de un log: por cada
`entity_id`, su ganadora, con `entity_id`, `op`, `creator`, `level` y `payload`. Aplica las mismas
reglas que `Apply`, en el mismo recorrido, así que la regla de merge tiene una sola implementación
en C#. `Terrain` la llama para emitir `EntitiesChanged`; no calcula ganadoras por su cuenta.

Cómo se fusiona y qué lanza *(propuesta de P3)*:

- Las ediciones se recorren en orden `(lamport, author, uuid)`, con `author` y `uuid` comparados
  byte a byte; por cada `entity_id` queda una ganadora, y cada edición la reemplaza (LWW) salvo
  que la regla la ignore entera.
- Una edición se ignora si la entidad está viva y la edición la baja (un tombstone, o un nivel
  menor en `hazard.set`/`obstacle.set`), su `author` no es su `creator`, su `confirmed` no es
  verdadero y no es `from_habitat`. `note.set` siempre es LWW.
- Una entidad borrada no tiene nivel: la edición siguiente del mismo `op` la recrea, la escriba
  quien la escriba.
- Una edición con otro `op` que la ganadora vigente de su entidad se ignora, también si la
  ganadora es un tombstone: una entidad no cambia de `op`.
- `LiveEntities` devuelve las ganadoras sin tombstone en el orden `(lamport, author, uuid)` de la
  ganadora, el de `log_merged.json`; `creator` es el de la ganadora y `level` el de su `payload`,
  255 o nulo.
- `Apply` copia `baseHazard` y, por cada entidad viva con polígono, escribe `max(muestra, level)`
  en las muestras de dentro. La muestra `(r, c)` está en `(256 · (origin.E + c),
  256 · (origin.N + 256 − r))`, en punto fijo; dentro es en el borde (vértices incluidos) o en el
  interior por par-impar, con productos exactos de 128 bits, así que ningún vértice `int`
  desborda. `zone_id` no se mira: `Terrain` pasa las ediciones de la zona.
- Lanzan `ArgumentNullException` con `edits` nulo o con un elemento nulo, y `ArgumentException`
  con dos ediciones del mismo `uuid`; `Apply`, además, `ArgumentNullException` con `baseHazard`
  nulo y `ArgumentException` si no tiene 257 × 257 muestras. Una edición que la regla ignora no
  lanza.

El tipo `Edit` de C# *(equipo)* lleva los campos del esquema de `Edit`, más `from_habitat`
(bool), que no viaja: `Terrain` lo pone a verdadero en una ganadora recibida por `changes`, y es
la marca que hace que no se reevalúe su `confirmed`. También lo pone a verdadero en una edición
local cuando llega su ack `accepted` *(equipo)*: el hábitat ya la evaluó con el log entero. Cómo
funciona: sección "`apply()` (D5)".

**Los tipos de las ediciones se definen una sola vez, en `Edits`** *(equipo)*, y entran en la
firma de H0:45. Los asmdef de `Terrain`, `HUD` y `FX` referencian `Edits`, igual que `Routing`.
Son inmutables con la misma regla que los de C4: el `polygon` se copia y se lee como
`IReadOnlyList<FixedPoint>` *(propuesta)*:

| Tipo | Contenido |
|---|---|
| `Edit` | el de arriba; su `payload` es un `EditPayload` |
| `EditPayload` | los campos de los tres `payload` del catálogo de `op` ([Convenciones](./convenciones.md), sección "Ediciones"): `polygon` y `point` como enteros en punto fijo, `level` (`byte`), `confirmed` (bool) y `text`, más el `op` para el que se construyó. Solo se construye con sus tres fábricas, que dejan sin valor los campos de los otros `op`: `EditPayload.Hazard(polygon, level, confirmed)`, `EditPayload.Obstacle(polygon, confirmed)` y `EditPayload.Note(point, text)`, con `confirmed` opcional |
| `LiveEntity` | `{entity_id, op, creator, level, payload}`: lo que devuelve `LiveEntities` |
| `EditOpText` | la única tabla entre `EditOp` y el texto del `op` que viaja: `ToWire(op)` da `"hazard.set"`, `"obstacle.set"` o `"note.set"`, y lanza con un `op` no definido; `TryParse(texto, out op)` da falso con un texto nulo o desconocido, sin lanzar, y deja `EditOp.None`. `EditOp` reserva el 0 para `None`, que ninguna edición lleva; `HazardSet`, `ObstacleSet` y `NoteSet` valen 1, 2 y 3 *(propuesta de P3)*. Compara exacto, sensible a mayúsculas. Solo cadenas: no lee JSON *(propuesta)* |

`EditRequested` lleva un `EditPayload` y `EntitiesChanged` la lista de `LiveEntities`, de solo lectura. El `HUD` construye
el `EditPayload` al crear una edición, con su fábrica; `FX` solo lee el `polygon` y el `level` de
cada entidad.

Lo que lanza `ArgumentException` (un argumento inválido, [Convenciones](./convenciones.md),
sección "Logs y errores") *(propuesta)*:

- Las fábricas: un `polygon` nulo, de menos de 3 vértices o con el primer vértice repetido al
  final, y un `text` nulo. Que el anillo sea simple no se comprueba: es cosa del `HUD`.
- El constructor de `Edit`: un `payload` construido para otro `op`; `uuid` o `entity_id` que no
  son UUIDv4 en minúsculas con guiones; `author` o `creator` fuera de `^[a-z0-9-]{2,16}$`;
  `zone_id` fuera de `^[a-z][a-z0-9-]{1,31}$`; `lamport` < 1; y un `op` que no es uno de los tres.
  Los formatos son los de [Convenciones](./convenciones.md), sección "Identificadores".
- El constructor de `LiveEntity`: un `op` que no es uno de los tres, un `payload` construido para
  otro `op`, y un `level` que no casa con el `op`: el `level` del `payload` en `hazard.set`, 255 en
  `obstacle.set` y sin valor en `note.set`.

### Entregables y goldens

En H0–2 P3 no tiene implementación. Deja en `main` lo que no depende de código, y los goldens que
sí dependen entran después *(equipo)*:

| Entregable | En H2 | Con código |
|---|---|---|
| `cost_params.json` y su esquema | completos | |
| La firma pública: los tipos y las llamadas de esta sección, en `Routing/` y `Edits/` | compila contra netstandard2.1; cada cuerpo de cálculo lanza `NotImplementedException`, pero los constructores, la comprobación de los argumentos, `DurationLimit` y `EditOpText` ya funcionan | se rellena según el orden de [Reparto del trabajo](./reparto.md), sección "P3: motor de rutas y ediciones" |
| `routing/costgrid_synth.bin` | completo: lo genera un script | |
| `routing/expected_route.json` | una `Route` que genera el script de `costgrid_synth.bin`, sin A\* *(equipo)*: una recta sobre la rampa de la grilla sintética, con `eta_s` = longitud_3D / (v₀ · exp(−b · |s + c|) · `f_terreno`), `c` = 0 y s = ±20/256 (+ hacia el este, − hacia el oeste): la función es simétrica, así que la ida y la vuelta sobre la misma recta tardan lo mismo. Su `u` sigue la rampa, así que se dibuja sobre el tile sintético. Fija la forma y es el mock de `Terrain`, `FX` y `HUD` | P3 la regenera con el A\* suavizado, por un PR en `contracts/` |
| `routing/expected_plan.json` | `{schema_version, plans[]}`: las tres `Plan`, en el orden de `Alternative`, que genera el mismo script con las dos paradas de la misión sintética. Sobre la rampa las tres son iguales. Es el mock de `RouteSelected` | P3 la regenera con `OrderStops` |
| `edits/base_tile/` y `edits/edits_NN.json` | completos; `base_tile/` es una copia del tile sintético de C1 | |
| `edits/expected_NN.sha256` | no existen | entran con `apply()`, antes de H6: los usa E9 |

Los fixtures de E9 viven por ahora en `Edits/Tests~/fixtures/edits/` *(propuesta de P3)*, con el
mismo layout (`base_tile/` + `edits_NN.json` + `expected_NN.sha256`, N = 6): `base_tile/` es una
copia del tile sintético de P1 de `origin/p1/ingest`, y `validate.py` no acepta un `edits_NN.json`
sin el esquema de `edit_log` de P2. Cuando el tile de P1 y el `edit_log` de P2 estén en `main`,
pasan a `contracts/fixtures/edits/` con `git mv`, y en los tests solo cambia `GoldenTests.Dir`.

- `costgrid_synth.bin` *(equipo)* es una `CostGrid` serializada, little-endian: `width` y `height`
  (`int32`), `cell_m` (`float32`), `origin_e` y `origin_n` (`float64`), y después los cuatro
  arrays en el orden de la tabla de tipos. La sintética es la grilla del tile sintético, con su
  rampa y su peligro: las fórmulas, el tamaño y el origen están en
  [Convenciones](./convenciones.md), sección "Cómo se fija un contrato". El script de P3 arma los
  rasters del tile con esas fórmulas y los reduce con la regla de `BuildGrid`; no lee el tile de
  P1. Las grillas patológicas viven en los tests de `Routing/`.
- Las rutas de la tabla son relativas a `contracts/fixtures/`.
- Resultados del benchmark: `Routing/BENCHMARK.md`.

## `apply()` (D5)

**`apply(baseTile, edits[]) → tile'` es una función pura con una sola implementación, en C#**
(ensamblado `Edits`, netstandard2.1), **y se ejecuta en la tablet.**

- Dos implementaciones (C# + Python) divergen por float32/64 y por la contracción a FMA que clang
  activa por defecto ([Clang](https://clang.llvm.org/docs/UsersManual.html#cmdoption-ffp-contract)).
  Por eso el hábitat solo fusiona metadatos ([Hábitat](./habitat.md), sección "Log de ediciones y
  merge (D5)").
- **Aritmética entera:** `hazard` es `uint8` y los vértices van en punto fijo (1/256 de celda).
- **`apply()` solo cambia `rasters.hazard`** *(equipo)*: ninguna edición toca la altura, y una
  nota no toca el tile.
- **Orden determinista:** `(lamport, author, uuid)`. Al recorrerlo, `apply()` evalúa la regla de
  confirmación igual que el merge ([Convenciones](./convenciones.md), sección "Ediciones").
- **Una ganadora que llegó por `changes` no se reevalúa** *(equipo)*: el hábitat ya la aceptó
  viendo el log entero, que la tablet no tiene. `Terrain` la marca al guardarla y `apply()` la
  aplica sin mirar `confirmed`. Lo mismo vale para una edición local desde que llega su ack
  `accepted`. La regla solo se evalúa sobre las ediciones locales sin ack. El
  golden de merge lleva un caso así: una subida de quien no es el creador, sobre una edición
  intermedia que la tablet no vio.
- Una muestra queda dentro de un polígono si cae en su interior o en su borde, con la regla
  par-impar *(equipo)*.
- Golden hashes en `contracts/fixtures/edits/`: `expected_NN.sha256` es el SHA-256 del `hazard.r8`
  que resulta *(equipo)*.
- Los datos originales no se tocan.

La equivalencia entre x64-JIT y ARM64-IL2CPP es **NO VERIFICADA** → **E9**.
P3 no corre E9 en la tablet *(propuesta de P3)*: E9 se cierra con N/N hashes iguales en
`dotnet test`, y la ejecución en IL2CPP ARM64 queda fuera de alcance por decisión.

## Efectos visuales (D8)

**El holograma es Shader Graph** (fresnel, scanlines, curvas de nivel por `frac` de la altura,
wireframe, bloom) **y el heatmap es una textura R8.** La niebla tiene dos partes:

- **Niebla lejana:** un tinte en el shader del terreno.
- **Niebla cercana:** una sola pasada analítica + pocas partículas grandes.

Por qué:

- URP no trae niebla volumétrica nativa. Desde arriba, una niebla estilizada se lee mejor y encaja
  con la estética holográfica (U6).
- En Mali el blending reduce la eliminación de superficies ocultas y los algoritmos fragment rinden
  más que los compute; el driver limita a 512 invocaciones por workgroup; la tablet sí soporta R8,
  R16 y RG8 como attachment y storage (leído en gpuinfo, no ejecutado en la tablet → **E1**)
  ([Arm GPU Best Practices 4.0, §8.6, §9.1, §12.7](https://documentation-service.arm.com/static/6ae1e4b40fefe512f5edb01a?token=);
  [gpuinfo #44864](https://vulkan.gpuinfo.org/displayreport.php?id=44864); frente A §3).

Cómo:

- **Sin compute, sin `discard` y sin framebuffer fetch en shaders propios.**
- `half` para el color y `float` para la altura de las curvas.
- Bloom a ½, sin High Quality Filtering.

Factibilidad en 48 h: alta.

Pendientes y riesgos:

- **El overdraw blended de la niebla es el riesgo principal**; su coste no tiene benchmark
  (**NO VERIFICADO**) → **E3**.
- Hay un crash de framebuffer fetch reportado en 2021.3 + GLES3 + Mali-G76
  ([hilo](https://discussions.unity.com/t/urp-shader-framebuffer-fetch-mali-crash/915692)); en 6.3
  es **NO VERIFICADO** → **E4**.
- Un raymarch de 8–16 pasos a ¼ de resolución solo entra como fragment shader y tras perfilar en
  H12–24.
- Plan B: solo el tinte en el terreno, sin partículas.

## Contrato de escena (C5)

| Qué | Contenido |
|---|---|
| Prefabs | `Terrain` (P4), `FX` (P5), `HUD` (P6), `Routes` (P5, en `tablet/Assets/FX/`) |
| Propiedades de material | por tile y globales: tablas de abajo |
| Capas | qué une cada capa de C1 con su textura y su toggle: tabla "Capas" |
| Bus de eventos | tabla "Eventos" |
| Prohibido en shaders propios | compute, `discard`, framebuffer fetch |

Lo que esta sección marca *(equipo)* ya está decidido: P4 lo transcribe al fijar el contrato.

### Entregables en H2

*(equipo)*

C5 no tiene fixture en `contracts/`: su entregable es código que compila en `tablet/`, en `main`.

| Entregable | Dónde | Contenido en H2 |
|---|---|---|
| `MapEvents` y `MapQuery` | `tablet/Assets/Terrain/` | todos los eventos de la tabla "Eventos", con el struct de su carga, y las dos consultas, que en H2 resuelven contra el plano Y = 0: `TryPick` da el punto de ese plano bajo el toque y `HeightAt` da 0. P4 las sustituye por el terreno real sin cambiar la firma |
| Tipos de datos | `tablet/Assets/Terrain/` | las clases que leen `zone.json` y `conditions.json`, con los campos de sus tablas |
| `AppConfig` | `tablet/Assets/Terrain/` | completa: es corta y no depende de ningún contrato, así que es lo primero que P4 escribe en H0. Sección "Configuración" |
| Cargador de fixtures | `tablet/Assets/Terrain/` | lee `expected_route.json`, `expected_plan.json`, `cost_params.json`, el `zone.json` y el `conditions.json` de `contracts/fixtures/` a los tipos de C4 y C5. Lo escribe P4 después de H0:45, cuando la firma de C4 está en `main` |
| `ShaderIds` | `tablet/Assets/Terrain/` | una constante por propiedad de esta sección |
| Prefabs `Terrain`, `FX`, `HUD`, `Routes` | la carpeta de cada dueño | vacíos, ya colocados en `Main.unity`. `Terrain` y `HUD` los crea P4 antes de H0, y `FX` y `Routes` los crea P5, con el esqueleto; desde H0 cada uno es de su dueño |
| Sub Graphs de `FX` | `tablet/Assets/FX/` | los de "Cómo dibuja `FX` sobre el terreno", sin efecto. Los crea P5 antes de H0 *(equipo)*; desde H0 son de P5 |

- El "bus de eventos falso" de cada dueño es un script de su escena de pruebas que emite los
  eventos de `MapEvents` con los fixtures; lo escribe cada uno, sobre el cargador de fixtures de
  [Convenciones](./convenciones.md), sección "Unity".
- `Route` y `Plan` se definen una sola vez, en `Routing`, y `EditPayload` y `LiveEntity` en
  `Edits`. Los asmdef de `Terrain`, `FX` y `HUD` referencian los dos paquetes, porque los structs
  de carga llevan esos tipos; de `Routing`, `FX` solo usa `polyline`. Por eso los eventos
  `RouteRequested`, `RouteSelected`, `EditRequested` y `EntitiesChanged` esperan a la firma de C4 ([Convenciones](./convenciones.md), sección "Qué contratos
  se fijan y en qué orden").
- Las clases de "Tipos de datos" leen `zone.json` y `conditions.json`. `mission.json` lo lee el
  `HUD`, con una clase suya.

### Configuración

*(equipo)*

`AppConfig` es una clase estática de `Terrain` que guarda la configuración de la app. El `HUD` la
usa desde su pantalla de ajustes y desde la que pide el `author`; `Terrain`, para su cliente HTTP.

| Miembro | Qué hace |
|---|---|
| `AppConfig.HabitatUrl`, `AppConfig.FilesUrl`, `AppConfig.Author` | el valor vigente, de solo lectura; una cadena vacía si nunca se guardó |
| `AppConfig.Save(habitatUrl, filesUrl, author)` | valida, guarda y emite `ConfigChanged`. Lanza una excepción si una URL no tiene la forma `http://host:puerto` o si `author` no cumple su expresión de C0 |

- Qué son las dos URL y qué puertos llevan: [Convenciones](./convenciones.md), sección "API (C3)".
- Se guarda en `persistentDataPath/config.json`, como `{schema_version, habitat_url, files_url,
  author}` *(propuesta)*. Solo `Terrain` lee y escribe ese archivo, así que no tiene esquema en
  `contracts/`.
- Se carga sola en el primer acceso. Sin `config.json`, toma los valores de
  `tablet/Assets/StreamingAssets/config.default.json`, con la misma forma *(propuesta)*: P4 lo
  commitea con las dos URL del portátil del hábitat en el router del equipo y con `author`
  vacío. Así el walking skeleton de H6 sondea sin pantalla de ajustes.
- Con una URL vacía `Terrain` no sondea y la app trabaja con su caché; con `author` vacío el
  `HUD` abre la pantalla de `author` y no deja editar hasta que se guarda.
- **Los fixtures llegan al APK por un paso de build** *(propuesta)*: un script de editor de P4,
  en `tablet/Assets/Terrain/Editor/`, copia `contracts/fixtures/` a
  `tablet/Assets/StreamingAssets/fixtures/` antes de cada build y al abrir el proyecto. Esa
  carpeta está en `.gitignore`: no hay una segunda copia en git. Está en el esqueleto, así que
  vale también para los APK de P5.
- **El `author` se guarda una sola vez** ([Convenciones](./convenciones.md), sección
  "Ediciones"): `Save` con un `author` distinto del ya guardado lanza una excepción.
- En H2 la clase está completa, así que no necesita mock: la escena de pruebas del `HUD` la usa
  tal cual.

### Propiedades por tile

Las pone `Terrain` en cada tile, con un `MaterialPropertyBlock`.

| Propiedad | Formato | Contenido |
|---|---|---|
| `_HeightTex` | **R16_UNORM**, 257² | el `uint16` de C1 tal cual |
| `_HeightScale`, `_HeightOffset` | float *(equipo)* | Y de la escena en metros = `_HeightOffset + _HeightScale · muestra`, con la muestra leída como 0–1. `_HeightScale` = `height.scale · 65535`; `_HeightOffset` = `height.offset − origin_height_m` |
| `_HazardTex` | R8, 257² | el `uint8` de C1 leído como 0–1 |
| `_LayerTex0`, `_LayerTex1`, `_LayerTex2` | R8, 257² | las capas del tile: tabla "Capas" |
| UV0 | | el tile, de 0 a 1 |

- Una textura tiene 257 muestras para 256 celdas: la muestra `k` cae en UV = `k / 256`, y el
  shader la lee en `(UV · 256 + 0,5) / 257`.
- **UV (0, 0) es la esquina suroeste del tile.** Los archivos de C1 empiezan por la fila norte, así
  que `Terrain` invierte las filas al cargar cada textura.
- Si a un tile le falta una capa, `Terrain` le pone una textura negra.

### Propiedades globales

*(equipo)*

Se ponen con `Shader.SetGlobal…`, así que ningún material necesita una referencia a otro.

| Propiedad | La pone | Contenido |
|---|---|---|
| `_ZoneOriginHeightM` | `Terrain` | `origin_height_m`: sumado a Y da la altura sobre el areoide, la de las curvas de nivel |
| `_LayerOn0`, `_LayerOn1`, `_LayerOn2` | `Terrain` | 0 o 1, según `LayerToggled` |
| `_HeatTex` | `Terrain` | R8, una por zona, con un texel por nodo de la `CostGrid` de C4. Sale del `Field` de `ReturnField`: `round(254 · min(time_s / _HeatMaxS, 1))`, y 255 donde no hay regreso |
| `_HeatMaxS` | `Terrain` | los segundos que valen 254: el `budget.t_s` de la misión; sin misión, el máximo finito del campo |
| `_HazardZoneTex` | `Terrain` | R8, una por zona: el `hazard` de la `CostGrid`, con las ediciones. Para lo que `FX` dibuja fuera del terreno |
| `_HeatTexRect` | `Terrain` | `(e_0, n_0, 1/(width · cell_m), 1/(height · cell_m))`, con `(e_0, n_0)` medio nodo al suroeste del nodo `(0, 0)`: lleva una posición ENU, la X y la Z de la escena, a la UV de `_HeatTex`, con cada nodo en el centro de su texel |
| `_FogFarColor` | `FX` | el color del tinte lejano |
| `_FogFarStrength` | `FX` | 0–1: cuánto tiñe |
| `_FogFarAmberMin`, `_FogFarRedMin` | `Terrain` | 0–1: `hazard_amber_min` y `hazard_red_min` de `cost_params.json`, divididos por 255 |

- **El tinte de niebla lejana vive en el material del terreno** (P4): lee `_HazardTex` y las
  cuatro propiedades `_FogFar…`. El color y la fuerza los fija `FX`; los umbrales, `Terrain`.

### Cómo dibuja `FX` sobre el terreno

*(equipo)*

**El terreno se dibuja en una sola pasada, con un solo shader, que es de P4.** Ese Shader Graph
incluye los Sub Graphs de P5, y así `FX` no añade overdraw sobre el terreno.

| Sub Graph (P5, en `tablet/Assets/FX/`) | Entradas | Salida |
|---|---|---|
| `SG_Hologram` | UV0, posición de mundo, normal, color base | color y emisión: fresnel, scanlines, curvas de nivel y wireframe |
| `SG_FogFar` | color, muestra de `_HazardTex` | color con el tinte lejano |
| `SG_Heatmap` | color, posición de mundo | color con el heatmap, leído de `_HeatTex` |

- P4 escribe el Shader Graph del terreno, en `tablet/Assets/Terrain/`: muestrea las texturas por
  tile, elige la capa base y encadena los tres Sub Graphs. P5 no toca ese archivo ni el material.
- En H2 los tres Sub Graphs existen con esas entradas y devuelven el color sin cambiarlo; P5 los
  rellena después.
- **Lo que `FX` dibuja con geometría propia** (la niebla cercana, sus partículas y el prefab
  `Routes`) no ve las texturas por tile. Lee el peligro de `_HazardZoneTex`, global: R8, con un
  texel por nodo de la `CostGrid` y la misma UV que `_HeatTex`. `Terrain` la llena con `CostGrid.hazard`
  cada vez que rearma la grilla.
- **Wireframe** *(equipo)*: es una rejilla que sale de UV0, sin atributo de vértice ni
  coordenadas baricéntricas. Hay línea donde `frac(UV0 · N)` queda cerca de 0 o de 1, con `fwidth`
  para el grosor; N = 256 da una línea por celda.
- Las curvas de nivel salen de `frac` de la altura sobre el areoide: Y + `_ZoneOriginHeightM`.

### Capas

*(equipo)*

| `layer_id` | Indicador | En el tile (P1) | En el material (P4) | Quién lo dibuja |
|---|---|---|---|---|
| `topo` | topográfico | `height` | `_HeightTex` | `FX`: las curvas de nivel del holograma |
| `ortho` | óptico | `layers[]`, `ortho` | `_LayerTex0` | el material del terreno |
| `thermal` | térmico | `layers[]`, `thermal` | `_LayerTex1` | el material del terreno, con una rampa de color |
| `thermal_ir` | térmico, cualitativo | `layers[]`, `thermal_ir` | `_LayerTex2` | el material del terreno |
| `hazard` | peligro | `rasters.hazard` | `_HazardTex` | el tinte lejano (material) y la niebla cercana (`FX`) |
| `heat` | regreso al hábitat | | `_HeatTex` | `FX`: el heatmap |

- El `HUD` (P6) tiene un toggle por fila y emite `LayerToggled` con ese `layer_id`.
- `ortho`, `thermal` y `thermal_ir` son excluyentes: el `HUD` apaga una al encender otra.
- `topo`, `hazard` y `heat` se ofrecen siempre. De `ortho`, `thermal` y `thermal_ir`, el `HUD`
  solo ofrece las que la zona tiene: el campo `layers[]` de `zone.json`, que trae también la
  unidad y el rango de la leyenda.
- La radiación no es una capa: es un número en el `HUD`.
- Los archivos de `layers[]`: [Tierra](./tierra.md), sección "Tiles (C1)".

### Eventos

Quién emite cada evento y quién lo escucha *(equipo)*; la carga es *(equipo)*:

| Evento | Lo emite | Lo escuchan | Qué avisa | Carga |
|---|---|---|---|---|
| `TileLoaded` | `Terrain` | `FX`, `HUD` | un tile quedó cargado o cambió por una edición | `zone_id`, `z`, `x`, `y` |
| `ZoneLoaded` | `Terrain` | `FX`, `HUD` | hay una zona activa, con su `zone.json` | el `zone.json` ya leído, la carpeta local de sus imágenes POV y el `author` de esta tablet |
| `ConditionsChanged` | `Terrain` | `HUD` | llegó un `conditions.json`, de la caché o del hábitat | `zone_id` y el `conditions.json` ya leído |
| `RouteRequested` | `HUD` | `Terrain` | el astronauta pide una ruta para un `mission.json`, o elige otra alternativa | la misión en tipos de C4: `stops[]`, `home`, `budget` y `mode`; más `start` y `alternative`, un `Alternative` de C4 |
| `RouteSelected` | `Terrain` | `FX`, `HUD` | hay una ruta calculada y elegida | `plans[]` (una `Plan` de C4 por alternativa, en el orden de `Alternative`), `selected` (el `Alternative` elegido, que es también su índice en `plans[]`) y `polyline`: la elegida, como `Vector3[]` en coordenadas de escena |
| `EditRequested` | `HUD` | `Terrain` | el astronauta creó, cambió o borró una entidad | `op`, `entity_id`, `payload` y `tombstone` |
| `EditRejected` *(equipo)* | `Terrain` | `HUD` | el hábitat rechazó una edición | `entity_id` y `code` |
| `EntitiesChanged` *(equipo)* | `Terrain` | `HUD`, `FX` | cambiaron las entidades vivas de la zona, por una edición local o por `changes` | la lista de entidades vivas: `entity_id`, `op`, `creator`, `level` y `payload` |
| `StopHovered` | `HUD` | `FX` | una parada está señalada | el `id` de la parada |
| `LayerToggled` | `HUD` | `Terrain`, `FX` | se encendió o apagó una capa | `layer_id` y `on` |
| `AstronautMoved` *(equipo)* | `HUD` | `Terrain`, `FX` | cambió la posición del astronauta | `enu_m` |
| `EditModeChanged` *(equipo)* | `HUD` | `Terrain` | entró o salió el modo edición: la cámara deja de usar los gestos de un dedo | `on` |
| `ZonesChanged` *(equipo)* | `Terrain` | `HUD` | cambió la lista de zonas de la caché | `zones[]`, con `zone_id`, `name` y `zone_version` |
| `ZoneRequested` *(equipo)* | `HUD` | `Terrain` | el astronauta elige otra zona; `Terrain` responde con `ZoneLoaded` | `zone_id` |
| `ConfigChanged` *(equipo)* | `Terrain` | `HUD` | `AppConfig.Save` guardó la configuración; `Terrain` reinicia su sondeo con las URL nuevas | `habitat_url`, `files_url` y `author` |

`Terrain` ofrece además dos consultas, en la clase estática `MapQuery` *(equipo)*, que el `HUD`
usa para situar toques, pines y paradas:

| Consulta | Devuelve |
|---|---|
| `MapQuery.TryPick(screenPosition, out enu_m)` | el punto del terreno bajo un toque, o falso si no toca terreno |
| `MapQuery.HeightAt(enu_m)` | la Y de la escena en ese punto |

- `FX` y el prefab `Routes` no usan los tipos de `Routing`: dibujan `polyline`. De `Edits`, `FX`
  lee `LiveEntity`.
- Los ejes de la escena, cómo se nombra un asset nuevo, qué es el bus y qué capas de Unity hay:
  [Convenciones](./convenciones.md), sección "Unity".
- El raster de peligro es uno solo ([Convenciones](./convenciones.md), sección "Reglas de todo el
  equipo"): una edición actualiza a la vez la niebla y la ruta.

## Orquestación

*(equipo)*

**`Terrain` es el único que llama a `Routing` y a `Edits`**, porque es quien tiene los tiles, la
caché y el log. `Routing` y `Edits` no ven el bus ni el motor
([Convenciones](./convenciones.md), sección "Código").

Una ruta:

1. El `HUD` emite `RouteRequested` con las paradas de `mission.json`.
2. `Terrain` entrega a `Routing` los rasters de los tiles, `cost_params.json` y las condiciones
   vigentes, y lo llama en un hilo de fondo: `BuildGrid` si la grilla cambió, `ReturnField` si cambió la grilla o la base, y
   después `OrderStops` con ese mismo `Field`, que calcula dentro el límite por radiación y
   devuelve una `Plan` por alternativa. `Terrain` guarda el último `Field`: pinta `_HeatTex` y
   comprueba `return_ok` *(propuesta)*.
3. De vuelta en el hilo principal, `Terrain` llena `_HeatTex` y emite `RouteSelected`.
4. El prefab `Routes` dibuja la polilínea, `FX` el resaltado y el heatmap, y el `HUD` la ETA y el
   presupuesto.

Una edición:

1. El **modo edición es del `HUD`**: el gesto, el polígono, el nivel y el diálogo de `confirmed`.
   El `HUD` emite `EditRequested` con `op`, `entity_id`, `payload` (ya en punto fijo) y
   `tombstone`.
2. `Terrain` completa `uuid`, `lamport`, `author`, `creator` y `zone_id`, agrega la edición al log local,
   llama a `apply()`, refresca `_HazardTex` y emite `TileLoaded` y `EntitiesChanged`.
3. `Terrain` rearma la grilla y recalcula el campo y la ruta activa, como en una ruta.
4. `Terrain` encola la edición para `POST /edits`.
5. Si el ack es `rejected`, `Terrain` quita la edición del log local, vuelve a llamar a `apply()`
   y emite `EditRejected`. Con `confirmation_required`, el `HUD` reabre el diálogo de
   confirmación.

El texto del `op` *(propuesta de P3)*: `Terrain` lo escribe con `EditOpText.ToWire` y lo lee, del
log local o de `changes`, con `EditOpText.TryParse`. Si `TryParse` da falso, descarta esa edición,
escribe `[Terrain] … op=<texto>` y sigue; no escribe su propia tabla.

## HUD e indicadores (D10)

El `HUD` muestra los cuatro indicadores de [Contexto del reto](./contexto.md): las capas
topográfica, óptica y térmica, y la dosis de radiación.

- **Radiación:** todo valor se muestra con su `basis` (medido o modelado). Un factor Forbush < 1 se
  muestra, sin alargar el límite de duración (regla Forbush de [Tierra](./tierra.md), sección
  "Condiciones (C6)").
- **Térmico:** el texto del HUD es "Inercia térmica (100 m/px): baja = material fino o suelto; alta
  = consolidado o rocoso; no resuelve rocas individuales". La interfaz lo muestra en inglés.
- **Pines POV:** cada pin muestra el `credit` de su entrada en `pov[]` ([Tierra](./tierra.md),
  sección "Tiles (C1)"). El `HUD` los recibe con `ZoneLoaded`.
- **Entidades editadas:** el `HUD` dibuja las notas y el contorno de cada zona de peligro y
  obstáculo, con la lista de `EntitiesChanged`. Con ella sabe también si quien edita es el
  `creator`, y abre el diálogo de `confirmed` antes de emitir `EditRequested` *(equipo)*.
- **Sin logos de NASA ni JPL** en la app.
- **Ruta:** ETA en P50 con rango P10–P90, y presupuesto. De cada alternativa muestra la ETA, los
  metros en celda ámbar y el desnivel *(equipo)*.
- **Posición del astronauta** *(equipo)*: es del `HUD`. La demo no tiene sensor de posición:
  empieza en la base de la misión y el astronauta la mueve con una pulsación larga sobre el mapa.
  El `HUD` dibuja el marcador y emite `AstronautMoved`; `Terrain` la usa para recentrar la cámara,
  y es el `start` de la ruta.

El idioma, el formato de cada magnitud, cómo se ven `basis` y los créditos, y los gestos de
navegación: [Convenciones](./convenciones.md), sección "Presentación".

## Misión (C6)

`mission.json` es la mitad de C6 que produce el `HUD`. Sus campos son *(equipo)*:

| Campo | Contenido |
|---|---|
| `schema_version` | int |
| `zone_id` | la zona de la misión |
| `name` | string |
| `base_enu_m` | `[e, n]`: la base, adonde se regresa; es el `home` de C4. Si se omite, es el `habitat_enu_m` de `zone.json` |
| `stops[]` | `{id, name, enu_m, objective, science_value, dwell_s}`: el objetivo científico en texto, su valor de 0 a 10 y el tiempo en la parada |
| `budget` | `{t_s, dose_uGy}`; `dose_uGy` es el `dose_budget_uGy` de [Convenciones](./convenciones.md), sección "Valores fijados" |
| `mode` | `"TSP"` u `"OP"` |

- Hay una misión por zona, en `tablet/Assets/HUD/` *(equipo)*; el fixture está en
  `contracts/fixtures/mission/`.
- En ejecución, `cost_params.json` es una copia del fixture en StreamingAssets *(equipo)*.
- **`mission.json` lo lee el `HUD`**, que lo entrega en `RouteRequested` convertido a los tipos
  de C4: `Terrain` no conoce la clase del `HUD`.
- `Routing` no lee el archivo: `Terrain` le pasa las paradas, la base y el presupuesto, con los
  tipos de la sección "Contrato de rutas (C4)".
