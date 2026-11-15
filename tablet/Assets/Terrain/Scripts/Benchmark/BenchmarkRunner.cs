using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace MartianMap.Terrain
{
    /// <summary>
    /// The benchmark scene P4 builds for P3 (Reparto, "P3: motor de rutas y ediciones"; E5 and E7).
    /// Button 1 calls <c>Routing.Benchmark.Run</c> on the main thread and shows what it returns.
    /// Button 2 (E7) measures the frame time at rest, then runs it in <c>Task.Run</c> and measures
    /// again: the criterion is p95 rising by ≤ 2 ms. Everything also goes to the log (logcat).
    /// The UI is built in code, with uGUI's legacy Text, so the scene has no asset dependencies.
    /// </summary>
    public sealed class BenchmarkRunner : MonoBehaviour
    {
        private const float E7LimitMs = 2f;

        [Tooltip("Seconds of frame time measured at rest before the background run (E7).")]
        [SerializeField] private float idleSeconds = 5f;

        [SerializeField] private int targetFrameRate = 60;

        private readonly ConcurrentQueue<string> _pending = new ConcurrentQueue<string>();
        private readonly List<string> _lines = new List<string>();
        private readonly List<float> _frameMs = new List<float>();

        private Text _output;
        private Button _runButton;
        private Button _backgroundButton;
        private bool _busy;
        private bool _recording;

        private void Awake()
        {
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = targetFrameRate;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
            BuildUi();
            Print($"Device: {SystemInfo.deviceModel} | {SystemInfo.graphicsDeviceType} | " +
                  $"RAM {SystemInfo.systemMemorySize} MB | {SystemInfo.processorType} x{SystemInfo.processorCount}");
            Print($"Screen {Screen.width}x{Screen.height} @ {Screen.dpi:F0} dpi, target {targetFrameRate} fps");
        }

        private void Update()
        {
            if (_recording)
            {
                _frameMs.Add(Time.unscaledDeltaTime * 1000f);
            }

            var changed = false;
            while (_pending.TryDequeue(out var line))
            {
                _lines.Add(line);
                changed = true;
            }

            if (changed)
            {
                if (_lines.Count > 60)
                {
                    _lines.RemoveRange(0, _lines.Count - 60);
                }

                _output.text = string.Join("\n", _lines);
            }
        }

        /// <summary>Button 1: Benchmark.Run on the main thread (blocks the UI while it runs).</summary>
        public void RunOnMainThread()
        {
            if (_busy)
            {
                return;
            }

            Print("— Benchmark.Run on the main thread —");
            var started = Time.realtimeSinceStartup;
            var result = SafeRun();
            Print(result);
            Print($"Wall time: {(Time.realtimeSinceStartup - started) * 1000f:F0} ms");
        }

        /// <summary>Button 2 (E7): frame time at rest, then during Benchmark.Run in Task.Run.</summary>
        public async void RunInBackground()
        {
            if (_busy)
            {
                return;
            }

            _busy = true;
            SetButtons(false);
            try
            {
                Print($"— E7: measuring {idleSeconds:F0} s at rest —");
                var idle = await RecordFramesAsync(Task.Delay(TimeSpan.FromSeconds(idleSeconds)));

                Print("— E7: Benchmark.Run in Task.Run —");
                string result = null;
                var during = await RecordFramesAsync(Task.Run(() => result = SafeRun()));
                Print(result);

                var idleP95 = Percentile(idle, 0.95f);
                var duringP95 = Percentile(during, 0.95f);
                var delta = duringP95 - idleP95;
                Print($"Frame time p95: rest {idleP95:F2} ms ({idle.Count} frames), " +
                      $"during {duringP95:F2} ms ({during.Count} frames), delta {delta:+0.00;-0.00} ms");
                Print(delta <= E7LimitMs
                    ? $"E7 PASS: p95 rose ≤ {E7LimitMs} ms"
                    : $"E7 FAIL: p95 rose > {E7LimitMs} ms (plan B: chunk the field in a coroutine or precompute it in the habitat)");
            }
            finally
            {
                _busy = false;
                SetButtons(true);
            }
        }

        private async Task<List<float>> RecordFramesAsync(Task work)
        {
            _frameMs.Clear();
            _recording = true;
            try
            {
                await work;
            }
            finally
            {
                _recording = false;
            }

            return new List<float>(_frameMs);
        }

        private string SafeRun()
        {
            try
            {
                return MartianMap.Routing.Benchmark.Run(Print) ?? "(Benchmark.Run returned null)";
            }
            catch (NotImplementedException)
            {
                return "Benchmark.Run is not implemented yet (P3 fills it in H2–4).";
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                return $"Benchmark.Run threw {exception.GetType().Name}: {exception.Message}";
            }
        }

        /// <summary>Thread-safe: queued for the screen, written to the log (logcat) at once.</summary>
        private void Print(string line)
        {
            Debug.Log($"[Terrain] benchmark {line}");
            _pending.Enqueue(line);
        }

        private static float Percentile(List<float> values, float q)
        {
            if (values.Count == 0)
            {
                return 0f;
            }

            values.Sort();
            var index = Mathf.Clamp(Mathf.CeilToInt(q * values.Count) - 1, 0, values.Count - 1);
            return values[index];
        }

        private void SetButtons(bool interactable)
        {
            _runButton.interactable = interactable;
            _backgroundButton.interactable = interactable;
        }

        private void BuildUi()
        {
            if (FindAnyObjectByType<EventSystem>() == null)
            {
                new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            }

            var canvasGo = new GameObject("BenchmarkCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1152f, 720f);
            scaler.matchWidthOrHeight = 0.5f;

            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _runButton = MakeButton(canvasGo.transform, font, "Run Benchmark.Run", new Vector2(20f, -20f), RunOnMainThread);
            _backgroundButton = MakeButton(canvasGo.transform, font, "E7: run in background", new Vector2(340f, -20f), RunInBackground);

            var textGo = new GameObject("Output", typeof(RectTransform), typeof(Text));
            textGo.transform.SetParent(canvasGo.transform, false);
            var rect = textGo.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.offsetMin = new Vector2(20f, 20f);
            rect.offsetMax = new Vector2(-20f, -100f);
            _output = textGo.GetComponent<Text>();
            _output.font = font;
            _output.fontSize = 16;
            _output.color = new Color(0.85f, 0.95f, 0.85f);
            _output.alignment = TextAnchor.LowerLeft;
            _output.verticalOverflow = VerticalWrapMode.Truncate;
            _output.horizontalOverflow = HorizontalWrapMode.Wrap;
        }

        private static Button MakeButton(Transform parent, Font font, string label, Vector2 topLeft, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject(label, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = topLeft;
            rect.sizeDelta = new Vector2(300f, 60f);
            go.GetComponent<Image>().color = new Color(0.2f, 0.35f, 0.5f);
            go.GetComponent<Button>().onClick.AddListener(onClick);

            var textGo = new GameObject("Label", typeof(RectTransform), typeof(Text));
            textGo.transform.SetParent(go.transform, false);
            var textRect = textGo.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = textRect.offsetMax = Vector2.zero;
            var text = textGo.GetComponent<Text>();
            text.font = font;
            text.text = label;
            text.fontSize = 20;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            return go.GetComponent<Button>();
        }
    }
}
