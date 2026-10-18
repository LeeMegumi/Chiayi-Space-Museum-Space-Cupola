// ControlSessionManager.cs
// 雙搖桿操控權管理：待機畫面 → 某支搖桿取得操控權 → 返回待機。
//
// 套用位置：Assets/Scripts/Controls/ControlSessionManager.cs
// 由 Editor 工具建立：Tools > Space Cupola > 4. 建立待機畫面與雙搖桿管理
//
// ── 規則（使用者確認）───────────────────────────────────────────
//   待機畫面：相機自動回到初始視角，兩支搖桿都可操作；任一支被推動即取得操控權。
//             畫面左側「小孩專用」、右側「大人專用」，對應現場擺放位置。
//             兩邊都在推時，「先開始推的」取得操控權（不依清單順序）。
//   操控中  ：只有取得操控權的那支有效；另一支在 Input System 層即被排除（InputActionAsset.devices）。
//             小孩搖桿：環繞速度 70%、響應曲線較平緩。
//             短按扳機：鎖定跟隨離準心最近的地標（再按一次或推搖桿解除）
//             長按扳機 2 秒：返回待機（畫面顯示進度圈，放開即取消）
//   返回待機：(1) 無人操作 60 秒（最後 10 秒倒數提示，動一下即取消）
//             (2) 長按扳機 2 秒
//             (3) 操控中的搖桿被拔除
//
// ── 工作人員按鍵 ────────────────────────────────────────────────
//   F9：設定大人／小孩搖桿（推動小孩搖桿 → 推動大人搖桿）。以 USB 插孔記憶，重開機不必重設。
//   F10：顯示／隱藏除錯畫面（啟動時隱藏）
//   Esc（操控中）：立即返回待機　　Space（待機中）：以鍵盤滑鼠開始　　L（操控中）：鎖定／解除地標

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
        [Tooltip("長按扳機多久返回待機（秒）。")]
        [SerializeField] private float exitHoldSeconds = 2f;

        [Header("扳機")]
        [Tooltip("扳機按住少於此時間放開，視為「短按」→ 鎖定／解除地標（秒）。")]
        [SerializeField] private float tapMaxSeconds = 0.5f;
        [Tooltip("扳機按住超過此時間才顯示退出進度圈，避免短按時進度圈閃一下（秒）。")]
        [SerializeField] private float exitRingDelay = 0.35f;

        [Header("取得操控權")]
        [Tooltip("返回待機後的冷卻時間（秒）。")]
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
        [SerializeField] private string triggerHintText = "短按扳機：鎖定地標　長按扳機 2 秒：返回待機畫面";

        private State _state;
        private float _stateTime;
        private Seat _seat;
        private InputDevice _active;
        private float _triggerHeld;
        // 扳機必須在本次操控中「放開過一次」才開始計算。
        // 參觀者常用「按住扳機」取得操控權 —— 若不先放開，開場 0.5 秒內放手會被當成短按（誤鎖定），
        // 一直按著則會在開場約 2 秒後直接退出。
        private bool _triggerArmed;
        private bool _hasPending;              // 已選中、等待卡片閃一下再開始
        private InputDevice _pending;          // 選中的搖桿（鍵盤開始時為 null）
        private Seat _pendingSeat;
        private readonly Dictionary<InputDevice, float> _throttleBaseline = new Dictionary<InputDevice, float>();

        // 進入待機後，搖桿必須先「完全放開」一次才可取得操控權。
        // 否則長按扳機 2 秒返回待機時，手還沒放開，冷卻一過就會立刻又取得操控權 —— 等於退不出去。
        private readonly HashSet<InputDevice> _armed = new HashSet<InputDevice>();

        // 待機中每支搖桿「開始被推動」的時間。兩邊同時在推時，先推的取得操控權。
        // （舊版依清單順序決定，冷卻結束時若兩邊都在推，後推的可能搶走 —— 展場回報「有時候會被搶走」。）
        private readonly Dictionary<InputDevice, float> _activeSince = new Dictionary<InputDevice, float>();

        // 設定模式
        private int _setupStep;
        private InputDevice _setupChild, _setupAdult;
        private InputDevice _setupCandidate;   // 已推動、等待 USB 插孔關聯完成
        private float _setupCandidateTime;
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
            HidPortTracker.EnsureStarted();
            InputSystem.onDeviceChange += OnDeviceChange;
            EnterStandby("啟動");
            Debug.Log($"[ControlSessionManager] 搖桿識別模式：{JoystickSeats.SavedMode}\n{HidPortTracker.Describe()}");
        }

        private void OnDestroy()
        {
            InputSystem.onDeviceChange -= OnDeviceChange;
            HidPortTracker.Stop();
        }

        private void OnDeviceChange(InputDevice device, InputDeviceChange change)
        {
            if (!(device is Joystick)) return;
            bool lost = change == InputDeviceChange.Removed || change == InputDeviceChange.Disconnected;
            bool added = change == InputDeviceChange.Added || change == InputDeviceChange.Reconnected;

            if (lost || added) HidPortTracker.Refresh();   // 插拔後重新掃描 USB 插孔
            if (lost && _state == State.Playing && device == _active)
                EnterStandby("操控中的搖桿中斷");
            if (added) CaptureThrottleBaseline(device);
        }

        private void Update()
        {
            _stateTime += Time.unscaledDeltaTime;
            Keyboard kb = Keyboard.current;

            DebugOverlay.HandleHotkey();
            // 輸出版隱藏滑鼠游標（展場錄影中游標一直停在畫面上）；除錯畫面（F10）或設定模式時才顯示
            if (!Application.isEditor) Cursor.visible = DebugOverlay.Visible || _state == State.Setup;
            HidPortTracker.Learn(JoystickSeats.ConnectedJoysticks());

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
            _triggerHeld = 0f;

            orbit.ClearLandmarkLock();
            orbit.SetLocked(true);
            orbit.SetActiveJoystick(null);
            SetGameplayVisible(false);

            _throttleBaseline.Clear();
            _armed.Clear();
            _activeSince.Clear();
            foreach (InputDevice d in JoystickSeats.ConnectedJoysticks()) CaptureThrottleBaseline(d);

            if (ui != null) ui.SetMode(ControlSessionUI.Mode.Standby);
            RefreshCards();
            Debug.Log($"[ControlSessionManager] 進入待機畫面（{reason}）。");
        }

        private void UpdateStandby(Keyboard kb)
        {
            RefreshCards();

            // 已選中：等卡片閃一下再開始；期間另一支搖桿無法搶走
            if (_hasPending)
            {
                if (_stateTime >= selectFlashSeconds) StartSession(_pending, _pendingSeat);
                return;
            }

            // 更新每支搖桿的狀態：放開過才「上膛」；記錄開始推動的時間
            InputDevice first = null;
            float firstSince = float.MaxValue;
            foreach (InputDevice d in JoystickSeats.ConnectedJoysticks())
            {
                if (!HasActivity(d))
                {
                    _armed.Add(d);
                    _activeSince.Remove(d);
                    continue;
                }
                if (!_armed.Contains(d)) continue;
                if (!_activeSince.ContainsKey(d)) _activeSince[d] = _stateTime;

                if (_activeSince[d] < firstSince) { firstSince = _activeSince[d]; first = d; }
            }

            if (first != null && _stateTime >= standbyGraceSeconds)
            {
                Seat seat = JoystickSeats.SeatOf(first);
                _hasPending = true;
                _pending = first;
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
                bool connected = configured ? JoystickSeats.IsSeatConnected(seat) : count > 0;
                ui.SetCard(seat, connected ? ControlSessionUI.CardState.Ready : ControlSessionUI.CardState.Disconnected);
            }

            string notice = null;
            if (count == 0) notice = "未偵測到搖桿";
            else if (!configured) notice = "尚未設定大人／小孩搖桿（工作人員請按 F9）";
            else if (count >= 2 && (!JoystickSeats.IsSeatConnected(Seat.Child) || !JoystickSeats.IsSeatConnected(Seat.Adult)))
                notice = "搖桿插孔可能已更換（工作人員請按 F9 重新設定）";
            else if (JoystickSeats.SavedMode == "連接順序")
                notice = "目前以連接順序識別搖桿，重開機可能對調（工作人員請按 F9 重新設定）";
            ui.SetStaffNotice(notice);
        }

        // ── 操控中 ───────────────────────────────────────────────

        private void StartSession(InputDevice device, Seat seat)
        {
            // 選中當下若尚未完成 USB 插孔關聯，卡片閃爍的 0.6 秒內通常已關聯 —— 再判斷一次
            if (seat == Seat.None && device != null) seat = JoystickSeats.SeatOf(device);

            _state = State.Playing;
            _stateTime = 0f;
            _seat = seat;
            _active = device;
            _hasPending = false;
            _pending = null;
            _triggerHeld = 0f;
            ButtonControl startTrigger = (device as Joystick)?.trigger;
            _triggerArmed = startTrigger == null || !startTrigger.isPressed;

            if (seat == Seat.Child) orbit.ApplyProfile(childSpeed, childExponent);
            else orbit.ApplyProfile(adultSpeed, adultExponent);

            orbit.SetActiveJoystick(device);   // 另一支搖桿從此被排除
            orbit.SetLocked(false);
            SetGameplayVisible(true);

            if (ui != null)
            {
                ui.SetMode(ControlSessionUI.Mode.Playing);
                ui.SetPlayingSeat(seat);
                ui.SetExitHint(triggerHintText);
            }
            Debug.Log($"[ControlSessionManager] 取得操控權：{(device != null ? $"{device.displayName} #{device.deviceId}" : "鍵盤滑鼠")}，位置 {seat}。");
        }

        private void UpdatePlaying(Keyboard kb)
        {
            if (kb != null && kb.escapeKey.wasPressedThisFrame) { EnterStandby("工作人員按 Esc"); return; }
            if (kb != null && kb.lKey.wasPressedThisFrame) orbit.ToggleLandmarkLock();

            if (_active != null && !_active.added) { EnterStandby("操控中的搖桿中斷"); return; }

            // ── 扳機：短按鎖定地標、長按 2 秒返回待機 ──
            ButtonControl trigger = (_active as Joystick)?.trigger;
            if (!_triggerArmed)
            {
                if (trigger == null || !trigger.isPressed) _triggerArmed = true;   // 開場時按著的扳機已放開
                _triggerHeld = 0f;
            }
            else if (trigger != null && trigger.isPressed)
            {
                _triggerHeld += Time.unscaledDeltaTime;
                if (_triggerHeld >= exitHoldSeconds) { EnterStandby("長按扳機"); return; }
            }
            else
            {
                if (_triggerHeld > 0f && _triggerHeld <= tapMaxSeconds) orbit.ToggleLandmarkLock();
                _triggerHeld = 0f;
            }

            if (ui != null)
            {
                float ring = _triggerHeld > exitRingDelay
                    ? (_triggerHeld - exitRingDelay) / Mathf.Max(0.01f, exitHoldSeconds - exitRingDelay)
                    : 0f;
                ui.SetExitProgress(ring);
            }

            // ── 閒置 ──
            float idle = orbit.IdleTime;
            if (_triggerHeld > 0f) idle = 0f;   // 正在按扳機時不算閒置
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
            _setupCandidate = null;
            _setupWaitRelease = true;
            _setupDoneTimer = 0f;

            orbit.ClearLandmarkLock();
            orbit.SetLocked(true);
            orbit.SetActiveJoystick(null);
            SetGameplayVisible(false);
            if (ui != null) ui.SetMode(ControlSessionUI.Mode.Setup);
            ShowSetupStep();
        }

        private const float SetupLinkTimeout = 5f;

        private void ShowSetupStep(string status = null)
        {
            if (ui == null) return;
            string tail = status != null ? $"\n<size=50%>{status}</size>" : "";
            int count = JoystickSeats.ConnectedJoysticks().Count;
            string head = $"<size=60%>工作人員設定　偵測到 {count} 支搖桿　（Esc 取消）</size>\n";
            switch (_setupStep)
            {
                case 0: ui.SetSetupMessage(head + "步驟 1／2\n請推動【小孩專用】搖桿（較低的那支）" + tail); break;
                case 1: ui.SetSetupMessage(head + "步驟 2／2\n請推動【大人專用】搖桿（較高的那支）" +
                                           (count < 2 ? "\n<size=60%>只有一支搖桿時，按 Enter 略過</size>" : "") + tail); break;
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
            // （判斷門檻與待機畫面相同。舊版用 0.15，若某支搖桿靜止時扭轉／搖桿不完全回中，會永遠卡在這一步）
            if (_setupWaitRelease)
            {
                string busy = null;
                foreach (InputDevice d in joys)
                {
                    JoystickSeats.Snapshot s = JoystickSeats.Read(d);
                    if (s.anyButton || s.stick >= stickThreshold || s.twist >= twistThreshold)
                    {
                        busy = $"{Describe(d)}　按鈕 {(s.anyButton ? "按下" : "—")}　搖桿 {s.stick:F2}　扭轉 {s.twist:F2}";
                        break;
                    }
                }
                if (busy == null && _stateTime > 0.3f)
                {
                    _setupWaitRelease = false;
                    foreach (InputDevice d in joys) CaptureThrottleBaseline(d);
                    ShowSetupStep();
                }
                else if (busy != null && _stateTime > 1f)
                {
                    ShowSetupStep("請先放開所有搖桿…\n" + busy);
                }
                return;
            }

            // 已推動的搖桿：等 USB 插孔關聯完成（最多 1.5 秒），才進下一步 —— 確保能以插孔記憶
            if (_setupCandidate != null)
            {
                bool linked = !HidPortTracker.IsAvailable || HidPortTracker.LocationOf(_setupCandidate) != null;
                float waited = Time.unscaledTime - _setupCandidateTime;
                if (!linked)
                    ShowSetupStep($"正在辨識 USB 插孔，請持續來回推動這支搖桿（另一支不要碰）… {Mathf.Max(0f, SetupLinkTimeout - waited):F0}");
                if (linked || waited > SetupLinkTimeout)
                {
                    if (!linked) Debug.LogWarning($"[ControlSessionManager] 設定：{_setupCandidate.displayName} #{_setupCandidate.deviceId} 未能關聯 USB 插孔。\n{HidPortTracker.Describe()}");
                    if (_setupStep == 0) _setupChild = _setupCandidate; else _setupAdult = _setupCandidate;
                    _setupCandidate = null;
                    if (_setupStep == 0) NextSetupStep(); else FinishSetup();
                }
                return;
            }

            if (_setupStep == 1 && kb != null && kb.enterKey.wasPressedThisFrame && joys.Count < 2) { FinishSetup(); return; }

            foreach (InputDevice d in joys)
            {
                if (_setupStep == 1 && d == _setupChild) continue;
                if (!HasActivity(d)) continue;
                _setupCandidate = d;
                _setupCandidateTime = Time.unscaledTime;
                return;
            }
        }

        private void NextSetupStep()
        {
            _setupStep++;
            _setupWaitRelease = true;
            _stateTime = 0f;
            ShowSetupStep();
        }

        private void FinishSetup()
        {
            string mode = JoystickSeats.Save(_setupChild, _setupAdult);

            string msg = "設定完成\n<size=60%>" +
                         $"小孩：{Describe(_setupChild)}\n大人：{Describe(_setupAdult)}\n{mode}</size>";
            ui?.SetSetupMessage(msg);
            Debug.Log("[ControlSessionManager] " + msg.Replace("<size=60%>", "").Replace("</size>", "").Replace('\n', '｜'));
            _setupDoneTimer = 3f;
        }

        private static string Describe(InputDevice d)
        {
            if (d == null) return "未設定";
            string loc = HidPortTracker.LocationOf(d);
            return loc != null ? $"USB {ShortLocation(loc)}" : $"{d.displayName} #{d.deviceId}";
        }

        /// <summary>插孔位置字串很長，畫面上只顯示最後一段（例如 USB(3)）。</summary>
        private static string ShortLocation(string loc)
        {
            int i = loc.LastIndexOf('#');
            return i >= 0 && i < loc.Length - 1 ? loc.Substring(i + 1) : loc;
        }

        // ── 除錯畫面（F10）──────────────────────────────────────

        private void OnGUI()
        {
            if (!DebugOverlay.Visible) return;
            GUILayout.BeginArea(new Rect(Screen.width - 560, Screen.height - 230, 550, 220), GUI.skin.box);
            GUILayout.Label($"操控權：{_state}　位置：{_seat}　裝置：{(_active != null ? $"{_active.displayName} #{_active.deviceId}" : "—")}");
            GUILayout.Label($"識別模式：{JoystickSeats.SavedMode}　扳機按住 {_triggerHeld:F1}s　鎖定：{(orbit != null && orbit.LockedCity != null ? orbit.LockedCity.Name : "—")}");
            GUILayout.Label(HidPortTracker.Describe());
            GUILayout.EndArea();
        }
    }
}
