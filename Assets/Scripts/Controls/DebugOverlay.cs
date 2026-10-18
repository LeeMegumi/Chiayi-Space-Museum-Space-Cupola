// DebugOverlay.cs
// 所有除錯畫面的統一開關：啟動時隱藏，鍵盤 F10 切換。
//
// 套用位置：Assets/Scripts/Controls/DebugOverlay.cs
// 使用者：JoystickProbe（左側搖桿數值）、SpacecraftOrbitController（右上相機數值）、
//        ControlSessionManager（操控權與 USB 插孔識別狀態）。

using UnityEngine;
using UnityEngine.InputSystem;

namespace SpaceCupola.Controls
{
    public static class DebugOverlay
    {
        /// <summary>除錯畫面是否顯示。程式啟動時為隱藏。</summary>
        public static bool Visible { get; private set; }

        private static int s_LastToggleFrame = -1;

        /// <summary>
        /// 檢查 F10。多個元件在同一帧呼叫也只會切換一次。
        /// </summary>
        public static void HandleHotkey()
        {
            Keyboard kb = Keyboard.current;
            if (kb == null || !kb.f10Key.wasPressedThisFrame) return;
            if (Time.frameCount == s_LastToggleFrame) return;
            s_LastToggleFrame = Time.frameCount;
            Visible = !Visible;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlay()
        {
            // 編輯器關閉 Domain Reload 時，靜態值會殘留到下一次 Play；每次啟動都重設為隱藏
            Visible = false;
            s_LastToggleFrame = -1;
        }
    }
}
