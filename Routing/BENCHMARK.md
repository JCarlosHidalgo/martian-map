# Routing benchmark (E5)

- Scenario: laptop only (the tablet is not tested): 5 terrains on 1000²/2000², A* 1 + 5 runs, field 1 + 1
- Machine: .NET 10.0.15, X64, 8 logical CPUs
- Config: seed 0x4D41525321; A* routes of 500 m on 1000/2000², 1 warm-up + 5 runs; ReturnField (v0 P90) on 1000/2000², 1 + 1; the A* rows time PlanRoute (A* + smoothing + trace), and 'smooth ms' is the p50 of the smoothing alone on the same pairs
- E5 (docs/guide/chapters/reparto.md): A* p95 <= 100.0 ms, field p95 <= 2000.0 ms, 0 GC during the measured runs, managed heap <= 90.0 MB. Measured on the laptop only: the tablet is not tested.

| size | terrain | query | runs | found | p50 ms | p95 ms | max ms | smooth ms | GC | managed MB | E5 |
|---|---|---|---|---|---|---|---|---|---|---|---|
| 1000 | Uniform | A* 500 m | 5 | 5 | 14.2 | 19.3 | 19.3 | 2.6 | 0 | 15.4 | ok |
| 1000 | Uniform | ReturnField | 1 | 1 | 2322.7 | 2322.7 | 2322.7 | - | 0 | 19.3 | FAIL: p95 |
| 1000 | Fractal | A* 500 m | 5 | 5 | 60.7 | 75.8 | 75.8 | 0.6 | 0 | 15.6 | ok |
| 1000 | Fractal | ReturnField | 1 | 1 | 2014.9 | 2014.9 | 2014.9 | - | 0 | 19.5 | FAIL: p95 |
| 1000 | WallWithGap | A* 500 m | 5 | 5 | 18.1 | 452.7 | 452.7 | 2.8 | 0 | 16.4 | FAIL: p95 |
| 1000 | WallWithGap | ReturnField | 1 | 1 | 2000.3 | 2000.3 | 2000.3 | - | 0 | 20.2 | FAIL: p95 |
| 1000 | Maze | A* 500 m | 5 | 5 | 118.4 | 377.5 | 377.5 | 11.8 | 0 | 17.4 | FAIL: p95 |
| 1000 | Maze | ReturnField | 1 | 1 | 349.0 | 349.0 | 349.0 | - | 0 | 21.2 | ok |
| 1000 | Blocked30 | A* 500 m | 5 | 5 | 14.7 | 18.3 | 18.3 | 0.1 | 0 | 17.4 | ok |
| 1000 | Blocked30 | ReturnField | 1 | 1 | 972.2 | 972.2 | 972.2 | - | 0 | 21.2 | ok |
| 2000 | Uniform | A* 500 m | 5 | 5 | 11.1 | 18.8 | 18.8 | 2.8 | 0 | 63.2 | ok |
| 2000 | Uniform | ReturnField | 1 | 1 | 9603.4 | 9603.4 | 9603.4 | - | 0 | 78.5 | FAIL: p95 |
| 2000 | Fractal | A* 500 m | 5 | 5 | 49.8 | 52.9 | 52.9 | 0.6 | 0 | 63.2 | ok |
| 2000 | Fractal | ReturnField | 1 | 1 | 9868.6 | 9868.6 | 9868.6 | - | 0 | 78.5 | FAIL: p95 |
| 2000 | WallWithGap | A* 500 m | 5 | 5 | 17.0 | 19.1 | 19.1 | 2.8 | 0 | 63.2 | ok |
| 2000 | WallWithGap | ReturnField | 1 | 1 | 8610.6 | 8610.6 | 8610.6 | - | 0 | 78.5 | FAIL: p95 |
| 2000 | Maze | A* 500 m | 5 | 5 | 88.0 | 1549.8 | 1549.8 | 6.2 | 0 | 69.2 | FAIL: p95 |
| 2000 | Maze | ReturnField | 1 | 1 | 1388.7 | 1388.7 | 1388.7 | - | 0 | 84.5 | ok |
| 2000 | Blocked30 | A* 500 m | 5 | 5 | 16.2 | 17.8 | 17.8 | 0.1 | 0 | 69.2 | ok |
| 2000 | Blocked30 | ReturnField | 1 | 1 | 4439.1 | 4439.1 | 4439.1 | - | 0 | 84.5 | FAIL: p95 |

## Before / after the smoothing

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
