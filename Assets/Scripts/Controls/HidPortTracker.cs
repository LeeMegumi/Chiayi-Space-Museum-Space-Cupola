// HidPortTracker.cs
// 以「USB 插孔位置」識別兩支同型號搖桿（Windows 專用）。
//
// 套用位置：Assets/Scripts/Controls/HidPortTracker.cs
//
// ── 為什麼需要 ───────────────────────────────────────────────────
// 兩支 TCA Sidestick 的 VID/PID、序號（"1"）完全相同，Unity 的 InputDeviceDescription 也沒有 USB 路徑。
// 舊做法以 deviceId 順序記憶，但 Windows 每次啟動列舉同型號裝置的先後不保證固定 →
// 大人／小孩對應會失效或對調（每次都要重新綁定、推小孩搖桿卻亮大人卡片）。
//
// ── 做法 ─────────────────────────────────────────────────────────
// 1. 透過 SetupAPI 列出 VID/PID 與 Unity 搖桿相同的 HID 介面，取得每支的「插孔位置」
//    （父裝置的 LOCATION_PATHS，例如 PCIROOT(0)#PCI(1400)#USBROOT(0)#USB(3)）。
//    只要插在同一個 USB 孔，重開機後不變。
// 2. 以共享模式開啟這些 HID 介面，背景執行緒讀取輸入報告，記錄「最後一次內容變化」的時間。
//    （HID 允許多個讀取者同時開啟，Unity 自己的讀取不受影響。）
// 3. 關聯：某一帧只有一支 Unity 搖桿的數值在變、同時也只有一個 HID 介面的報告在變 → 兩者是同一支。
//    有人推搖桿時就會自然建立關聯；取得操控權那一刻（只有一個人在推）也會即時關聯。
//
// 任何一步失敗（非 Windows、權限、API 錯誤）都只會讓 IsAvailable = false，
// JoystickSeats 會退回舊的序號／順序識別，不影響其他功能。

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using Debug = UnityEngine.Debug;

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;
#endif

namespace SpaceCupola.Controls
{
    public static class HidPortTracker
    {
        /// <summary>兩邊「同時有變化」的判定時間窗（秒）。</summary>
        private const double MatchWindow = 0.15;

        private class Tracked
        {
            public string path;          // HID 介面路徑（本次開機有效）
            public string location;      // 插孔位置（跨開機穩定）
            public long lastChange;      // Stopwatch timestamp，背景執行緒寫入
            public int changes;          // 報告內容變化次數（第一筆為初始值，不算操作）
            public volatile bool stop;
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            public SafeFileHandle handle;
            public Thread thread;
#endif
        }

        private static readonly List<Tracked> s_Tracked = new List<Tracked>();
        private static readonly Dictionary<int, string> s_Assoc = new Dictionary<int, string>();   // Unity deviceId → location
        private static readonly Dictionary<int, long> s_UnityLastChange = new Dictionary<int, long>();
        private static readonly Dictionary<int, int> s_UnitySignature = new Dictionary<int, int>();
        private static bool s_Started;
        private static string s_LastError;

        /// <summary>插孔識別是否可用（Windows 且至少開啟一個 HID 介面）。</summary>
        public static bool IsAvailable => s_Tracked.Count > 0;

        /// <summary>目前偵測到的插孔位置。</summary>
        public static IEnumerable<string> CurrentLocations
        {
            get { foreach (Tracked t in s_Tracked) yield return t.location; }
        }

        // ─────────────────────────────────────────────────────────────

        /// <summary>啟動或重新掃描（搖桿插拔時由 ControlSessionManager 呼叫）。</summary>
        public static void Refresh()
        {
            Stop();
            s_Started = true;
            s_Assoc.Clear();
            s_UnityLastChange.Clear();
            s_UnitySignature.Clear();
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            try { Enumerate(); }
            catch (Exception e)
            {
                s_LastError = e.Message;
                Debug.LogWarning($"[HidPortTracker] 無法以 USB 插孔識別搖桿，改用舊識別方式：{e.Message}");
                Stop();
            }
#else
            s_LastError = "非 Windows 平台";
#endif
            Debug.Log($"[HidPortTracker] 偵測到 {s_Tracked.Count} 個搖桿 HID 介面：" +
                      string.Join("、", s_Tracked.ConvertAll(t => t.location)));
        }

        private static bool s_HooksRegistered;

        public static void EnsureStarted()
        {
            if (!s_HooksRegistered)
            {
                s_HooksRegistered = true;
                // 程式關閉、編輯器重新編譯腳本前，確實停掉背景執行緒（避免編輯器卡住）
                Application.quitting += Stop;
#if UNITY_EDITOR
                UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += Stop;
                UnityEditor.EditorApplication.playModeStateChanged += s =>
                {
                    if (s == UnityEditor.PlayModeStateChange.ExitingPlayMode) Stop();
                };
#endif
            }
            if (!s_Started) Refresh();
        }

        /// <summary>停止所有讀取執行緒。每個執行緒最多 100ms 內會察覺並結束。</summary>
        public static void Stop()
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            foreach (Tracked t in s_Tracked) t.stop = true;
            foreach (Tracked t in s_Tracked)
            {
                try { t.thread?.Join(500); } catch { }
                try { t.handle?.Dispose(); } catch { }
            }
#endif
            s_Tracked.Clear();
            s_Started = false;
        }

        /// <summary>此 Unity 搖桿目前對應的插孔位置（尚未關聯則為 null）。</summary>
        public static string LocationOf(InputDevice device)
        {
            if (device == null) return null;
            if (s_Tracked.Count == 1 && JoystickSeats.ConnectedJoysticks().Count == 1)
                return s_Tracked[0].location;   // 只有一支時不需要關聯
            return s_Assoc.TryGetValue(device.deviceId, out string loc) ? loc : null;
        }

        /// <summary>
        /// 每帧呼叫：比對 Unity 搖桿與 HID 介面「誰在變」，建立關聯。
        /// </summary>
        public static void Learn(List<InputDevice> joysticks)
        {
            if (!IsAvailable || joysticks == null) return;
            long now = Stopwatch.GetTimestamp();

            // Unity 端：數值有變的搖桿
            foreach (InputDevice d in joysticks)
            {
                int sig = Signature(d);
                if (s_UnitySignature.TryGetValue(d.deviceId, out int prev) && prev != sig)
                    s_UnityLastChange[d.deviceId] = now;
                s_UnitySignature[d.deviceId] = sig;
            }

            InputDevice uniqueUnity = null;
            int unityCount = 0;
            foreach (InputDevice d in joysticks)
            {
                if (s_UnityLastChange.TryGetValue(d.deviceId, out long t) && Seconds(now - t) <= MatchWindow)
                { unityCount++; uniqueUnity = d; }
            }

            // 以「插孔」計數：同一支搖桿若有多個 HID 介面（多個 collection）會在同一插孔，只算一次
            Tracked uniqueHid = null;
            int hidCount = 0;
            foreach (Tracked t in s_Tracked)
            {
                long lc = Interlocked.Read(ref t.lastChange);
                if (Volatile.Read(ref t.changes) > 1 && Seconds(now - lc) <= MatchWindow)
                {
                    if (uniqueHid == null) { hidCount = 1; uniqueHid = t; }
                    else if (uniqueHid.location != t.location) hidCount = 2;   // 兩個以上插孔同時在動 → 無法判斷
                }
            }

            if (unityCount == 1 && hidCount == 1)
            {
                // 一個插孔只能對應一支 Unity 搖桿
                foreach (int key in new List<int>(s_Assoc.Keys))
                    if (s_Assoc[key] == uniqueHid.location && key != uniqueUnity.deviceId) s_Assoc.Remove(key);

                if (!s_Assoc.TryGetValue(uniqueUnity.deviceId, out string old) || old != uniqueHid.location)
                {
                    s_Assoc[uniqueUnity.deviceId] = uniqueHid.location;
                    Debug.Log($"[HidPortTracker] 關聯：{uniqueUnity.displayName} #{uniqueUnity.deviceId} ↔ {uniqueHid.location}");
                }
            }
        }

        /// <summary>除錯畫面與記錄檔用的狀態說明。</summary>
        public static string Describe()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"USB 插孔識別：{(IsAvailable ? "可用" : "不可用" + (s_LastError != null ? $"（{s_LastError}）" : ""))}");
            long now = Stopwatch.GetTimestamp();
            foreach (Tracked t in s_Tracked)
            {
                long lc = Interlocked.Read(ref t.lastChange);
                string ago = t.changes > 1 ? $"{Seconds(now - lc):F1}s 前變化" : "尚無操作";
                sb.AppendLine($"  {t.location}  （{ago}）");
            }
            foreach (var kv in s_Assoc) sb.AppendLine($"  Unity #{kv.Key} ↔ {kv.Value}");
            return sb.ToString();
        }

        // ── Unity 端數值簽章（數值改變即視為有變化）──────────────────

        private static int Signature(InputDevice d)
        {
            unchecked
            {
                int h = 17;
                if (d is Joystick j && j.stick != null)
                {
                    Vector2 s = j.stick.ReadValue();
                    h = h * 31 + Mathf.RoundToInt(s.x * 200f);
                    h = h * 31 + Mathf.RoundToInt(s.y * 200f);
                }
                if (InputControlPath.TryFindChild(d, "rz") is AxisControl rz) h = h * 31 + Mathf.RoundToInt(rz.ReadValue() * 200f);
                if (InputControlPath.TryFindChild(d, "slider") is AxisControl sl) h = h * 31 + Mathf.RoundToInt(sl.ReadValue() * 200f);
                foreach (InputControl c in d.allControls)
                    if (c is ButtonControl b && !c.synthetic) h = h * 31 + (b.isPressed ? 1 : 0);
                return h;
            }
        }

        private static double Seconds(long ticks) => (double)ticks / Stopwatch.Frequency;

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        // ── Windows：列舉 HID 介面 ────────────────────────────────────

        private static void Enumerate()
        {
            // 只追蹤與 Unity 搖桿相同 VID/PID 的 HID 介面
            var wanted = new HashSet<string>();
            foreach (InputDevice d in JoystickSeats.ConnectedJoysticks())
            {
                VidPid v = JsonUtility.FromJson<VidPid>(d.description.capabilities ?? "{}");
                if (v.vendorId != 0) wanted.Add($"vid_{v.vendorId:x4}&pid_{v.productId:x4}");
            }
            if (wanted.Count == 0) return;

            HidD_GetHidGuid(out Guid hidGuid);
            IntPtr set = SetupDiGetClassDevsW(ref hidGuid, IntPtr.Zero, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
            if (set == INVALID_HANDLE_VALUE) throw new Exception("SetupDiGetClassDevs 失敗");

            try
            {
                var ifData = new SP_DEVICE_INTERFACE_DATA { cbSize = (uint)Marshal.SizeOf<SP_DEVICE_INTERFACE_DATA>() };
                for (uint i = 0; SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref hidGuid, i, ref ifData); i++)
                {
                    var devInfo = new SP_DEVINFO_DATA { cbSize = (uint)Marshal.SizeOf<SP_DEVINFO_DATA>() };
                    string path = GetInterfacePath(set, ref ifData, ref devInfo);
                    if (path == null) continue;

                    string lower = path.ToLowerInvariant();
                    bool match = false;
                    foreach (string w in wanted) if (lower.Contains(w)) { match = true; break; }
                    if (!match) continue;

                    string location = GetLocation(devInfo.DevInst) ?? path;
                    StartReader(path, location);
                }
            }
            finally
            {
                SetupDiDestroyDeviceInfoList(set);
            }
        }

        [Serializable] private struct VidPid { public int vendorId; public int productId; }

        private static string GetInterfacePath(IntPtr set, ref SP_DEVICE_INTERFACE_DATA ifData, ref SP_DEVINFO_DATA devInfo)
        {
            SetupDiGetDeviceInterfaceDetailW(set, ref ifData, IntPtr.Zero, 0, out uint required, ref devInfo);
            if (required == 0) return null;

            IntPtr buf = Marshal.AllocHGlobal((int)required);
            try
            {
                // SP_DEVICE_INTERFACE_DETAIL_DATA_W.cbSize：x64 為 8、x86 為 6
                Marshal.WriteInt32(buf, IntPtr.Size == 8 ? 8 : 6);
                if (!SetupDiGetDeviceInterfaceDetailW(set, ref ifData, buf, required, out _, ref devInfo)) return null;
                return Marshal.PtrToStringUni(IntPtr.Add(buf, 4));
            }
            finally
            {
                Marshal.FreeHGlobal(buf);
            }
        }

        /// <summary>往上找最近一個有 LOCATION_PATHS（或 LOCATION_INFORMATION）的祖先節點。</summary>
        private static string GetLocation(uint devInst)
        {
            uint node = devInst;
            for (int depth = 0; depth < 6; depth++)
            {
                string paths = GetDevNodeString(node, CM_DRP_LOCATION_PATHS);
                if (!string.IsNullOrEmpty(paths)) return paths;
                string info = GetDevNodeString(node, CM_DRP_LOCATION_INFORMATION);
                if (!string.IsNullOrEmpty(info) && info.Contains("Port_#")) return info;
                if (CM_Get_Parent(out uint parent, node, 0) != 0) break;
                node = parent;
            }
            return null;
        }

        private static string GetDevNodeString(uint devInst, uint property)
        {
            uint length = 0;
            CM_Get_DevNode_Registry_PropertyW(devInst, property, out _, IntPtr.Zero, ref length, 0);
            if (length == 0) return null;
            IntPtr buf = Marshal.AllocHGlobal((int)length);
            try
            {
                if (CM_Get_DevNode_Registry_PropertyW(devInst, property, out _, buf, ref length, 0) != 0) return null;
                return Marshal.PtrToStringUni(buf);   // MULTI_SZ 取第一段即可
            }
            finally
            {
                Marshal.FreeHGlobal(buf);
            }
        }

        private static void StartReader(string path, string location)
        {
            SafeFileHandle h = CreateFileW(path, GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE,
                                           IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_OVERLAPPED, IntPtr.Zero);
            if (h == null || h.IsInvalid)
            {
                Debug.LogWarning($"[HidPortTracker] 無法開啟 HID 介面（錯誤 {Marshal.GetLastWin32Error()}）：{location}");
                return;
            }

            int reportLen = 64;
            if (HidD_GetPreparsedData(h, out IntPtr pre))
            {
                if (HidP_GetCaps(pre, out HIDP_CAPS caps) == HIDP_STATUS_SUCCESS && caps.InputReportByteLength > 0)
                    reportLen = caps.InputReportByteLength;
                HidD_FreePreparsedData(pre);
            }

            var t = new Tracked { path = path, location = location, handle = h };
            t.thread = new Thread(() => ReadLoop(t, reportLen)) { IsBackground = true, Name = "HidPortTracker" };
            s_Tracked.Add(t);
            t.thread.Start();
        }

        /// <summary>
        /// 非阻塞（overlapped）讀取：每 100ms 醒來檢查是否該停止。
        /// 若改用阻塞式 ReadFile，搖桿靜止時不會送報告，執行緒會卡在原生呼叫中，
        /// 編輯器停止 Play 或重新編譯時可能因等待執行緒而卡住。
        /// </summary>
        private static void ReadLoop(Tracked t, int reportLen)
        {
            IntPtr evt = CreateEventW(IntPtr.Zero, true, false, null);
            IntPtr ov  = Marshal.AllocHGlobal(Marshal.SizeOf<NativeOverlapped>());
            IntPtr buf = Marshal.AllocHGlobal(reportLen);
            var cur  = new byte[reportLen];
            var prev = new byte[reportLen];
            bool pending = false;

            try
            {
                while (!t.stop)
                {
                    ResetEvent(evt);
                    Marshal.StructureToPtr(new NativeOverlapped { EventHandle = evt }, ov, false);

                    if (!ReadFile(t.handle, buf, (uint)reportLen, IntPtr.Zero, ov))
                    {
                        if (Marshal.GetLastWin32Error() != ERROR_IO_PENDING) break;   // 裝置拔除等錯誤
                        pending = true;

                        uint w;
                        while ((w = WaitForSingleObject(evt, 100)) == WAIT_TIMEOUT)
                            if (t.stop) break;
                        if (t.stop || w != WAIT_OBJECT_0) break;
                    }

                    if (!GetOverlappedResult(t.handle, ov, out uint read, false)) { pending = false; break; }
                    pending = false;
                    if (read == 0) continue;

                    Marshal.Copy(buf, cur, 0, (int)read);
                    bool changed = false;
                    for (int i = 0; i < read; i++)
                        if (cur[i] != prev[i]) { changed = true; break; }
                    if (!changed) continue;

                    Buffer.BlockCopy(cur, 0, prev, 0, (int)read);
                    Interlocked.Exchange(ref t.lastChange, Stopwatch.GetTimestamp());
                    Interlocked.Increment(ref t.changes);
                }
            }
            catch { /* 裝置拔除或關閉時結束 */ }
            finally
            {
                // 尚有未完成的讀取時，必須先取消並等它結束，才能釋放緩衝區（否則驅動程式可能寫入已釋放的記憶體）
                if (pending)
                {
                    try { CancelIoEx(t.handle, ov); GetOverlappedResult(t.handle, ov, out _, true); } catch { }
                }
                Marshal.FreeHGlobal(buf);
                Marshal.FreeHGlobal(ov);
                if (evt != IntPtr.Zero) CloseHandle(evt);
            }
        }

        // ── P/Invoke ─────────────────────────────────────────────────

        private const uint DIGCF_PRESENT = 0x02, DIGCF_DEVICEINTERFACE = 0x10;
        private const uint GENERIC_READ = 0x80000000, FILE_SHARE_READ = 1, FILE_SHARE_WRITE = 2, OPEN_EXISTING = 3;
        private const uint FILE_FLAG_OVERLAPPED = 0x40000000;
        private const int ERROR_IO_PENDING = 997;
        private const uint WAIT_OBJECT_0 = 0, WAIT_TIMEOUT = 258;
        private const uint CM_DRP_LOCATION_INFORMATION = 0x0000000E, CM_DRP_LOCATION_PATHS = 0x00000024;
        private const int HIDP_STATUS_SUCCESS = 0x00110000;
        private static readonly IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);

        [StructLayout(LayoutKind.Sequential)]
        private struct SP_DEVICE_INTERFACE_DATA { public uint cbSize; public Guid InterfaceClassGuid; public uint Flags; public IntPtr Reserved; }

        [StructLayout(LayoutKind.Sequential)]
        private struct SP_DEVINFO_DATA { public uint cbSize; public Guid ClassGuid; public uint DevInst; public IntPtr Reserved; }

        [StructLayout(LayoutKind.Sequential)]
        private struct HIDP_CAPS
        {
            public ushort Usage, UsagePage, InputReportByteLength, OutputReportByteLength, FeatureReportByteLength;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
            public ushort NumberLinkCollectionNodes, NumberInputButtonCaps, NumberInputValueCaps, NumberInputDataIndices,
                          NumberOutputButtonCaps, NumberOutputValueCaps, NumberOutputDataIndices,
                          NumberFeatureButtonCaps, NumberFeatureValueCaps, NumberFeatureDataIndices;
        }

        [DllImport("hid.dll")] private static extern void HidD_GetHidGuid(out Guid hidGuid);
        [DllImport("hid.dll", SetLastError = true)] private static extern bool HidD_GetPreparsedData(SafeFileHandle h, out IntPtr preparsed);
        [DllImport("hid.dll")] private static extern bool HidD_FreePreparsedData(IntPtr preparsed);
        [DllImport("hid.dll")] private static extern int HidP_GetCaps(IntPtr preparsed, out HIDP_CAPS caps);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr SetupDiGetClassDevsW(ref Guid classGuid, IntPtr enumerator, IntPtr hwndParent, uint flags);
        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr devInfo, ref Guid guid, uint index, ref SP_DEVICE_INTERFACE_DATA data);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetupDiGetDeviceInterfaceDetailW(IntPtr set, ref SP_DEVICE_INTERFACE_DATA data, IntPtr detail,
                                                                    uint detailSize, out uint requiredSize, ref SP_DEVINFO_DATA devInfo);
        [DllImport("setupapi.dll", SetLastError = true)] private static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);

        [DllImport("cfgmgr32.dll")] private static extern int CM_Get_Parent(out uint parent, uint devInst, uint flags);
        [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
        private static extern int CM_Get_DevNode_Registry_PropertyW(uint devInst, uint property, out uint regDataType,
                                                                     IntPtr buffer, ref uint length, uint flags);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFileW(string fileName, uint access, uint share, IntPtr security,
                                                         uint creation, uint flags, IntPtr template);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool ReadFile(SafeFileHandle h, IntPtr buffer, uint toRead, IntPtr read, IntPtr overlapped);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetOverlappedResult(SafeFileHandle h, IntPtr overlapped, out uint transferred, bool wait);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CancelIoEx(SafeFileHandle h, IntPtr overlapped);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateEventW(IntPtr security, bool manualReset, bool initialState, string name);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool ResetEvent(IntPtr h);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern uint WaitForSingleObject(IntPtr h, uint ms);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool CloseHandle(IntPtr h);
#else
        private static class Interlocked { public static long Read(ref long v) => v; }
        private static class Volatile { public static int Read(ref int v) => v; }
#endif
    }
}
