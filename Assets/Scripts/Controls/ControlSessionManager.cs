// ControlSessionManager.cs
// 雙搖桿操控權管理：待機畫面 → 某支搖桿取得操控權 → 返回待機。
//
// 套用位置：Assets/Scripts/Controls/ControlSessionManager.cs
// 由 Editor 工具建立：Tools > Space Cupola > 4. 建立待機畫面與雙搖桿管理
//
// ── 規則（使用者確認）───────────────────────────────────────────
//   待機畫面：相機自動回到初始視角，兩支搖桿都可操作；任一支被推動即取得操控權。
//             畫面左側「小孩專用」、右側「大人專用」，對應現場擺放位置。
//   操控中  ：只有取得操控權的那支有效；另一支在 Input System 層即被排除（InputActionAsset.devices）。
//             小孩搖桿：環繞速度 70%、響應曲線較平緩。
//   返回待機：(1) 無人操作 60 秒（最後 10 秒倒數提示，動一下即取消）
//             (2) 操控中的搖桿長按「退出鍵」2 秒（畫面顯示進度圈，放開即取消）
//             (3) 操控中的搖桿被拔除
//
// ── 工作人員設定（鍵盤 F9）──────────────────────────────────────
//   依序：推動小孩搖桿 → 推動大人搖桿 → 按下要當作退出鍵的按鈕。Esc 取消。
//   退出鍵用「按一下」的方式指定，不需要知道它在 Unity 中是 button 幾號。
//   扳機不可設為退出鍵（扳機長按 2 秒已用於回到初始視角）。
//
// ── 其他工作人員按鍵 ────────────────────────────────────────────
//   Esc（操控中）：立即返回待機　　Space（待機中）：以鍵盤滑鼠開始（無搖桿測試用）

using System.Collections.Generic;
using SpaceCupola.UI;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace SpaceCupola.Controls
{
    public class ControlSessionManager : MonoBehaviour
    {
        public enum State { Standby, Playing, Setup }

        [Header("參照（由 Editor 工具自動指定）")]
        [SerializeField] private SpacecraftOrbitController orbit;
        [SerializeField] private ControlSessionUI ui;
        [Tooltip("操控中才顯示的物件（資訊面板）。")]
        [SerializeField] private CanvasGroup infoPanel;
        [Tooltip("操控中才顯示的物件（畫面中央準心）。")]
        [SerializeField] private GameObject reticle;

        [Header("返回待機")]
        [SerializeField] private float idleTimeout = 60f;
        [SerializeField] private float countdownSeconds = 10f;
        [SerializeField] private float exitHoldSeconds = 2f;

        [Header("取得操控權")]
        [Tooltip("返回待機後的冷卻時間（秒）。避免剛放開退出鍵、或手還搭在搖桿上就立刻重新取得操控權。")]
        [SerializeField] private float standbyGraceSeconds = 1.5f;
        [SerializeField, Range(0.1f, 1f)] private float stickThreshold = 0.35f;
        [SerializeField, Range(0.1f, 1f)] private float twistThreshold = 0.40f;
        [Tooltip("推力桿需離開進入待機時的位置多少才算操作。")]
        [SerializeField, Range(0.05f, 1f)] private float throttleThreshold = 0.15f;
        [Tooltip("取得操控權後，被選中的卡片停留多久才淡出待機畫面（秒）。")]
        [SerializeField] private float selectFlashSeconds = 0.6f;

        [Header("操控手感")]
        [SerializeField] private float adultSpeed = 1.0f;
        [SerializeField] private float adultExponent = 1.6f;
        [SerializeField] private float childSpeed = 0.7f;
        [SerializeField] private float childExponent = 2.0f;

        [Header("文字")]
        [SerializeField] private string exitHintText = "長按搖桿上的退出鍵 2 秒，返回待機畫面";
        [SerializeField] private string exitHintNoButton = "無人操作 60 秒後自動返回待機畫面";

        private State _state;
        private float _stateTime;
        private Seat _seat;
        private InputDevice _active;
        private float _exitHold;
        private bool _hasPending;              // 已選中、等待卡片閃一下再開始
        private InputDevice _pending;          // 選中的搖桿（鍵盤開始時為 null）
        private Seat _pendingSeat;
        private readonly Dictionary<InputDevice, float> _throttleBaseline = new Dictionary<InputDevice, float>();

        // 進入待機後，搖桿必須先「完全放開」一次才可取得操控權。
        // 否則長按退出鍵 2 秒返回待機時，手還沒放開，冷卻一過就會立刻又取得操控權 —— 等於退不出去。
        private readonly HashSet<InputDevice> _armed = new HashSet<InputDevice>();

        // 設定模式
        private int _setupStep;
        private InputDevice _setupChild, _setupAdult;
        private bool _setupWaitRelease;
        private float _setupDoneTimer;

        public State CurrentState => _state;
        public Seat ActiveSeat => _seat;

        // ─────────────────────────────────────────────────────────────

        private void Start()
        {
            if (orbit == null && Camera.main != null) orbit = Camera.main.GetComponent<SpacecraftOrbitController>();
            if (orbit == null)
            {
                Debug.LogError("[ControlSessionManager] 找不到 SpacecraftOrbitController（應掛在 Main Camera）。", this);
                enabled = false;
                return;
            }
            InputSystem.onDeviceChange += OnDeviceChange;
            EnterStandby("啟動");
        }

        private void OnDestroy() => InputSystem.onDeviceChange -= OnDeviceChange;

        private void OnDeviceChange(InputDevice device, InputDeviceChange change)
        {
            if (!(device is Joystick)) return;
            bool lost = change == InputDeviceChange.Removed || change == InputDeviceChange.Disconnected;
            if (lost && _state == State.Playing && device == _active)
                EnterStandby("操控中的搖桿中斷");
            if (change == InputDeviceChange.Added || change == InputDeviceChange.Reconnected)
                CaptureThrottleBaseline(device);
        }

        private void Update()
        {
            _stateTime += Time.unscaledDeltaTime;
            Keyboard kb = Keyboard.current;

            if (kb != null && kb.f9Key.wasPressedThisFrame && _state != State.Setup) { EnterSetup(); return; }

            switch (_state)
            {
                case State.Standby: UpdateStandby(kb); break;
                case State.Playing: UpdatePlaying(kb); break;
                case State.Setup:   UpdateSetup(kb);   break;
            }
        }

        // ── 待機 ─────────────────────────────────────────────────

        private void EnterStandby(string reason)
        {
            _state = State.Standby;
            _stateTime = 0f;
            _seat = Seat.None;
            _active = null;
            _hasPending = false;
            _pending = null;
            _pendingSeat = Seat.None;
            _exitHold = 0f;

            orbit.SetLocked(true);
            orbit.SetActiveJoystick(null);
            SetGameplayVisible(false);

            _throttleBaseline.Clear();
            _armed.Clear();
            foreach (InputDevice d in JoystickSeats.ConnectedJoysticks()) CaptureThrottleBaseline(d);

            if (ui != null) ui.SetMode(ControlSessionUI.Mode.Standby);
            RefreshCards();
            Debug.Log($"[ControlSessionManager] 進入待機畫面（{reason}）。");
        }

        private void UpdateStandby(Keyboard kb)
        {
            RefreshCards();

            // 已選中：等卡片閃一下再開始
            if (_hasPending)
            {
                if (_stateTime >= selectFlashSeconds) StartSession(_pending, _pendingSeat);
                return;
            }

            foreach (InputDevice d in JoystickSeats.ConnectedJoysticks())
            {
                bool act = HasActivity(d);
                if (!act) { _armed.Add(d); continue; }                       // 放開過 → 之後的操作才算數
                if (!_armed.Contains(d) || _stateTime < standbyGraceSeconds) continue;
                Seat seat = JoystickSeats.SeatOf(d);
                _hasPending = true;
                _pending = d;
                _pendingSeat = seat;
                _stateTime = 0f;
                if (ui != null && seat != Seat.None) ui.SetCard(seat, ControlSessionUI.CardState.Selected);
                return;
            }

            // 工作人員：Space 以鍵盤滑鼠開始
            if (kb != null && kb.spaceKey.wasPressedThisFrame)
            {
                _hasPending = true;
                _pending = null;
                _pendingSeat = Seat.None;
                _stateTime = 0f;
            }
        }

        private bool HasActivity(InputDevice d)
        {
            JoystickSeats.Snapshot s = JoystickSeats.Read(d);
            if (s.anyButton) return true;
            if (s.stick >= stickThreshold) return true;
            if (s.twist >= twistThreshold) return true;
            if (_throttleBaseline.TryGetValue(d, out float baseTh) && Mathf.Abs(s.throttle - baseTh) >= throttleThreshold) return true;
            return false;
        }

        private void CaptureThrottleBaseline(InputDevice d)
        {
            if (d == null) return;
            _throttleBaseline[d] = JoystickSeats.Read(d).throttle;
        }

        private void RefreshCards()
        {
            if (ui == null) return;

            bool configured = JoystickSeats.IsConfigured;
            int count = JoystickSeats.ConnectedJoysticks().Count;

            foreach (Seat seat in new[] { Seat.Child, Seat.Adult })
            {
                if (_hasPending && _pendingSeat == seat) continue;   // 保持「已選中」
                bool connected = !configured ? count > 0 : JoystickSeats.DeviceFor(seat) != null;
                ui.SetCard(seat, connected ? ControlSessionUI.CardState.Ready : ControlSessionUI.CardState.Disconnected);
            }

            string notice = null;
            if (count == 0) notice = "未偵測到搖桿";
            else if (!configured) notice = "尚未設定大人／小孩搖桿（工作人員請按 F9）";
            else if (count >= 2 && (JoystickSeats.DeviceFor(Seat.Child) == null || JoystickSeats.DeviceFor(Seat.Adult) == null))
                notice = "搖桿對應可能已改變（工作人員請按 F9 重新設定）";
            ui.SetStaffNotice(notice);
        }

        // ── 操控中 ───────────────────────────────────────────────

        private void StartSession(InputDevice device, Seat seat)
        {
            _state = State.Playing;
            _stateTime = 0f;
            _seat = seat;
            _active = device;
            _hasPending = false;
            _pending = null;
            _exitHold = 0f;

            if (seat == Seat.Child) orbit.ApplyProfile(childSpeed, childExponent);
            else orbit.ApplyProfile(adultSpeed, adultExponent);

            orbit.SetActiveJoystick(device);   // 另一支搖桿從此被排除
            orbit.SetLocked(false);
            SetGameplayVisible(true);

            if (ui != null)
            {
                ui.SetMode(ControlSessionUI.Mode.Playing);
                ui.SetPlayingSeat(seat);
                ui.SetExitHint(string.IsNullOrEmpty(JoystickSeats.ExitButtonPath) ? exitHintNoButton : exitHintText);
            }
            Debug.Log($"[ControlSessionManager] 取得操控權：{(device != null ? $"{device.displayName} #{device.deviceId}" : "鍵盤滑鼠")}，位置 {seat}。");
        }

        private void UpdatePlaying(Keyboard kb)
        {
            if (kb != null && kb.escapeKey.wasPressedThisFrame) { EnterStandby("工作人員按 Esc"); return; }

            if (_active != null && !_active.added) { EnterStandby("操控中的搖桿中斷"); return; }

            // ── 退出鍵長按 ──
            ButtonControl exit = JoystickSeats.FindButton(_active, JoystickSeats.ExitButtonPath);
            if (exit != null && exit.isPressed)
            {
                _exitHold += Time.unscaledDeltaTime;
                if (_exitHold >= exitHoldSeconds) { EnterStandby("長按退出鍵"); return; }
            }
            else
            {
                _exitHold = 0f;
            }
            if (ui != null) ui.SetExitProgress(_exitHold / exitHoldSeconds);

            // ── 閒置 ──
            float idle = orbit.IdleTime;
            if (_exitHold > 0f) idle = 0f;   // 正在長按退出鍵時不算閒置
            float remaining = idleTimeout - idle;
            if (ui != null) ui.SetCountdown(remaining <= countdownSeconds ? Mathf.CeilToInt(Mathf.Max(0f, remaining)) : -1);
            if (remaining <= 0f) EnterStandby($"無人操作 {idleTimeout:F0} 秒");
        }

        private void SetGameplayVisible(bool visible)
        {
            if (infoPanel != null) infoPanel.alpha = visible ? 1f : 0f;
            if (reticle != null) reticle.SetActive(visible);
        }

        // ── 工作人員設定（F9）────────────────────────────────────

        private void EnterSetup()
        {
            _state = State.Setup;
            _stateTime = 0f;
            _setupStep = 0;
            _setupChild = _setupAdult = null;
            _setupWaitRelease = true;
            _setupDoneTimer = 0f;

            orbit.SetLocked(true);
            orbit.SetActiveJoystick(null);
            SetGameplayVisible(false);
            if (ui != null) ui.SetMode(ControlSessionUI.Mode.Setup);
            ShowSetupStep();
        }

        private void ShowSetupStep()
        {
            if (ui == null) return;
            int count = JoystickSeats.ConnectedJoysticks().Count;
            string head = $"<size=60%>工作人員設定　偵測到 {count} 支搖桿　（Esc 取消）</size>\n";
            switch (_setupStep)
            {
                case 0: ui.SetSetupMessage(head + "步驟 1／3\n請推動【小孩專用】搖桿（較低的那支）"); break;
                case 1: ui.SetSetupMessage(head + "步驟 2／3\n請推動【大人專用】搖桿（較高的那支）" +
                                           (count < 2 ? "\n<size=60%>只有一支搖桿時，按 Enter 略過</size>" : "")); break;
                case 2: ui.SetSetupMessage(head + "步驟 3／3\n請按下要當作「退出」的按鈕\n<size=60%>扳機不可使用（已用於長按回到初始視角）；按 Enter 略過</size>"); break;
            }
        }

        private void UpdateSetup(Keyboard kb)
        {
            if (_setupDoneTimer > 0f)
            {
                _setupDoneTimer -= Time.unscaledDeltaTime;
                if (_setupDoneTimer <= 0f) EnterStandby("設定完成");
                return;
            }

            if (kb != null && kb.escapeKey.wasPressedThisFrame) { EnterStandby("取消設定"); return; }

            List<InputDevice> joys = JoystickSeats.ConnectedJoysticks();

            // 每一步都要等所有搖桿回到放開狀態，避免同一個動作被記成兩步
            if (_setupWaitRelease)
            {
                bool anyActive = false;
                foreach (InputDevice d in joys)
                {
                    JoystickSeats.Snapshot s = JoystickSeats.Read(d);
                    if (s.anyButton || s.stick > 0.15f || s.twist > 0.15f) { anyActive = true; break; }
                }
                if (!anyActive && _stateTime > 0.3f)
                {
                    _setupWaitRelease = false;
                    foreach (InputDevice d in joys) CaptureThrottleBaseline(d);
                }
                return;
            }

            switch (_setupStep)
            {
                case 0:
                    foreach (InputDevice d in joys)
                        if (HasActivity(d)) { _setupChild = d; NextSetupStep(); return; }
                    break;

                case 1:
                    if (kb != null && kb.enterKey.wasPressedThisFrame && joys.Count < 2) { NextSetupStep(); return; }
                    foreach (InputDevice d in joys)
                        if (d != _setupChild && HasActivity(d)) { _setupAdult = d; NextSetupStep(); return; }
                    break;

                case 2:
                    if (kb != null && kb.enterKey.wasPressedThisFrame) { FinishSetup(null); return; }
                    foreach (InputDevice d in joys)
                    {
                        string pressed = JoystickSeats.FirstPressedButton(d);
                        if (pressed == null) continue;
                        if (pressed == "trigger")
                        {
                            ui?.SetSetupMessage("步驟 3／3\n扳機已用於「長按回到初始視角」，請改按其他按鈕");
                            _setupWaitRelease = true; _stateTime = 0f;
                            return;
                        }
                        FinishSetup(pressed);
                        return;
                    }
                    break;
            }
        }

        private void NextSetupStep()
        {
            _setupStep++;
            _setupWaitRelease = true;
            _stateTime = 0f;
            ShowSetupStep();
        }

        private void FinishSetup(string exitPath)
        {
            string mode = JoystickSeats.Save(_setupChild, _setupAdult);
            JoystickSeats.ExitButtonPath = exitPath ?? "";

            string msg = "設定完成\n<size=60%>" +
                         $"小孩：{Describe(_setupChild)}\n大人：{Describe(_setupAdult)}\n" +
                         $"退出鍵：{(string.IsNullOrEmpty(exitPath) ? "未設定" : exitPath)}\n{mode}</size>";
            ui?.SetSetupMessage(msg);
            Debug.Log("[ControlSessionManager] " + msg.Replace("<size=60%>", "").Replace("</size>", "").Replace('\n', '｜'));
            _setupDoneTimer = 3f;
        }

        private static string Describe(InputDevice d) =>
            d == null ? "未設定" : $"{d.displayName} #{d.deviceId}（serial \"{d.description.serial}\"）";
    }
}
