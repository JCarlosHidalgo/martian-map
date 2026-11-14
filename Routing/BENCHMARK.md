# Routing benchmark (E5)

- Scenario: laptop only (the tablet is not tested): 5 terrains on 1000²/2000², A* 1 + 5 runs, field 1 + 1
- Machine: .NET 10.0.15, X64, 8 logical CPUs
- Config: seed 0x4D41525321; A* routes of 500 m on 1000/2000², 1 warm-up + 5 runs; field at v0 P90 on 1000/2000², 1 + 1
- E5 (docs/guide/chapters/reparto.md): A* p95 <= 100.0 ms, field p95 <= 2000.0 ms, 0 GC during the measured runs, managed heap <= 90.0 MB. Measured on the laptop only: the tablet is not tested.

| size | terrain | query | runs | found | p50 ms | p95 ms | max ms | GC | managed MB | E5 |
|---|---|---|---|---|---|---|---|---|---|---|
| 1000 | Uniform | A* 500 m | 5 | 5 | 13.3 | 17.4 | 17.4 | 0 | 15.4 | ok |
| 1000 | Uniform | field | 1 | 1 | 2210.9 | 2210.9 | 2210.9 | 0 | 19.3 | FAIL: p95 |
| 1000 | Fractal | A* 500 m | 5 | 5 | 55.9 | 74.3 | 74.3 | 0 | 15.6 | ok |
| 1000 | Fractal | field | 1 | 1 | 2116.9 | 2116.9 | 2116.9 | 0 | 19.5 | FAIL: p95 |
| 1000 | WallWithGap | A* 500 m | 5 | 5 | 15.0 | 456.3 | 456.3 | 0 | 16.4 | FAIL: p95 |
| 1000 | WallWithGap | field | 1 | 1 | 1878.8 | 1878.8 | 1878.8 | 0 | 20.2 | ok |
| 1000 | Maze | A* 500 m | 5 | 5 | 105.5 | 335.0 | 335.0 | 0 | 17.4 | FAIL: p95 |
| 1000 | Maze | field | 1 | 1 | 340.1 | 340.1 | 340.1 | 0 | 21.2 | ok |
| 1000 | Blocked30 | A* 500 m | 5 | 5 | 14.7 | 17.3 | 17.3 | 0 | 17.4 | ok |
| 1000 | Blocked30 | field | 1 | 1 | 937.6 | 937.6 | 937.6 | 0 | 21.2 | ok |
| 2000 | Uniform | A* 500 m | 5 | 5 | 8.6 | 15.6 | 15.6 | 0 | 63.2 | ok |
| 2000 | Uniform | field | 1 | 1 | 9282.0 | 9282.0 | 9282.0 | 0 | 78.5 | FAIL: p95 |
| 2000 | Fractal | A* 500 m | 5 | 5 | 46.3 | 51.3 | 51.3 | 0 | 63.2 | ok |
| 2000 | Fractal | field | 1 | 1 | 9472.2 | 9472.2 | 9472.2 | 0 | 78.5 | FAIL: p95 |
| 2000 | WallWithGap | A* 500 m | 5 | 5 | 12.1 | 17.6 | 17.6 | 0 | 63.2 | ok |
| 2000 | WallWithGap | field | 1 | 1 | 8476.9 | 8476.9 | 8476.9 | 0 | 78.5 | FAIL: p95 |
| 2000 | Maze | A* 500 m | 5 | 5 | 77.5 | 1396.9 | 1396.9 | 0 | 69.2 | FAIL: p95 |
| 2000 | Maze | field | 1 | 1 | 1384.8 | 1384.8 | 1384.8 | 0 | 84.5 | ok |
| 2000 | Blocked30 | A* 500 m | 5 | 5 | 16.3 | 17.8 | 17.8 | 0 | 69.2 | ok |
| 2000 | Blocked30 | field | 1 | 1 | 4407.1 | 4407.1 | 4407.1 | 0 | 84.5 | FAIL: p95 |
