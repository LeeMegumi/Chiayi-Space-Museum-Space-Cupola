// JoystickSeats.cs
// 大人專用／小孩專用兩支搖桿的識別、記憶與活動偵測。
//
// 套用位置：Assets/Scripts/Controls/JoystickSeats.cs
//
// ── 識別問題 ────────────────────────────────────────────────────
// 兩支同型號的 TCA Sidestick 在 Unity 中都是「T.A320 Copilot」，VID/PID 相同、序號皆為 "1"，
// deviceId 依啟動時的列舉順序分配，而 Windows 列舉同型號裝置的先後不保證固定。
// 實際展場測試：每次開啟都要重新綁定、推小孩搖桿卻亮大人卡片（看起來像「被搶走」）。
//
// 識別方式（設定時自動選擇最可靠者，記錄在 PlayerPrefs）：
//   1. USB 插孔（HidPortTracker，Windows）—— 插在同一個孔，重開機後仍正確。【優先】
//   2. 序號 —— 兩支序號不同時可用。
//   3. 連接順序 —— 最後手段，重開機可能對調。

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace SpaceCupola.Controls
{
    public enum Seat
    {
        None  = 0,   // 尚未設定
        Child = 1,   // 小孩專用（較低）
        Adult = 2,   // 大人專用（較高）
    }

    public static class JoystickSeats
    {
        private const string PrefChild = "SpaceCupola.Seat.Child";
        private const string PrefAdult = "SpaceCupola.Seat.Adult";
        private const string PrefExit  = "SpaceCupola.ExitButton";

        private const string PortPrefix   = "port:";
        private const string SerialPrefix = "serial:";
        private const string OrderPrefix  = "order:";

        // ── 查詢 ─────────────────────────────────────────────────

        /// <summary>目前連接的搖桿，依 deviceId 排序（順序模式的依據）。</summary>
        public static List<InputDevice> ConnectedJoysticks()
        {
            var list = new List<InputDevice>();
            foreach (Joystick j in Joystick.all) if (j != null && j.added) list.Add(j);
            list.Sort((a, b) => a.deviceId.CompareTo(b.deviceId));
            return list;
        }

        public static bool IsConfigured =>
            PlayerPrefs.HasKey(PrefChild) || PlayerPrefs.HasKey(PrefAdult);

        /// <summary>此搖桿屬於哪個位置。未設定或對不上時回傳 None。</summary>
        public static Seat SeatOf(InputDevice device)
        {
            if (device == null) return Seat.None;
            if (Matches(device, PlayerPrefs.GetString(PrefChild, ""))) return Seat.Child;
            if (Matches(device, PlayerPrefs.GetString(PrefAdult, ""))) return Seat.Adult;
            return Seat.None;
        }

        /// <summary>此位置目前對應的搖桿（未連接或尚未關聯則為 null）。</summary>
        public static InputDevice DeviceFor(Seat seat)
        {
            foreach (InputDevice d in ConnectedJoysticks())
                if (SeatOf(d) == seat) return d;
            return null;
        }

        /// <summary>
        /// 此位置的搖桿是否已連接。插孔識別模式下只需該插孔有裝置，不必等到有人推動、完成關聯。
        /// </summary>
        public static bool IsSeatConnected(Seat seat)
        {
            string key = PlayerPrefs.GetString(seat == Seat.Child ? PrefChild : PrefAdult, "");
            if (key.StartsWith(PortPrefix))
            {
                string loc = key.Substring(PortPrefix.Length);
                foreach (string l in HidPortTracker.CurrentLocations) if (l == loc) return true;
                return false;
            }
            return DeviceFor(seat) != null;
        }

        /// <summary>目前儲存的識別模式（顯示用）。</summary>
        public static string SavedMode
        {
            get
            {
                string k = PlayerPrefs.GetString(PrefChild, PlayerPrefs.GetString(PrefAdult, ""));
                if (k.StartsWith(PortPrefix))   return "USB 插孔";
                if (k.StartsWith(SerialPrefix)) return "序號";
                if (k.StartsWith(OrderPrefix))  return "連接順序";
                return "未設定";
            }
        }

        private static bool Matches(InputDevice device, string key)
        {
            if (string.IsNullOrEmpty(key)) return false;

            if (key.StartsWith(PortPrefix))
                return HidPortTracker.LocationOf(device) == key.Substring(PortPrefix.Length);

            if (key.StartsWith(SerialPrefix))
                return device.description.serial == key.Substring(SerialPrefix.Length);

            if (key.StartsWith(OrderPrefix) && int.TryParse(key.Substring(OrderPrefix.Length), out int idx))
            {
                List<InputDevice> list = ConnectedJoysticks();
                return idx >= 0 && idx < list.Count && list[idx] == device;
            }
            return false;
        }

        // ── 設定 ─────────────────────────────────────────────────

        /// <summary>儲存兩支搖桿的位置對應。adult 可為 null（只接一支時）。回傳使用的識別模式說明。</summary>
        public static string Save(InputDevice child, InputDevice adult)
        {
            // 1. USB 插孔（最可靠）
            string cl = HidPortTracker.LocationOf(child);
            string al = HidPortTracker.LocationOf(adult);
            if (cl != null && (adult == null || (al != null && al != cl)))
            {
                PlayerPrefs.DeleteKey(PrefChild);
                PlayerPrefs.DeleteKey(PrefAdult);
                if (child != null) PlayerPrefs.SetString(PrefChild, PortPrefix + cl);
                if (adult != null) PlayerPrefs.SetString(PrefAdult, PortPrefix + al);
                PlayerPrefs.Save();
                return "以 USB 插孔識別（重開機後仍正確；請勿調換兩支搖桿的插孔）";
            }

            // 2. 序號／3. 連接順序
            string cs = child != null ? child.description.serial : null;
            string asr = adult != null ? adult.description.serial : null;

            bool serialUsable = !string.IsNullOrEmpty(cs) &&
                                (adult == null || (!string.IsNullOrEmpty(asr) && asr != cs));

            List<InputDevice> list = ConnectedJoysticks();
            string Key(InputDevice d) => serialUsable
                ? SerialPrefix + d.description.serial
                : OrderPrefix + list.IndexOf(d);

            PlayerPrefs.DeleteKey(PrefChild);
            PlayerPrefs.DeleteKey(PrefAdult);
            if (child != null) PlayerPrefs.SetString(PrefChild, Key(child));
            if (adult != null) PlayerPrefs.SetString(PrefAdult, Key(adult));
            PlayerPrefs.Save();

            return serialUsable
                ? "以序號識別（重開機後仍正確）"
                : "兩支序號相同，以連接順序識別（若重開機後大人／小孩對調，請重新設定）";
        }

        /// <summary>退出鍵：相對於裝置的控制路徑，例如 "button5"、"hat/up"。</summary>
        public static string ExitButtonPath
        {
            get => PlayerPrefs.GetString(PrefExit, "");
            set { PlayerPrefs.SetString(PrefExit, value ?? ""); PlayerPrefs.Save(); }
        }

        public static ButtonControl FindButton(InputDevice device, string relativePath)
        {
            if (device == null || string.IsNullOrEmpty(relativePath)) return null;
            return InputControlPath.TryFindChild(device, relativePath) as ButtonControl;
        }

        /// <summary>目前被按下的第一顆按鈕（相對路徑）。設定退出鍵時使用。</summary>
        public static string FirstPressedButton(InputDevice device, string exclude = null)
        {
            if (device == null) return null;
            foreach (InputControl c in device.allControls)
            {
                if (!(c is ButtonControl b) || c.synthetic || !b.isPressed) continue;
                string rel = RelativePath(device, c);
                if (rel == exclude) continue;
                return rel;
            }
            return null;
        }

        public static string RelativePath(InputDevice device, InputControl control)
        {
            string prefix = device.path + "/";
            return control.path.StartsWith(prefix) ? control.path.Substring(prefix.Length) : control.name;
        }

        // ── 活動偵測 ─────────────────────────────────────────────

        /// <summary>讀取一支搖桿目前的狀態，用來判斷「有人在操作」。</summary>
        public struct Snapshot
        {
            public float stick;      // 搖桿偏移幅度 0..1
            public float twist;      // 扭轉幅度 0..1
            public float throttle;   // 推力桿 −1..1
            public bool  anyButton;
        }

        public static Snapshot Read(InputDevice device)
        {
            var s = new Snapshot();
            if (!(device is Joystick j)) return s;

            if (j.stick != null) s.stick = j.stick.ReadValue().magnitude;
            if (InputControlPath.TryFindChild(j, "rz") is AxisControl rz) s.twist = Mathf.Abs(rz.ReadValue());
            if (InputControlPath.TryFindChild(j, "slider") is AxisControl sl) s.throttle = sl.ReadValue();

            foreach (InputControl c in j.allControls)
            {
                if (c is ButtonControl b && !c.synthetic && b.isPressed && !(c.parent is StickControl))
                {
                    s.anyButton = true;
                    break;
                }
            }
            return s;
        }
    }
}
