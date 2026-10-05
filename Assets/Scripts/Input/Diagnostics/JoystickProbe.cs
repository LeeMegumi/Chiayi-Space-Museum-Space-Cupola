// JoystickProbe.cs
// 搖桿偵測工具（診斷用，確認完對應後可移除）。
//
// 套用位置：Assets/Scripts/Input/Diagnostics/JoystickProbe.cs
//
// 用途：實測 THRUSTMASTER TCA Sidestick 在 Unity Input System 下的
//   * 裝置辨識（layout、產品名稱、HID 描述）
//   * 每個軸的實際範圍、回中位置、靜止時的抖動量（決定死區用）
//   * 每顆按鈕與帽子開關對應的 control path
// 不憑規格表猜測 —— 以實機數據決定綁定。
//
// 使用步驟：
//   1. 場景中任一物件掛上本元件，按 Play
//   2. 前 3 秒「不要碰搖桿」—— 量測回中位置與抖動
//   3. 把每個軸推到兩端極限、握把左右扭轉、推力桿推到底再拉回
//   4. 每顆按鈕按一次、帽子開關八個方向各按一次
//   5. 按畫面左上的「輸出報告」（或鍵盤 F12）
// 報告會寫到 _Claude/reports/joystick_probe.txt，同時印在 Console。

using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace SpaceCupola.InputDiagnostics
{
    public class JoystickProbe : MonoBehaviour
    {
        [Tooltip("開始後這段時間內視為「靜止」，用來量測回中位置與抖動（秒）。")]
        [SerializeField] private float restMeasureSeconds = 3f;

        [Tooltip("開頭這段時間不列入任何統計。裝置剛連上的頭幾幀會讀到 0，之後才跳到實際位置，\n" +
                 "若列入會被誤判成大幅抖動（第一次實測時推力桿就出現 1.0 的假抖動）。")]
        [SerializeField] private float warmupSeconds = 0.5f;

        [Tooltip("軸的變化量超過此值才算「有被操作」，用於報告中區分有用與無用的軸。")]
        [SerializeField] private float usedAxisThreshold = 0.2f;

        [SerializeField] private bool showOverlay = true;

        private class AxisStat
        {
            public AxisControl control;
            public float min = float.MaxValue, max = float.MinValue;
            public float restMin = float.MaxValue, restMax = float.MinValue, restSum; public int restCount;
            public int pressCount;       // 僅按鈕
            public bool wasPressed;
        }

        private readonly Dictionary<InputDevice, List<AxisStat>> _stats = new Dictionary<InputDevice, List<AxisStat>>();
        private readonly List<string> _events = new List<string>();
        private float _startTime;
        private string _lastReportPath;
        private Vector2 _scroll;

        private void OnEnable()
        {
            _startTime = Time.realtimeSinceStartup;
            foreach (InputDevice d in InputSystem.devices) Track(d);
            InputSystem.onDeviceChange += OnDeviceChange;
        }

        private void OnDisable() => InputSystem.onDeviceChange -= OnDeviceChange;

        private void OnDeviceChange(InputDevice device, InputDeviceChange change)
        {
            _events.Add($"{Time.realtimeSinceStartup - _startTime:F1}s  {change}  {device.displayName} ({device.layout})");
            if (change == InputDeviceChange.Added || change == InputDeviceChange.Reconnected) Track(device);
        }

        private static bool IsInteresting(InputDevice d) =>
            d is Joystick || d is Gamepad ||
            (!(d is Keyboard) && !(d is Mouse) && !(d is Pointer) && !(d is Sensor));

        private void Track(InputDevice d)
        {
            if (!IsInteresting(d) || _stats.ContainsKey(d)) return;
            var list = new List<AxisStat>();
            foreach (InputControl c in d.allControls)
                if (c is AxisControl a && !c.synthetic) list.Add(new AxisStat { control = a });
            _stats[d] = list;
        }

        private void Update()
        {
            float elapsed = Time.realtimeSinceStartup - _startTime;
            if (elapsed < warmupSeconds) return;
            bool resting = elapsed < restMeasureSeconds;

            foreach (var kv in _stats)
            {
                foreach (AxisStat s in kv.Value)
                {
                    float v = s.control.ReadValue();
                    if (v < s.min) s.min = v;
                    if (v > s.max) s.max = v;

                    if (resting)
                    {
                        if (v < s.restMin) s.restMin = v;
                        if (v > s.restMax) s.restMax = v;
                        s.restSum += v; s.restCount++;
                    }

                    if (s.control is ButtonControl b)
                    {
                        bool p = b.isPressed;
                        if (p && !s.wasPressed) s.pressCount++;
                        s.wasPressed = p;
                    }
                }
            }

            if (!resting && Keyboard.current != null && Keyboard.current.f12Key.wasPressedThisFrame) WriteReport();
        }

        [ContextMenu("輸出報告")]
        public void WriteReport()
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== Space Cupola 搖桿偵測報告 ===");
            sb.AppendLine($"時間：{System.DateTime.Now:yyyy-MM-dd HH:mm:ss}   Unity {Application.unityVersion}");
            sb.AppendLine($"量測時長：{Time.realtimeSinceStartup - _startTime:F1} 秒（前 {restMeasureSeconds} 秒為靜止量測）");
            sb.AppendLine();

            sb.AppendLine("── 所有已連接裝置 ──");
            foreach (InputDevice d in InputSystem.devices)
                sb.AppendLine($"  [{d.deviceId}] {d.displayName}  layout={d.layout}  類別={d.GetType().Name}  " +
                              $"interface={d.description.interfaceName}  product=\"{d.description.product}\"  " +
                              $"manufacturer=\"{d.description.manufacturer}\"");
            sb.AppendLine();

            foreach (var kv in _stats)
            {
                InputDevice d = kv.Key;
                sb.AppendLine($"── 裝置：{d.displayName}（layout {d.layout}，{d.GetType().Name}）──");
                sb.AppendLine($"  product=\"{d.description.product}\"  manufacturer=\"{d.description.manufacturer}\"  " +
                              $"version=\"{d.description.version}\"  serial=\"{d.description.serial}\"");

                sb.AppendLine("  [軸]  path | 範圍 min~max | 靜止中心 | 靜止抖動 | 判定");
                foreach (AxisStat s in kv.Value)
                {
                    if (s.control is ButtonControl) continue;
                    float range = s.max - s.min;
                    float restCenter = s.restCount > 0 ? s.restSum / s.restCount : float.NaN;
                    float jitter = s.restCount > 0 ? s.restMax - s.restMin : float.NaN;
                    string used = range >= usedAxisThreshold ? "★有操作" : "（未動）";
                    sb.AppendLine($"    {s.control.path,-40} {s.min,7:F3} ~ {s.max,7:F3} | {restCenter,7:F3} | {jitter,6:F4} | {used}" +
                                  $"{(s.control.noisy ? "  noisy" : "")}");
                }

                sb.AppendLine("  [按鈕]  path | 按下次數");
                foreach (AxisStat s in kv.Value)
                {
                    if (!(s.control is ButtonControl)) continue;
                    sb.AppendLine($"    {s.control.path,-40} {s.pressCount,3} 次{(s.pressCount > 0 ? "  ★" : "")}");
                }

                sb.AppendLine("  [HID 描述（完整）]");
                sb.AppendLine("    " + (d.description.capabilities ?? ""));
                sb.AppendLine();
            }

            if (_events.Count > 0)
            {
                sb.AppendLine("── 裝置連線事件 ──");
                foreach (string e in _events) sb.AppendLine("  " + e);
            }

            string report = sb.ToString();
            Debug.Log(report);

#if UNITY_EDITOR
            // 寫到 Unity 專案「外層」的 _Claude/reports/（與 Assets 無關，不進 git）
            try
            {
                string dir = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..", "..", "_Claude", "reports"));
                System.IO.Directory.CreateDirectory(dir);
                _lastReportPath = System.IO.Path.Combine(dir, "joystick_probe.txt");
                System.IO.File.WriteAllText(_lastReportPath, report, Encoding.UTF8);
                Debug.Log($"[JoystickProbe] 報告已寫入：{_lastReportPath}");
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[JoystickProbe] 無法寫入報告檔（{ex.Message}），請直接複製 Console 內容。");
            }
#endif
        }

        private void OnGUI()
        {
            if (!showOverlay) return;

            float elapsed = Time.realtimeSinceStartup - _startTime;
            GUILayout.BeginArea(new Rect(10, 10, 560, Screen.height - 20), GUI.skin.box);
            GUILayout.Label(elapsed < restMeasureSeconds
                ? $"<b>靜止量測中… 請不要碰搖桿（{restMeasureSeconds - elapsed:F1}s）</b>"
                : "<b>請把每個軸推到極限、每顆按鈕按一次，完成後按「輸出報告」或 F12</b>",
                new GUIStyle(GUI.skin.label) { richText = true, fontSize = 14 });

            // 進度：有被操作過的軸數、按過的按鈕數 —— 讓操作者知道還有哪些沒碰到
            int axesTotal = 0, axesUsed = 0, btnTotal = 0, btnUsed = 0;
            foreach (var kv in _stats)
                foreach (AxisStat s in kv.Value)
                {
                    if (s.control is ButtonControl) { btnTotal++; if (s.pressCount > 0) btnUsed++; }
                    else { axesTotal++; if (s.max - s.min >= usedAxisThreshold) axesUsed++; }
                }
            GUILayout.Label($"進度：軸 {axesUsed}/{axesTotal}　按鈕 {btnUsed}/{btnTotal}");

            GUI.enabled = elapsed >= restMeasureSeconds;   // 靜止量測期間不允許輸出，避免數據不完整
            if (GUILayout.Button("輸出報告", GUILayout.Height(28))) WriteReport();
            GUI.enabled = true;
            if (!string.IsNullOrEmpty(_lastReportPath)) GUILayout.Label("已寫入：" + _lastReportPath);

            _scroll = GUILayout.BeginScrollView(_scroll);
            foreach (var kv in _stats)
            {
                GUILayout.Label($"<b>{kv.Key.displayName}</b>  ({kv.Key.layout})", new GUIStyle(GUI.skin.label) { richText = true });
                foreach (AxisStat s in kv.Value)
                {
                    float v = s.control.ReadValue();
                    if (s.control is ButtonControl b)
                    {
                        if (b.isPressed || s.pressCount > 0)
                            GUILayout.Label($"  {(b.isPressed ? "●" : "○")} {s.control.path}  ×{s.pressCount}");
                        continue;
                    }
                    GUILayout.BeginHorizontal();
                    GUILayout.Label($"  {s.control.name}", GUILayout.Width(140));
                    GUILayout.HorizontalSlider(Mathf.InverseLerp(-1f, 1f, Mathf.Clamp(v, -1f, 1f)), 0f, 1f, GUILayout.Width(220));
                    GUILayout.Label($"{v,7:F3}  [{s.min:F2}~{s.max:F2}]");
                    GUILayout.EndHorizontal();
                }
            }
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }
    }
}
