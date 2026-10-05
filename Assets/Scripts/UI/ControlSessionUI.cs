// ControlSessionUI.cs
// 待機畫面、操控中提示、閒置倒數、退出進度圈、工作人員設定畫面的顯示層。
//
// 套用位置：Assets/Scripts/UI/ControlSessionUI.cs
// 版面由 Editor 工具建立：Tools > Space Cupola > 4. 建立待機畫面與雙搖桿管理
// 此元件只負責「顯示」，所有判斷由 ControlSessionManager 決定。

using SpaceCupola.Controls;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SpaceCupola.UI
{
    public class ControlSessionUI : MonoBehaviour
    {
        public enum Mode { Standby, Playing, Setup }
        public enum CardState { Ready, Disconnected, Selected }

        [System.Serializable]
        public class Card
        {
            public RectTransform root;
            public Image background;
            public Image accent;
            public TMP_Text title;
            public TMP_Text subtitle;
            public TMP_Text status;
        }

        [Header("待機畫面")]
        [SerializeField] private CanvasGroup standbyGroup;
        [SerializeField] private TMP_Text standbyTitle;
        [SerializeField] private TMP_Text standbyPrompt;
        [SerializeField] private Card childCard;
        [SerializeField] private Card adultCard;
        [SerializeField] private TMP_Text staffNotice;

        [Header("操控中")]
        [SerializeField] private CanvasGroup playingGroup;
        [SerializeField] private TMP_Text seatBadge;
        [SerializeField] private TMP_Text exitHint;
        [SerializeField] private Image exitRing;

        [Header("閒置倒數")]
        [SerializeField] private CanvasGroup countdownGroup;
        [SerializeField] private TMP_Text countdownText;

        [Header("工作人員設定")]
        [SerializeField] private CanvasGroup setupGroup;
        [SerializeField] private TMP_Text setupText;

        [Header("文字")]
        [SerializeField] private string readyText        = "推動搖桿開始  →";
        [SerializeField] private string disconnectedText = "搖桿未連接";
        [SerializeField] private string selectedText     = "開始！";
        [SerializeField] private string childBadge       = "小孩專用｜操控中";
        [SerializeField] private string adultBadge       = "大人專用｜操控中";
        [SerializeField] private string unassignedBadge  = "操控中";
        [SerializeField] private string countdownFormat  = "無人操作，<b>{0}</b> 秒後返回待機畫面";

        [Header("外觀")]
        [SerializeField] private float fadeSpeed = 4f;
        [SerializeField] private Color accentReady    = new Color(0.45f, 1.00f, 0.80f, 0.55f);
        [SerializeField] private Color accentSelected = new Color(0.45f, 1.00f, 0.80f, 1.00f);
        [SerializeField] private Color accentOff      = new Color(1f, 1f, 1f, 0.15f);

        private Mode _mode = Mode.Standby;
        private bool _countdownVisible;
        private CardState _childState, _adultState;

        // ── 由 ControlSessionManager 呼叫 ─────────────────────────────

        public void SetMode(Mode mode)
        {
            _mode = mode;
            if (mode != Mode.Playing) { SetCountdown(-1); SetExitProgress(0f); }
        }

        public void SetCard(Seat seat, CardState state)
        {
            if (seat == Seat.Child) _childState = state;
            else if (seat == Seat.Adult) _adultState = state;
        }

        public void SetPlayingSeat(Seat seat)
        {
            if (seatBadge == null) return;
            seatBadge.text = seat == Seat.Child ? childBadge : seat == Seat.Adult ? adultBadge : unassignedBadge;
        }

        public void SetExitHint(string text)
        {
            if (exitHint != null) exitHint.text = text;
        }

        public void SetExitProgress(float t01)
        {
            if (exitRing == null) return;
            exitRing.fillAmount = Mathf.Clamp01(t01);
            exitRing.enabled = t01 > 0.001f;
        }

        /// <summary>顯示剩餘秒數；傳入負值隱藏。</summary>
        public void SetCountdown(int seconds)
        {
            _countdownVisible = seconds >= 0;
            if (_countdownVisible && countdownText != null)
                countdownText.text = string.Format(countdownFormat, seconds);
        }

        public void SetSetupMessage(string message)
        {
            if (setupText != null) setupText.text = message;
        }

        public void SetStaffNotice(string message)
        {
            if (staffNotice == null) return;
            staffNotice.text = message ?? string.Empty;
            staffNotice.enabled = !string.IsNullOrEmpty(message);
        }

        // ─────────────────────────────────────────────────────────────

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            Fade(standbyGroup,   _mode == Mode.Standby, dt);
            Fade(playingGroup,   _mode == Mode.Playing, dt);
            Fade(setupGroup,     _mode == Mode.Setup, dt);
            Fade(countdownGroup, _mode == Mode.Playing && _countdownVisible, dt);

            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 2.4f);
            ApplyCard(childCard, _childState, pulse);
            ApplyCard(adultCard, _adultState, pulse);
        }

        private void Fade(CanvasGroup g, bool visible, float dt)
        {
            if (g == null) return;
            g.alpha = Mathf.MoveTowards(g.alpha, visible ? 1f : 0f, dt * fadeSpeed);
            g.blocksRaycasts = false;
            g.interactable = false;
        }

        private void ApplyCard(Card c, CardState state, float pulse)
        {
            if (c == null || c.root == null) return;

            Color accent;
            float scale;
            string status;
            switch (state)
            {
                case CardState.Selected:
                    accent = accentSelected; scale = 1.06f; status = selectedText; break;
                case CardState.Disconnected:
                    accent = accentOff; scale = 1f; status = disconnectedText; break;
                default:
                    accent = Color.Lerp(accentReady, accentSelected, pulse * 0.6f);
                    scale = 1f + 0.015f * pulse;
                    status = readyText; break;
            }

            if (c.accent != null) c.accent.color = accent;
            if (c.status != null && c.status.text != status) c.status.text = status;
            if (c.status != null) c.status.alpha = state == CardState.Disconnected ? 0.5f : 1f;
            c.root.localScale = Vector3.Lerp(c.root.localScale, Vector3.one * scale, Time.unscaledDeltaTime * 10f);
        }
    }
}
