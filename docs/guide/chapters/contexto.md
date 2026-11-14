# Contexto del reto

> **Decidido, no construido.** Toda la guía describe un diseño que aún no tiene código. Cada
> página lo enuncia en presente; lo que falta medir lleva la marca **NO VERIFICADO** y el
> experimento que lo cierra.

Martian Map es una app de tablet para un astronauta en la superficie de Marte: un mapa 3D que
crece solo con datos de NASA, se puede editar y calcula la ruta más rápida y segura. Esta página
dice qué pide el reto, qué queda fuera, qué zona se eligió y qué indicadores muestra el mapa.

## Orden de lectura

| Página | La pregunta que responde |
|---|---|
| Contexto del reto (esta) | qué hay que construir, para quién y con qué límites |
| [Los ambientes](./ambientes.md) | qué tres ambientes hay, qué viaja entre ellos y qué contratos los unen |
| [Convenciones](./convenciones.md) | cómo se fija un contrato y qué convenciones cumple todo el equipo |
| [Tierra](./tierra.md) | cómo se convierten los datos de NASA en tiles, condiciones y bundles |
| [Hábitat](./habitat.md) | cómo el servidor importa bundles, sirve la API y fusiona ediciones |
| [Tablet](./tablet.md) | cómo la app dibuja el terreno, calcula rutas, aplica ediciones y muestra indicadores |
| [Reparto del trabajo en 6 personas](./reparto.md) | quién construye qué, con qué mocks, en qué orden y qué mide |
| [Fuentes](./fuentes.md) | de dónde sale cada afirmación con fuente, y qué URL se sondean en H0 |
| [El reto](./reto.md) | qué dice el enunciado aportado por P6, qué recursos incluye y qué falta cotejar con la fuente oficial |

## Cómo leer las fuentes

- Una URL es una fuente primaria.
- `frente A–D §n` es una sección de la investigación previa. Qué afirma cada una y su URL primaria:
  [Fuentes](./fuentes.md), sección "Secciones de la investigación".
- `S1…S13`, junto a una fuente, son los sondeos en vivo del **2026-11-12**:
  [Fuentes](./fuentes.md), sección "Sondeos en vivo". No son los requisitos `S5…S11` del reto, que
  están más abajo en esta página y nunca acompañan a una fuente.
- Lo que no tiene fuente va como *(inferencia)* o **NO VERIFICADO**.
- Lo que decidió el equipo sin fuente externa lleva la marca *(equipo)*. Casi todo está en
  [Convenciones](./convenciones.md); el resto, en la página de su contrato.
- `D1…D11` son las decisiones del diseño, `C0…C6` y `C1b` los contratos, `E1…E14` los
  experimentos. Los experimentos están en [Reparto del trabajo](./reparto.md), cada uno bajo su
  responsable.

## El reto

NASA Space Apps 2026 (14 y 15 de noviembre de 2026), reto *Interplanetary Survival Guide: Martian
Map*. Hay que crear un mapa, herramienta o interfaz para un astronauta en una misión de superficie
en Marte. Debe ser una vista **hiperlocal**, por capas e integrada, de un lugar o ruta, y combinar
datos de **varias misiones de NASA** para planear y ejecutar una Marswalk segura con ciencia en el
camino.

El enunciado aportado por P6, traducido íntegramente al español, y sus cuatro enlaces de recursos
están en [El reto](./reto.md), al final del orden de lectura. Su sección "Fuente y estado de
verificación" identifica lo que falta cotejar con la página oficial.

## Requisitos del reto

Se infieren del texto del reto. R = explícito, S = sugerido.

| Id | Requisito |
|---|---|
| **R1** | mapa, herramienta o interfaz que use datos y herramientas de NASA |
| **R2** | datos de varias misiones; S: ≥3 tipos (óptico, térmico, espectral, topográfico, geofísico, atmosférico, radiación, químico) |
| **R3** | escala hiperlocal, no regional ni global |
| **R4** | seguridad primero (radiación y clima espacial, terreno peligroso) |
| **S5** | ruta que llegue a los objetivos científicos y evite obstáculos |
| **S6** | objetivos científicos definidos y zona elegida con justificación |
| **S7** | formato habitáculo, tablet, HUD u otro |
| **S8** | producto estático, interactivo o un sistema que genera mapas de otras zonas o con otros datos |
| **S9** | imágenes POV de rovers dentro del mapa |
| **S10** | rovers o robots de apoyo, o astronautas en parejas |
| **S11** | datos de un periodo pasado, predicción futura o sin tiempo |

## Requisitos del usuario

| Id | Requisito |
|---|---|
| **U1** | una interfaz común para alimentar el mapa |
| **U2** | el mapa crece solo al recibir zonas nuevas (por ingesta automática desde NASA) |
| **U3** | se pueden editar secciones existentes |
| **U4** | rutas en el menor tiempo, con uno o varios algoritmos |
| **U5** | el stack cumple el reto completo por encima de la comodidad del equipo |
| **U6** | exploración 3D al estilo del mapa de niveles de *DOOM: The Dark Ages* (inspiración, sin copiar recursos ni apariencia) |
| **U7** | niebla roja en las zonas de peligro (terreno y radiación) e indicadores topográfico, óptico, térmico y de radiación |
| **U8** | app de tablet sobre motor de videojuego |
| **U9** | equipo de 6 personas, 48 h, con trabajo independiente entre ellas |

## Fuera de alcance

No se reabre:

- trajes (O₂, Pandolf, costo metabólico);
- indicadores distintos de los 4;
- PostGIS, MinIO, Celery, Mars Rover Photos API, Theta\* como principal, CRDT de librería, Godot,
  iOS y Cesium en el build de la tablet.

## La zona elegida (D9)

**Gale / Mount Sharp (boxwork) va precargada**: una zona de ~3×3 km centrada en (−4.81, 137.385).
**Jezero es la segunda zona y se ingiere en vivo.** Cómo llega Gale a la tablet:
[Tablet](./tablet.md), sección "Caché local".

| Zona | Por qué |
|---|---|
| Gale | 13 DTM HiRISE (3 sobre el boxwork, ~1,01 m); única zona con radiación medida (RAD); POV de Curiosity; ejemplo literal del reto |
| Jezero | DTM HiRISE vía STAC (4 en el bbox 77.40–77.50 E, 18.40–18.48 N, S1c); otra misión y el mismo pipeline, lo que demuestra U2 y S8 |

Factibilidad en 48 h: alta.

Datos de zona que faltan *(equipo)*. **P1 los fija antes de H0**, junto a E13, y los anota en esta
sección con un PR; P6 aprueba el hábitat, porque de él salen las paradas de la misión. Hasta
entonces todos trabajan con la zona `synth` ([Convenciones](./convenciones.md), sección "Cómo se
fija un contrato"), y ningún contrato de H0–2 depende de estos valores:

| Dato | Estado |
|---|---|
| bbox y centro de la zona de Jezero | por fijar: una zona de ≤ 3×3 km dentro del bbox de la tabla, que es el de la búsqueda STAC y no el de la zona |
| `habitat_enu_m` de Gale y de Jezero | por fijar: un punto dentro de `extent_enu_m`, sobre una celda con peligro 0 |

Riesgos y pendientes:

- El jurado puede ver Gale como "elección obvia".
- **En Jezero la radiación no está medida.** Su valor va con `basis = "modelled"`, `derived_from` =
  RAD Gale release 42 + DONKI, incertidumbre ±30 % *(inferencia)* y la etiqueta "Perseverance no
  mide dosis" ([ERRATA RAD](https://pds-ppi.igpp.ucla.edu/data/MSL-M-RAD-3-RDR-V1.0/ERRATA.TXT),
  S3). Los campos son los de `conditions.json`, en [Tierra](./tierra.md), sección "Condiciones (C6)".
- El coeficiente dosis–columna atmosférica de Gale a Jezero es **NO VERIFICADO**.

## Los cuatro indicadores (D10)

El mapa muestra **solo 4 tipos de indicador: topográfico, óptico, térmico y radiación**. Son 4
tipos (cumple el S de R2: ≥3) y combinan las misiones MRO, MGS, Mars Odyssey, MSL y Mars 2020, más
heliofísica. No se usa la Mars Rover Photos API.

El **peligro** llega al astronauta de dos formas *(equipo)*:

- **Terreno:** sale del DTM, es espacial y se ve como niebla roja (U7), junto con lo que marca el
  astronauta.
- **Radiación:** es una tasa base de RAD modulada por DONKI. No varía dentro de una zona, así que
  no es niebla: se ve como dosis en el `HUD` y acorta la duración de la ruta.

Factibilidad en 48 h: alta.

| Qué | Dónde está |
|---|---|
| Los productos de cada indicador, su acceso, su licencia y sus pendientes | [Tierra](./tierra.md), sección "Fuentes de datos (D10, C2)" |
| Cómo DONKI modula la radiación | [Tierra](./tierra.md), sección "Condiciones (C6)" |
| Cómo se presenta cada indicador al astronauta | [Tablet](./tablet.md), sección "HUD e indicadores (D10)" |
