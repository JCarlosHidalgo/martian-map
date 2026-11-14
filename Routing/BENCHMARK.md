# Routing benchmark (E5)

- Scenario: laptop only (the tablet is not tested): 5 terrains on 1000²/2000², A* 1 + 5 runs, field 1 + 1
- Machine: .NET 10.0.15, X64, 8 logical CPUs
- Config: seed 0x4D41525321; A* routes of 500 m on 1000/2000², 1 warm-up + 5 runs; field at v0 P90 on 1000/2000², 1 + 1
- E5 (docs/guide/chapters/reparto.md): A* p95 <= 100.0 ms, field p95 <= 2000.0 ms, 0 GC during the measured runs, managed heap <= 90.0 MB. Measured on the laptop only: the tablet is not tested.

| size | terrain | query | runs | found | p50 ms | p95 ms | max ms | GC | managed MB | E5 |
|---|---|---|---|---|---|---|---|---|---|---|
| 1000 | Uniform | A* 500 m | 5 | 5 | 36.2 | 55.0 | 55.0 | 0 | 15.4 | ok |
| 1000 | Uniform | field | 1 | 1 | 1592.1 | 1592.1 | 1592.1 | 0 | 19.3 | ok |
| 1000 | Fractal | A* 500 m | 5 | 5 | 40.6 | 52.2 | 52.2 | 0 | 22.3 | ok |
| 1000 | Fractal | field | 1 | 1 | 1463.4 | 1463.4 | 1463.4 | 0 | 19.5 | ok |
| 1000 | WallWithGap | A* 500 m | 5 | 5 | 10.0 | 299.3 | 299.3 | 0 | 23.1 | FAIL: p95 |
| 1000 | WallWithGap | field | 1 | 1 | 1272.1 | 1272.1 | 1272.1 | 0 | 20.2 | ok |
| 1000 | Maze | A* 500 m | 5 | 5 | 81.5 | 273.6 | 273.6 | 0 | 24.7 | FAIL: p95 |
| 1000 | Maze | field | 1 | 1 | 277.4 | 277.4 | 277.4 | 0 | 21.2 | ok |
| 1000 | Blocked30 | A* 500 m | 5 | 5 | 11.1 | 12.9 | 12.9 | 0 | 24.1 | ok |
| 1000 | Blocked30 | field | 1 | 1 | 709.6 | 709.6 | 709.6 | 0 | 21.2 | ok |
| 2000 | Uniform | A* 500 m | 5 | 5 | 7.5 | 17.1 | 17.1 | 0 | 69.8 | ok |
| 2000 | Uniform | field | 1 | 1 | 6688.0 | 6688.0 | 6688.0 | 0 | 78.5 | FAIL: p95 |
| 2000 | Fractal | A* 500 m | 5 | 5 | 35.6 | 39.0 | 39.0 | 0 | 89.9 | ok |
| 2000 | Fractal | field | 1 | 1 | 7297.5 | 7297.5 | 7297.5 | 0 | 78.5 | FAIL: p95 |
| 2000 | WallWithGap | A* 500 m | 5 | 5 | 8.8 | 11.8 | 11.8 | 0 | 89.9 | ok |
| 2000 | WallWithGap | field | 1 | 1 | 6067.1 | 6067.1 | 6067.1 | 0 | 78.5 | FAIL: p95 |
| 2000 | Maze | A* 500 m | 5 | 5 | 61.9 | 1074.0 | 1074.0 | 0 | 96.3 | FAIL: p95 MB |
| 2000 | Maze | field | 1 | 1 | 1056.7 | 1056.7 | 1056.7 | 0 | 84.5 | ok |
| 2000 | Blocked30 | A* 500 m | 5 | 5 | 12.1 | 13.3 | 13.3 | 0 | 95.9 | FAIL: MB |
| 2000 | Blocked30 | field | 1 | 1 | 3298.5 | 3298.5 | 3298.5 | 0 | 84.5 | FAIL: p95 |
