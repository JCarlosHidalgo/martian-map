# docs/guide: la guía que el equipo lee en Docmost

Este nivel contiene la guía de Martian Map que una persona del equipo lee de arriba abajo, y la
instancia de Docmost donde se lee. Docmost no guarda nada escrito allí: solo lo que `publish.py`
construye desde este repositorio.

La herramienta se adoptó de Centinela (Dravensoft), `docs/guide/` de ese proyecto. Las diferencias
con el original están en "Desviaciones respecto a Centinela".

## Por qué existe cada archivo

| Ruta | Por qué existe |
|---|---|
| `guide.json` | el manifiesto: el árbol de páginas y el orden de lectura; cada página es un archivo del repositorio, o un título con una introducción de una línea |
| `chapters/` | las páginas de la guía: el contexto del reto, los ambientes, las convenciones, una página por ambiente, el reparto del trabajo y las fuentes |
| `publish.py` | construye el zip desde el manifiesto y lo importa en Docmost como el espacio `martian-map` |
| `docker-compose.yml` | Docmost con su propio Postgres y Redis |
| `.env.example` | cada ajuste que leen el compose y `publish.py`, sin secretos: la plantilla de una instancia nueva, cuyo `APP_SECRET` sale de `openssl rand -hex 32` |
| `.env` | los ajustes de la instancia local, creados desde `.env.example`; contiene secretos y no se imprime |
| `.env.docs` | un `.env` ya relleno, para levantar la guía en un repositorio recién clonado con `cp .env.docs .env`. Sus tres secretos son públicos: valen solo para una instancia local que no sale de `localhost` |

## Decisiones

- **Docmost guarda una proyección, nunca una fuente.** Cada ejecución de `publish.py` borra el
  espacio y lo importa de nuevo, y el grupo por defecto lo lee sin poder escribirlo, porque una
  página editada en Docmost sería una copia de un hecho que caduca sin que nada falle.
- **Cada hecho vive en una sola página, y las demás la enlazan.** La página de reparto es la que
  más enlaza y la que menos afirma. *Ningún gate lo sostiene.*
- **Un diagrama dibuja algo que el texto afirma y lleva la leyenda `Dibuja:`** con la página y el
  encabezado que dibuja. `publish.py:check_draws(text, source)` rechaza el build cuando ese
  encabezado ya no existe. El chequeo ve desaparecer un encabezado, no cambiar el texto que tiene
  debajo.
- **Docmost `0.96.0`, Postgres `18.6` y Redis `8.10.2` están fijados**, porque `publish.py` depende
  del comportamiento de importación de esa versión: una página se titula por su primer encabezado,
  los hermanos se ordenan por nombre de archivo, un enlace relativo a `.md` solo se vuelve enlace
  interno sin `#fragmento`, y un bloque ` ```mermaid ` se dibuja como diagrama. Un cambio de
  versión se vuelve a verificar contra esos cuatro.
- **Un enlace entre páginas nombra la sección en el texto**, como en `[Tierra](./tierra.md),
  sección "Tiles (C1)"`, porque el fragmento se pierde al importar.
- **`publish.py` entra como la UI**, por `/api/auth/setup` en una instancia nueva y
  `/api/auth/login` después, porque Docmost solo da API keys en su edición enterprise.
- **Docmost escucha en `DOCMOST_PORT`, 3200 por defecto, y su Postgres y su Redis no exponen
  puerto en el host.** El proyecto de Compose es `martian-map-guide`, así que borrar sus volúmenes
  no toca los de ninguna otra instancia.

## Desviaciones respecto a Centinela

| Desviación | Motivo |
|---|---|
| **`publish.py` no lee git.** No tiene `git()`, `commit_stamp()`, `forge_url()` ni `inventory()`, y el manifiesto no admite páginas `generated` | el proyecto es un repositorio git (`origin`: `github.com/JCarlosHidalgo/martian-map`), pero aún no tiene código que inventariar, y sellar cada página con su commit entra con él |
| Las páginas publicadas no llevan el aviso "Publicado desde…" bajo el título | se quitó a pedido; que una edición en Docmost se pierde lo dice este archivo |
| Un enlace a un archivo del repositorio que la guía no importa se publica como texto con la ruta entre comillas de código | la forja aún no tiene ningún archivo al que enlazar |
| Un enlace relativo a un archivo que no existe rechaza el build | sin gates de citas, es el único chequeo de enlaces que hay |
| Compose `martian-map-guide`, puerto 3200, espacio `martian-map` | no chocar con la instancia de Centinela (`centinela-guide`, 3100, `centinela`) |
| `.env` propio, no copiado de Centinela | cada instancia tiene sus secretos |
| **La guía está en español**, y con ella la leyenda `Dibuja:` y el marcador `> **Decidido, no construido.**` | es el idioma del equipo |
| La guía no importa páginas de nivel: todas sus páginas son capítulos | el proyecto aún no tiene niveles con `AGENTS.md` propios. La guía es la fuente del diseño: no hay otro documento detrás |

El día que el repositorio tenga código, las filas sobre git se revierten copiando de nuevo esas
funciones de Centinela.

## Comandos

Se corren desde este directorio, con Python 3 y Docker Compose:

| Comando | Qué hace |
|---|---|
| `docker compose up -d` | levanta Docmost en `APP_URL`; `curl -fsS "$APP_URL/api/health"` responde cuando está listo |
| `python3 publish.py` | construye la guía y reemplaza el espacio `martian-map`; imprime su dirección |
| `python3 publish.py --build-only DIR` | escribe el árbol Markdown de la guía en `DIR` y no toca Docmost |

Una instancia nueva necesita su `.env`. Para leer la guía en local basta `cp .env.docs .env`. Una
instancia que otros alcancen por la red parte de `.env.example`, con `APP_SECRET`,
`POSTGRES_PASSWORD` y `DOCMOST_ADMIN_PASSWORD` propios.

## Reglas de este nivel

- **Una corrección va al repositorio y se publica**, nunca a Docmost.
- **Una página nueva entra por `guide.json`.**
- **Una sección que describe un diseño sin código abre con `> **Decidido, no construido.**`**
  `publish.py` convierte esa cita en una caja de aviso. Mientras nada tenga código, la marca va una
  sola vez, en la primera página. *Ningún gate lo sostiene.*
- **Lo que no tiene fuente se marca** *(inferencia)* o **NO VERIFICADO**, con el experimento que lo
  cierra. *Ningún gate lo sostiene.*
- **Una fuente se cita por su URL o por su etiqueta** (`S1…S13`, `frente A–D §n`), y la etiqueta
  se resuelve en `chapters/fuentes.md`. Ninguna página cita un archivo de fuera de `docs/guide/`.
  *Ningún gate lo sostiene.*
- **La guía no cuenta historia**: qué cambió entre versiones del diseño no entra.

## Verificado por una persona

Tras cambiar un capítulo o `guide.json`: publicar, abrir cada página cambiada en Docmost, y
comprobar que cada diagrama se ve como dibujo y no como código, y que cada enlace interno abre una
página de Docmost.
