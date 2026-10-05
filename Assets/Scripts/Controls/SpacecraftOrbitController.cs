// SpacecraftOrbitController.cs
// 以 THRUSTMASTER TCA Sidestick（Unity 辨識為「T.A320 Copilot」）操控環繞地球的相機。
//
// 套用位置：Assets/Scripts/Controls/SpacecraftOrbitController.cs
// 掛在 Main Camera（與 CameraControllerInSpace 同一物件）。
// 元件啟用時由 CameraControllerInSpace.Update 轉交控制；停用即恢復原本的滑鼠操作。
//
// ── 設計（使用者確認）─────────────────────────────────────────────
//   * 保留原本的 Pivot 軌道模型：相機是 Pivot 的子物件，旋轉 Pivot 環繞、縮放 Pivot 改變高度。
//     觀眾永遠不會飛離地球或迷路。
//   * 搖桿前後左右：沿經線／緯線環繞（高度越低轉得越慢，地表移動速度感一致）
//   * 握把扭轉：同樣是左右環繞
//   * 推力桿：位置直接對應高度，往前推＝靠近地球
//   * 長按扳機 2 秒：回到初始視角（扳機最常被亂按，所以要長按）
//   * 閒置 60 秒：自動回到初始高度與角度；地球本身每秒自轉 1°（UnitEarth），不另加相機旋轉
//
// ── 實測數據（_Claude/reports/joystick_probe.txt）─────────────────
//   stick/x, stick/y  −1~1，回中 0，靜止抖動 0
//   rz（扭轉）       −0.947~1，回中 0     → 綁定處理器 AxisDeadzone(max=0.94) 讓兩側都能滿格
//   slider（推力桿）  −1~1，拉到底為 −1
//   死區 0.08：靜止抖動雖為 0，但推過放手後不一定回到 0
//
// ── 推力桿「接管」機制 ──────────────────────────────────────────
// 推力桿是絕對位置，若閒置回正或長按重置後立刻讓它生效，高度會瞬間跳回拉桿的位置。
// 因此回正／重置後推力桿先「解除控制」，要被推動超過 throttleTakeoverThreshold 才重新接管。

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Utilities;

namespace SpaceCupola.Controls
{
    [RequireComponent(typeof(CameraControllerInSpace))]
    public class SpacecraftOrbitController : MonoBehaviour
    {
        private const string ResourcePath = "SpaceCupolaControls";
        private const string MapName = "Spacecraft";

        [Header("輸入")]
        [Tooltip("留空則自動載入 Resources/SpaceCupolaControls.inputactions。")]
        [SerializeField] private InputActionAsset actions;

        [Header("環繞")]
        [Tooltip("搖桿推到底時的環繞速度（度／秒），此為預設高度下的值。")]
        [SerializeField] private float orbitSpeed = 40f;

        [Tooltip("搖桿響應曲線指數。>1 讓中心附近更細膩、推到底才全速。")]
        [Range(1f, 3f)]
        [SerializeField] private float responseExponent = 1.6f;

        [Tooltip("握把扭轉對左右環繞的貢獻比例。")]
        [Range(0f, 1f)]
        [SerializeField] private float twistWeight = 0.6f;

        [Tooltip("方向顛倒時勾選。預設：推桿往前＝往北、往右＝往東。")]
        [SerializeField] private bool invertPitch = false;
        [SerializeField] private bool invertYaw = false;
        [SerializeField] private bool invertTwist = false;

        [Tooltip("俯仰角限制（度）。±80° 可以從極區上空俯瞰極光，又不會翻過極點。")]
        [SerializeField] private Vector2 pitchLimits = new Vector2(-80f, 80f);

        [Header("高度（推力桿）")]
        [Tooltip("最近的 Pivot 縮放。相機本地距離 16 × 此值 = 到地心距離；地球半徑 5。0.45 → 距地表約 2.2。")]
        [SerializeField] private float nearScale = 0.45f;

        [Tooltip("最遠的 Pivot 縮放。1.6 → 距地心 25.6，可看到完整地球與周圍太空。")]
        [SerializeField] private float farScale = 1.6f;

        [Tooltip("勾選：推力桿往前推＝靠近地球。")]
        [SerializeField] private bool throttleForwardIsNear = true;

        [Tooltip("最低高度時的環繞速度倍率。越靠近地表轉越慢，地表的移動速度感才會一致。")]
        [Range(0.1f, 1f)]
        [SerializeField] private float nearSpeedFactor = 0.35f;

        [Tooltip("推力桿需移動超過此量才會重新接管高度（見檔頭說明）。")]
        [Range(0.01f, 0.5f)]
        [SerializeField] private float throttleTakeoverThreshold = 0.05f;

        [Header("平滑")]
        [SerializeField] private float rotationDamping = 6f;
        [SerializeField] private float zoomDamping = 4f;

        [Header("滑鼠（無搖桿或除錯用）")]
        [Tooltip("按住右鍵拖曳環繞，滾輪改變高度。")]
        [SerializeField] private float mouseDegreesPerPixel = 0.15f;
        [SerializeField] private float mouseZoomStep = 0.1f;

        [Header("閒置自動回正")]
        [SerializeField] private float idleSeconds = 60f;
        [Tooltip("回正動作漸入所需時間（秒），避免突然開始移動。")]
        [SerializeField] private float attractBlendSeconds = 4f;
        [Tooltip("初始／回正的俯仰角。20° 讓畫面偏北半球（台灣位於北緯約 23.5°）。")]
        [SerializeField] private float defaultPitch = 20f;
        [SerializeField] private float defaultScale = 1f;
        [Tooltip("回正時相機額外的環繞速度（度／秒）。地球本身已每秒自轉 1°，預設不另加。")]
        [SerializeField] private float attractYawSpeed = 0f;

        [Header("除錯")]
        [SerializeField] private bool showDebugOverlay = false;

        private InputAction _orbit, _twist, _throttle, _reset, _mouseHold, _mouseDelta, _mouseZoom;

        private bool  _initialized;
        private float _yaw, _pitch, _scale;          // 目標值；Pivot 以阻尼追隨
        private float _idle;
        private float _attractWeight;
        private bool  _resetQueued;

        private bool  _throttleControls;             // 推力桿目前是否控制高度
        private float _throttleArmValue;             // 解除控制時推力桿的位置
        private float _lastThrottle = float.NaN;

        /// <summary>目前是否處於閒置回正狀態。</summary>
        public bool IsAttractMode => _attractWeight > 0.001f;

        /// <summary>距離上次有人操作經過的秒數。</summary>
        public float IdleTime => _idle;

        // ── 由 ControlSessionManager 控制（雙搖桿／待機畫面）─────────────
        private bool _managed;          // 受操控權管理：解鎖期間不自行閒置回正（改由管理器返回待機）
        private bool _locked;           // 鎖定：忽略所有輸入，持續回正（待機畫面）
        private InputDevice _activeJoystick;
        private float _baseOrbitSpeed = -1f;

        /// <summary>
        /// 鎖定時忽略所有輸入並回到初始視角（待機畫面）；解鎖時交還操控，推力桿需被推動才接管高度。
        /// 呼叫後此元件即視為受管理：解鎖期間不再自行閒置回正。
        /// </summary>
        public void SetLocked(bool locked)
        {
            _managed = true;
            _locked = locked;
            _resetQueued = false;
            if (locked)
            {
                _idle = Mathf.Max(_idle, idleSeconds);   // 立即開始回正
            }
            else
            {
                _idle = 0f;
                _attractWeight = 0f;
                _lastThrottle = float.NaN;
                DisarmThrottle();
            }
        }

        /// <summary>
        /// 限定只接受這支搖桿（加上鍵盤、滑鼠供工作人員使用）。另一支搖桿的輸入在 Input System 層即被排除。
        /// 傳入 null 表示只接受鍵盤、滑鼠。
        /// </summary>
        public void SetActiveJoystick(InputDevice joystick)
        {
            _activeJoystick = joystick;
            if (actions == null) return;

            var list = new List<InputDevice>(3);
            if (joystick != null) list.Add(joystick);
            if (Keyboard.current != null) list.Add(Keyboard.current);
            if (Mouse.current != null) list.Add(Mouse.current);
            actions.devices = new ReadOnlyArray<InputDevice>(list.ToArray());
            DisarmThrottle();
        }

        /// <summary>套用操控手感：速度倍率（相對於 Inspector 設定的 orbitSpeed）與響應曲線指數。</summary>
        public void ApplyProfile(float speedMultiplier, float exponent)
        {
            if (_baseOrbitSpeed < 0f) _baseOrbitSpeed = orbitSpeed;
            orbitSpeed = _baseOrbitSpeed * Mathf.Max(0.05f, speedMultiplier);
            responseExponent = Mathf.Clamp(exponent, 1f, 3f);
        }

        private bool HasJoystick =>
            _activeJoystick != null ? _activeJoystick.added
                                    : (!_managed && Joystick.current != null);

        // ─────────────────────────────────────────────────────────────

        private void Awake()
        {
            if (actions == null) actions = Resources.Load<InputActionAsset>(ResourcePath);
            if (actions == null)
            {
                Debug.LogWarning($"[SpacecraftOrbitController] 找不到 Resources/{ResourcePath}.inputactions，停用搖桿操控。", this);
                enabled = false;
                return;
            }

            InputActionMap map = actions.FindActionMap(MapName, throwIfNotFound: true);
            _orbit      = map.FindAction("Orbit", true);
            _twist      = map.FindAction("Twist", true);
            _throttle   = map.FindAction("Throttle", true);
            _reset      = map.FindAction("ResetView", true);
            _mouseHold  = map.FindAction("MouseOrbitHold", true);
            _mouseDelta = map.FindAction("MouseDelta", true);
            _mouseZoom  = map.FindAction("MouseZoom", true);

            _reset.performed += OnResetPerformed;
        }

        private void OnEnable()
        {
            if (actions != null) actions.Enable();
            InputSystem.onDeviceChange += OnDeviceChange;
        }

        private void OnDisable()
        {
            if (actions != null) actions.Disable();
            InputSystem.onDeviceChange -= OnDeviceChange;
        }

        private void OnDestroy()
        {
            if (_reset != null) _reset.performed -= OnResetPerformed;
        }

        private void OnResetPerformed(InputAction.CallbackContext _) => _resetQueued = true;

        private void OnDeviceChange(InputDevice device, InputDeviceChange change)
        {
            if (!(device is Joystick)) return;
            if (change == InputDeviceChange.Removed || change == InputDeviceChange.Disconnected)
                Debug.LogWarning($"[SpacecraftOrbitController] 搖桿中斷：{device.displayName}。暫以滑鼠操作。");
            else if (change == InputDeviceChange.Added || change == InputDeviceChange.Reconnected)
            {
                Debug.Log($"[SpacecraftOrbitController] 搖桿已連接：{device.displayName}。");
                DisarmThrottle();   // 重新連上時不要讓推力桿位置瞬間改變高度
            }
        }

        // ─────────────────────────────────────────────────────────────

        /// <summary>由 CameraControllerInSpace.Update 每幀呼叫。</summary>
        public void Tick(Transform pivot)
        {
            if (pivot == null || _orbit == null) return;

            if (!_initialized)
            {
                Vector3 e = pivot.rotation.eulerAngles;
                _pitch = Mathf.DeltaAngle(0f, e.x);
                _yaw   = e.y;
                _scale = pivot.localScale.x;
                DisarmThrottle();
                _initialized = true;
            }

            float dt = Time.unscaledDeltaTime;
            bool active = false;

            // ── 環繞速度依高度調整 ──
            float lo = Mathf.Min(nearScale, farScale), hi = Mathf.Max(nearScale, farScale);
            float altitude01 = Mathf.InverseLerp(lo, hi, _scale);
            float speed = orbitSpeed * Mathf.Lerp(nearSpeedFactor, 1f, altitude01);

            // ── 鎖定（待機畫面）：不讀任何輸入 ──
            if (_locked)
            {
                _resetQueued = false;
                _idle = Mathf.Max(_idle + dt, idleSeconds);
            }
            else
            {
                active = ReadInputs(dt, speed);
            }

            ApplyIdleAndPivot(pivot, dt, active, lo, hi);
        }

        /// <summary>讀取搖桿、扭轉、滑鼠、推力桿與重置鍵。回傳本幀是否有人操作。</summary>
        private bool ReadInputs(float dt, float speed)
        {
            bool active = false;

            // ── 搖桿 + 扭轉（鍵盤方向鍵與 Q/E 亦綁在同一動作，供測試）──
            Vector2 stick = ShapeStick(_orbit.ReadValue<Vector2>());
            float twist = ShapeAxis(_twist.ReadValue<float>()) * (invertTwist ? -1f : 1f);
            if (stick.sqrMagnitude > 0f || twist != 0f) active = true;

            float yawInput = stick.x + twist * twistWeight;
            // Pivot 以 Euler(pitch, yaw, 0) 旋轉：yaw 正向會讓相機往 −x 移動（往西），
            // 因此推桿往右（往東）要讓 yaw 減少。
            _yaw   += (invertYaw ? yawInput : -yawInput) * speed * dt;
            _pitch += (invertPitch ? -stick.y : stick.y) * speed * dt;

            // ── 滑鼠：右鍵拖曳（抓著地球轉的手感，與原本相同）──
            if (_mouseHold.IsPressed())
            {
                Vector2 d = _mouseDelta.ReadValue<Vector2>();
                if (d.sqrMagnitude > 0f)
                {
                    active = true;
                    _yaw   += d.x * mouseDegreesPerPixel;
                    _pitch -= d.y * mouseDegreesPerPixel;
                }
            }

            float scroll = _mouseZoom.ReadValue<float>();
            if (Mathf.Abs(scroll) > 0.01f)
            {
                active = true;
                _scale *= 1f - Mathf.Sign(scroll) * mouseZoomStep;
                DisarmThrottle();   // 滾輪調整後，推力桿要被推動才重新接管
            }

            // ── 推力桿：位置對應高度 ──
            if (HasJoystick)
            {
                float th = _throttle.ReadValue<float>();   // −1（拉到底）~ 1（推到底）
                if (!float.IsNaN(_lastThrottle) && Mathf.Abs(th - _lastThrottle) > 0.01f) active = true;
                _lastThrottle = th;

                if (!_throttleControls && Mathf.Abs(th - _throttleArmValue) > throttleTakeoverThreshold)
                    _throttleControls = true;

                if (_throttleControls)
                {
                    float t01 = (th + 1f) * 0.5f;
                    if (!throttleForwardIsNear) t01 = 1f - t01;
                    // 以對數內插：高度感知上較均勻（近處的變化不會擠在推力桿末端一小段）
                    _scale = Mathf.Exp(Mathf.Lerp(Mathf.Log(farScale), Mathf.Log(nearScale), t01));
                }
            }

            // ── 長按扳機 2 秒：回到初始視角 ──
            if (_resetQueued)
            {
                _resetQueued = false;
                _pitch = defaultPitch;
                _scale = defaultScale;
                DisarmThrottle();
                active = true;
                Debug.Log("[SpacecraftOrbitController] 回到初始視角。");
            }

            return active;
        }

        private void ApplyIdleAndPivot(Transform pivot, float dt, bool active, float lo, float hi)
        {
            // ── 閒置回正 ──
            // 受管理且解鎖（操控中）時不自行回正：由 ControlSessionManager 在 60 秒後返回待機畫面。
            bool attractAllowed = !_managed || _locked;

            if (active)
            {
                _idle = 0f;
                _attractWeight = 0f;
            }
            else
            {
                _idle += dt;
            }

            if (attractAllowed && _idle >= idleSeconds)
            {
                if (_attractWeight <= 0f) DisarmThrottle();   // 進入回正：推力桿需被推動才重新接管
                _attractWeight = Mathf.MoveTowards(_attractWeight, 1f, dt / Mathf.Max(0.01f, attractBlendSeconds));
                float k = 1f - Mathf.Exp(-dt * 0.8f * _attractWeight);
                _pitch = Mathf.Lerp(_pitch, defaultPitch, k);
                _scale = Mathf.Lerp(_scale, defaultScale, k);
                _yaw  += attractYawSpeed * _attractWeight * dt;
            }

            // ── 限制與套用 ──
            _pitch = Mathf.Clamp(_pitch, pitchLimits.x, pitchLimits.y);
            _scale = Mathf.Clamp(_scale, lo, hi);
            _yaw   = Mathf.Repeat(_yaw, 360f);

            Quaternion target = Quaternion.Euler(_pitch, _yaw, 0f);
            pivot.rotation = Quaternion.Slerp(pivot.rotation, target, 1f - Mathf.Exp(-rotationDamping * dt));

            float s = Mathf.Lerp(pivot.localScale.x, _scale, 1f - Mathf.Exp(-zoomDamping * dt));
            pivot.localScale = Vector3.one * s;
        }

        // ─────────────────────────────────────────────────────────────

        private void DisarmThrottle()
        {
            _throttleControls = false;
            _throttleArmValue = _throttle != null ? _throttle.ReadValue<float>() : 0f;
        }

        /// <summary>搖桿響應曲線：保留方向，對幅度套用指數。</summary>
        private Vector2 ShapeStick(Vector2 v)
        {
            float m = v.magnitude;
            if (m < 1e-4f) return Vector2.zero;
            return v / m * Mathf.Pow(Mathf.Min(m, 1f), responseExponent);
        }

        private float ShapeAxis(float v)
        {
            if (Mathf.Abs(v) < 1e-4f) return 0f;
            return Mathf.Sign(v) * Mathf.Pow(Mathf.Min(Mathf.Abs(v), 1f), responseExponent);
        }

        private void OnGUI()
        {
            if (!showDebugOverlay || _orbit == null) return;
            GUILayout.BeginArea(new Rect(Screen.width - 330, 10, 320, 250), GUI.skin.box);
            InputDevice shown = _activeJoystick != null ? _activeJoystick : Joystick.current;
            GUILayout.Label($"搖桿：{(HasJoystick && shown != null ? $"{shown.displayName} (#{shown.deviceId})" : "未連接（滑鼠模式）")}");
            GUILayout.Label($"狀態：{(_locked ? "鎖定（待機）" : _managed ? "操控中" : "獨立模式")}");
            GUILayout.Label($"stick {_orbit.ReadValue<Vector2>()}   twist {_twist.ReadValue<float>():F2}");
            GUILayout.Label($"throttle {_throttle.ReadValue<float>():F2}   控制中：{_throttleControls}");
            GUILayout.Label($"yaw {_yaw:F1}°  pitch {_pitch:F1}°  scale {_scale:F2}");
            GUILayout.Label($"閒置 {_idle:F0}s / {idleSeconds:F0}s   回正權重 {_attractWeight:F2}");
            GUILayout.EndArea();
        }
    }
}
