# Routing benchmark (E5)

- Scenario: laptop only (the tablet is not tested): 5 terrains on 1000²/2000², A* 1 + 5 runs, field 1 + 1
- Machine: .NET 10.0.15, X64, 8 logical CPUs
- Config: seed 0x4D41525321; A* routes of 500 m on 1000/2000², 1 warm-up + 5 runs; ReturnField (v0 P90) on 1000/2000², 1 + 1; the A* rows time PlanRoute (A* + smoothing + trace), and 'smooth ms' is the p50 of the smoothing alone on the same pairs; outside E5, Alternatives on the same pairs, and OrderStops (TSP) on 1000² with 12 free stops within 100 m of the field row's home, which is also the start, and the field of that home (untimed), 1 run, where 'order ms' is Held-Karp alone on the straight-line distances of the same points
- E5 (docs/guide/chapters/reparto.md): A* p95 <= 100.0 ms, field p95 <= 2000.0 ms, 0 GC during the measured runs, managed heap <= 90.0 MB. Measured on the laptop only: the tablet is not tested.

| size | terrain | query | runs | found | p50 ms | p95 ms | max ms | smooth ms | order ms | GC | managed MB | E5 |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 1000 | Uniform | A* 500 m | 5 | 5 | 14.1 | 19.4 | 19.4 | 2.6 | - | 0 | 15.4 | ok |
| 1000 | Uniform | ReturnField | 1 | 1 | 1967.4 | 1967.4 | 1967.4 | - | - | 0 | 19.3 | ok |
| 1000 | Fractal | A* 500 m | 5 | 5 | 53.1 | 70.4 | 70.4 | 0.6 | - | 0 | 15.6 | ok |
| 1000 | Fractal | ReturnField | 1 | 1 | 2033.5 | 2033.5 | 2033.5 | - | - | 0 | 19.5 | FAIL: p95 |
| 1000 | WallWithGap | A* 500 m | 5 | 5 | 17.0 | 426.9 | 426.9 | 2.5 | - | 0 | 16.4 | FAIL: p95 |
| 1000 | WallWithGap | ReturnField | 1 | 1 | 1825.0 | 1825.0 | 1825.0 | - | - | 0 | 20.2 | ok |
| 1000 | Maze | A* 500 m | 5 | 5 | 136.2 | 362.1 | 362.1 | 9.8 | - | 0 | 17.4 | FAIL: p95 |
| 1000 | Maze | ReturnField | 1 | 1 | 328.6 | 328.6 | 328.6 | - | - | 0 | 21.2 | ok |
| 1000 | Blocked30 | A* 500 m | 5 | 5 | 14.1 | 17.4 | 17.4 | 0.1 | - | 0 | 17.4 | ok |
| 1000 | Blocked30 | ReturnField | 1 | 1 | 926.6 | 926.6 | 926.6 | - | - | 0 | 21.2 | ok |
| 1000 | Uniform | Alternatives | 5 | 5 | 41.7 | 58.2 | 58.2 | - | - | 0 | 17.4 | outside E5 |
| 1000 | Uniform | OrderStops k=12 | 1 | 1 | 121.2 | 121.2 | 121.2 | - | 1.5 | 0 | 21.2 | outside E5 |
| 1000 | Fractal | Alternatives | 5 | 5 | 210.1 | 238.4 | 238.4 | - | - | 0 | 17.4 | outside E5 |
| 1000 | Fractal | OrderStops k=12 | 1 | 1 | 494.2 | 494.2 | 494.2 | - | 1.4 | 0 | 21.2 | outside E5 |
| 1000 | WallWithGap | Alternatives | 5 | 5 | 64.3 | 1359.5 | 1359.5 | - | - | 0 | 17.4 | outside E5 |
| 1000 | WallWithGap | OrderStops k=12 | 1 | 1 | 6765.0 | 6765.0 | 6765.0 | - | 1.5 | 3 | 21.3 | outside E5 |
| 1000 | Maze | Alternatives | 5 | 5 | 348.5 | 1138.6 | 1138.6 | - | - | 5 | 17.4 | outside E5 |
| 1000 | Maze | OrderStops k=12 | 1 | 1 | 17456.2 | 17456.2 | 17456.2 | - | 1.4 | 26 | 28.6 | outside E5 |
| 1000 | Blocked30 | Alternatives | 5 | 5 | 43.9 | 54.7 | 54.7 | - | - | 0 | 17.4 | outside E5 |
| 1000 | Blocked30 | OrderStops k=12 | 1 | 1 | 176.8 | 176.8 | 176.8 | - | 1.5 | 0 | 21.3 | outside E5 |
| 2000 | Uniform | A* 500 m | 5 | 5 | 11.0 | 18.4 | 18.4 | 2.7 | - | 0 | 63.2 | ok |
| 2000 | Uniform | ReturnField | 1 | 1 | 8381.6 | 8381.6 | 8381.6 | - | - | 0 | 78.5 | FAIL: p95 |
| 2000 | Fractal | A* 500 m | 5 | 5 | 47.9 | 51.3 | 51.3 | 0.6 | - | 0 | 63.2 | ok |
| 2000 | Fractal | ReturnField | 1 | 1 | 8780.4 | 8780.4 | 8780.4 | - | - | 0 | 78.5 | FAIL: p95 |
| 2000 | WallWithGap | A* 500 m | 5 | 5 | 14.0 | 18.6 | 18.6 | 2.6 | - | 0 | 63.2 | ok |
| 2000 | WallWithGap | ReturnField | 1 | 1 | 7625.6 | 7625.6 | 7625.6 | - | - | 0 | 78.5 | FAIL: p95 |
| 2000 | Maze | A* 500 m | 5 | 5 | 84.0 | 1551.2 | 1551.2 | 6.0 | - | 0 | 69.2 | FAIL: p95 |
| 2000 | Maze | ReturnField | 1 | 1 | 1314.5 | 1314.5 | 1314.5 | - | - | 0 | 84.5 | ok |
| 2000 | Blocked30 | A* 500 m | 5 | 5 | 15.9 | 17.7 | 17.7 | 0.1 | - | 0 | 69.2 | ok |
| 2000 | Blocked30 | ReturnField | 1 | 1 | 4175.7 | 4175.7 | 4175.7 | - | - | 0 | 84.5 | FAIL: p95 |
| 2000 | Uniform | Alternatives | 5 | 5 | 32.7 | 55.6 | 55.6 | - | - | 0 | 69.2 | outside E5 |
| 2000 | Fractal | Alternatives | 5 | 5 | 166.6 | 176.8 | 176.8 | - | - | 0 | 69.2 | outside E5 |
| 2000 | WallWithGap | Alternatives | 5 | 5 | 42.2 | 55.6 | 55.6 | - | - | 0 | 69.2 | outside E5 |
| 2000 | Maze | Alternatives | 5 | 5 | 281.5 | 4713.1 | 4713.1 | - | - | 2 | 69.2 | outside E5 |
| 2000 | Blocked30 | Alternatives | 5 | 5 | 51.2 | 53.2 | 53.2 | - | - | 0 | 69.2 | outside E5 |

## Before / after the alternatives

The A* and field rows of `cc4f602` (before this piece) against this run, same day, seed and protocol, on the laptop. The only change these rows run through is the alternative's weights carried through the segment rule (a struct and one `if` per piece); every MB is the same, every E5 verdict but one is the same, and the times move within the run-to-run noise. The one verdict that differs, Fractal 1000² ReturnField, sits on the 2000 ms line: 1996.3 ms before, 2033.5 ms here, and 1942.6 ms in an earlier run of this branch (`ok`).

| size | terrain | query | p50 ms before | p95 ms before | p50 ms after | p95 ms after | E5 before | E5 after |
|---|---|---|---|---|---|---|---|---|
| 1000 | Uniform | A* 500 m | 16.1 | 27.9 | 14.1 | 19.4 | ok | ok |
| 1000 | Uniform | ReturnField | 1901.6 | 1901.6 | 1967.4 | 1967.4 | ok | ok |
| 1000 | Fractal | A* 500 m | 59.4 | 75.4 | 53.1 | 70.4 | ok | ok |
| 1000 | Fractal | ReturnField | 1996.3 | 1996.3 | 2033.5 | 2033.5 | ok | FAIL: p95 |
| 1000 | WallWithGap | A* 500 m | 17.2 | 459.7 | 17.0 | 426.9 | FAIL: p95 | FAIL: p95 |
| 1000 | WallWithGap | ReturnField | 1768.9 | 1768.9 | 1825.0 | 1825.0 | ok | ok |
| 1000 | Maze | A* 500 m | 121.4 | 373.3 | 136.2 | 362.1 | FAIL: p95 | FAIL: p95 |
| 1000 | Maze | ReturnField | 337.9 | 337.9 | 328.6 | 328.6 | ok | ok |
| 1000 | Blocked30 | A* 500 m | 14.3 | 16.8 | 14.1 | 17.4 | ok | ok |
| 1000 | Blocked30 | ReturnField | 902.7 | 902.7 | 926.6 | 926.6 | ok | ok |
| 2000 | Uniform | A* 500 m | 11.0 | 18.2 | 11.0 | 18.4 | ok | ok |
| 2000 | Uniform | ReturnField | 8487.2 | 8487.2 | 8381.6 | 8381.6 | FAIL: p95 | FAIL: p95 |
| 2000 | Fractal | A* 500 m | 47.0 | 50.4 | 47.9 | 51.3 | ok | ok |
| 2000 | Fractal | ReturnField | 8713.1 | 8713.1 | 8780.4 | 8780.4 | FAIL: p95 | FAIL: p95 |
| 2000 | WallWithGap | A* 500 m | 14.6 | 18.7 | 14.0 | 18.6 | ok | ok |
| 2000 | WallWithGap | ReturnField | 7851.6 | 7851.6 | 7625.6 | 7625.6 | FAIL: p95 | FAIL: p95 |
| 2000 | Maze | A* 500 m | 82.1 | 1519.4 | 84.0 | 1551.2 | FAIL: p95 | FAIL: p95 |
| 2000 | Maze | ReturnField | 1336.5 | 1336.5 | 1314.5 | 1314.5 | ok | ok |
| 2000 | Blocked30 | A* 500 m | 16.0 | 17.4 | 15.9 | 17.7 | ok | ok |
| 2000 | Blocked30 | ReturnField | 4129.8 | 4129.8 | 4175.7 | 4175.7 | FAIL: p95 | FAIL: p95 |

What the rows outside E5 do not show:

- **Stops far apart, or across a wall.** The OrderStops rows draw their 12 stops within 100 m of the base (the user's decision, 2026-11-15, to keep the runner near 2 minutes; it takes 135 s). Within 250 m the maze took 28.0 s and the wall with a gap 8.4 s on 1000², and on 2000² the wall with a gap took 27.0 s: each of the 182 searches (156 for the order, 26 for the safest and gentlest legs) explores as far as the route has to go, and a stop across the wall makes every leg to it find the gap.
- **Held-Karp itself is not the cost.** 'order ms' is 1.4–1.5 ms for 12 stops (a table of 2^12 · 12 values, 393 KB per call); the searches are.
- **The workspace grows (open item against E5's 90 MB).** The Alternatives and OrderStops rows search further than a 500 m A* and grow the per-thread workspace (its touched list and the heap's slot arrays double and never shrink), which stays: run before an E5 row, they pushed its managed heap on 2000² from 63 to 105 MB. They run after every E5 row of their size so that the E5 rows measure what they did before, but a session that orders stops on 2000² will hold about 105 MB, above E5's 90 MB. The mechanism predates this piece (p3-core M1: any long or unreachable PlanRoute); the fix (a reset of every node past a share of the grid, or trimming the workspace when it is returned) is left for its own piece, by the user's decision of 2026-11-15.
- **Not measured again: the workspace no longer stays large (p3-final, 2026-11-15).** The touched list now stops at an eighth of the grid's nodes (0.5 M nodes, 2 MB, on 2000²) and, past it, the return resets every node; and the return trims the heap's slot arrays to 4096 entries. The arrays of one entry per node (key, move and the heap's index) stay, as in the 63 MB baseline. No result changes, but this table was not regenerated: the piece was done without running the benchmark, by the user's decision, so the 105 MB above is the figure before the fix and the figure after it is not known. The OrderStops rows still time TSP with 12 stops (Held-Karp); orienteering and nearest neighbour + 2-opt (more than 12 stops) have no row.
- **GC.** OrderStops allocates its 182 routes and the Held-Karp table on every call: the maze row saw 26 collections, the wall row 3.

## Before / after the smoothing (from `cc4f602`)

The A* rows of `88373f0` (PlanRoute = A* + trace, no smoothing) against this run (A* + smoothing + trace), same seed and protocol, laptop on battery with the CPU at ~2.1–2.4 GHz. `smooth ms` is the smoothing alone. No A* row that passed before fails now; the differences are within the run-to-run noise of 5 runs. The field rows now time `ReturnField`, whose only extra work is its result array (one `float` per node); WallWithGap 1000² crossing 2000 ms (1878.8 → 2000.3 ms) is mostly noise: the search did not change, and the new array (4 MB at 1000², allocated and zeroed once per call) costs a few ms.

| size | terrain | p50 ms before | p95 ms before | p50 ms after | p95 ms after | smooth ms | E5 before | E5 after |
|---|---|---|---|---|---|---|---|---|
| 1000 | Uniform | 13.3 | 17.4 | 14.2 | 19.3 | 2.6 | ok | ok |
| 1000 | Fractal | 55.9 | 74.3 | 60.7 | 75.8 | 0.6 | ok | ok |
| 1000 | WallWithGap | 15.0 | 456.3 | 18.1 | 452.7 | 2.8 | FAIL: p95 | FAIL: p95 |
| 1000 | Maze | 105.5 | 335.0 | 118.4 | 377.5 | 11.8 | FAIL: p95 | FAIL: p95 |
| 1000 | Blocked30 | 14.7 | 17.3 | 14.7 | 18.3 | 0.1 | ok | ok |
| 2000 | Uniform | 8.6 | 15.6 | 11.1 | 18.8 | 2.8 | ok | ok |
| 2000 | Fractal | 46.3 | 51.3 | 49.8 | 52.9 | 0.6 | ok | ok |
| 2000 | WallWithGap | 12.1 | 17.6 | 17.0 | 19.1 | 2.8 | ok | ok |
| 2000 | Maze | 77.5 | 1396.9 | 88.0 | 1549.8 | 6.2 | FAIL: p95 | FAIL: p95 |
| 2000 | Blocked30 | 16.3 | 17.8 | 16.2 | 17.8 | 0.1 | ok | ok |

What these rows do not show:

- **Longer routes.** The smoothing is quadratic in the length of the stretch it straightens. In a review probe (Release, flat diagonal route), A* + smoothing took 180–307 ms + ~40 ms at 1000² (594 nodes) and 1020–1220 ms + 172–234 ms at 2000² (1594 nodes). A* still dominates, at 15–20 % on top. E5 only covers 500 m routes, but `Alternatives` will smooth three routes.
- **Repeated fields.** Each `ReturnField` allocates its result (16 MB at 2000²) on the large-object heap. The field rows run 1 + 1, so their "0 GC" does not cover a caller that recomputes the field on every base or grid change: that caller will see gen-2 collections.
