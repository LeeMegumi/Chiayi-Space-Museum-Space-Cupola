// SpaceCupolaStandbyBuilder.cs
// 建立待機畫面 UI 與雙搖桿操控權管理器。
//
// 套用位置：Assets/Scripts/Editor/SpaceCupolaStandbyBuilder.cs（必須在 Editor 資料夾內）
// 使用方式：Tools > Space Cupola > 4. 建立待機畫面與雙搖桿管理
//           （需先執行 1. 建立中文字體資產、3. 建立整合資訊面板）
//
// 建立內容（1920×1080 參考解析度）：
//   Canvas/ControlSessionUI
//     Standby   標題、提示、左「小孩專用」右「大人專用」兩張卡片、工作人員提示
//     Playing   左上「大人專用｜操控中」標籤、退出鍵說明與長按進度圈
//     Countdown 上方中央「無人操作，N 秒後返回待機畫面」
//     Setup     中央工作人員設定說明（F9）
//   場景根目錄/ControlSessionManager（並自動連結 Main Camera 的 SpacecraftOrbitController、
//     整合資訊面板、畫面中央準心）
// 全部記錄在同一個 Undo 群組；重複執行會刪除舊的 UI 後重建。

#if UNITY_EDITOR
using SpaceCupola.Controls;
using SpaceCupola.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace SpaceCupola.EditorTools
{
    public static class SpaceCupolaStandbyBuilder
    {
        private const string UiName      = "ControlSessionUI";
        private const string ManagerName = "ControlSessionManager";
        private const string UndoName    = "Space Cupola 待機畫面";

        // ── 配色（與整合資訊面板一致）──
        private static readonly Color Dim        = new Color(0.01f, 0.02f, 0.05f, 0.40f);
        private static readonly Color PanelColor = new Color(0.04f, 0.07f, 0.13f, 0.86f);
        private static readonly Color Accent     = new Color(0.45f, 1.00f, 0.80f, 1.00f);
        private static readonly Color SubColor   = new Color(0.70f, 0.78f, 0.88f, 1.00f);
        private static readonly Color WarnColor  = new Color(0.55f, 0.18f, 0.08f, 0.88f);
        private static readonly Color NoticeColor= new Color(1.00f, 0.75f, 0.35f, 1.00f);

        [MenuItem("Tools/Space Cupola/4. 建立待機畫面與雙搖桿管理")]
        private static void Build()
        {
            TMP_FontAsset regular = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(SpaceCupolaFontAssetBuilder.RegularAssetPath);
            TMP_FontAsset bold    = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(SpaceCupolaFontAssetBuilder.BoldAssetPath);
            if (regular == null || bold == null)
            {
                EditorUtility.DisplayDialog("Space Cupola", "找不到中文字體資產。\n請先執行 1. 建立中文字體資產。", "OK");
                return;
            }

            Camera cam = Camera.main;
            if (cam == null || cam.GetComponent<CameraControllerInSpace>() == null)
            {
                EditorUtility.DisplayDialog("Space Cupola", "找不到帶有 CameraControllerInSpace 的 Main Camera。", "OK");
                return;
            }

            LocationInfoPanel info = Object.FindFirstObjectByType<LocationInfoPanel>(FindObjectsInactive.Include);
            Canvas canvas = info != null ? info.GetComponentInParent<Canvas>(true) : Object.FindFirstObjectByType<Canvas>(FindObjectsInactive.Include);
            if (canvas == null)
            {
                EditorUtility.DisplayDialog("Space Cupola", "場景中找不到 Canvas。", "OK");
                return;
            }
            canvas = canvas.rootCanvas;

            if (!EditorUtility.DisplayDialog("Space Cupola",
                    "將建立：\n" +
                    "• 待機畫面（左：小孩專用、右：大人專用）\n" +
                    "• 操控中提示、閒置倒數、退出進度圈、工作人員設定畫面\n" +
                    "• 場景物件 ControlSessionManager（操控權管理）\n" +
                    (cam.GetComponent<SpacecraftOrbitController>() == null ? "• 在 Main Camera 加上 SpacecraftOrbitController\n" : "") +
                    "\n已存在的待機畫面會刪除後重建；全部可用 Ctrl+Z 還原。",
                    "建立", "取消"))
                return;

            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(UndoName);

            // ── 相機控制器 ──
            SpacecraftOrbitController orbit = cam.GetComponent<SpacecraftOrbitController>();
            if (orbit == null) orbit = Undo.AddComponent<SpacecraftOrbitController>(cam.gameObject);

            // ── UI ──
            Transform old = canvas.transform.Find(UiName);
            if (old != null) Undo.DestroyObjectImmediate(old.gameObject);
            ControlSessionUI ui = BuildUi(canvas, regular, bold);

            // ── 管理器 ──
            ControlSessionManager mgr = Object.FindFirstObjectByType<ControlSessionManager>(FindObjectsInactive.Include);
            if (mgr == null)
            {
                var go = new GameObject(ManagerName);
                Undo.RegisterCreatedObjectUndo(go, UndoName);
                SceneManager_MoveToScene(go, canvas.gameObject.scene);
                mgr = go.AddComponent<ControlSessionManager>();
            }

            var so = new SerializedObject(mgr);
            so.FindProperty("orbit").objectReferenceValue = orbit;
            so.FindProperty("ui").objectReferenceValue = ui;
            so.FindProperty("infoPanel").objectReferenceValue = info != null ? info.GetComponent<CanvasGroup>() : null;
            Transform reticle = canvas.transform.Find("CenterReticle");
            so.FindProperty("reticle").objectReferenceValue = reticle != null ? reticle.gameObject : null;
            so.ApplyModifiedProperties();

            Undo.CollapseUndoOperations(group);
            EditorSceneManager.MarkSceneDirty(canvas.gameObject.scene);
            Selection.activeGameObject = mgr.gameObject;

            Debug.Log("[SpaceCupolaStandbyBuilder] 待機畫面與雙搖桿管理已建立。\n" +
                      $"  整合資訊面板：{(info != null ? "已連結" : "未找到（請先執行 3.）")}　" +
                      $"準心：{(reticle != null ? "已連結" : "未找到")}\n" +
                      "  下一步：按 Play，在待機畫面按 F9 設定大人／小孩搖桿與退出鍵。", mgr);
        }

        private static void SceneManager_MoveToScene(GameObject go, UnityEngine.SceneManagement.Scene scene)
        {
            if (go.scene != scene) UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, scene);
        }

        // ─────────────────────────────────────────────────────────────

        private static ControlSessionUI BuildUi(Canvas canvas, TMP_FontAsset regular, TMP_FontAsset bold)
        {
            var root = NewRect(UiName, canvas.transform);
            Undo.RegisterCreatedObjectUndo(root.gameObject, UndoName);
            Stretch(root);
            root.SetAsLastSibling();   // 蓋在資訊面板之上
            var ui = root.gameObject.AddComponent<ControlSessionUI>();

            Sprite rounded = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Background.psd");
            Sprite knob    = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");

            // ── 待機 ──
            RectTransform standby = NewRect("Standby", root); Stretch(standby);
            CanvasGroup standbyGroup = standby.gameObject.AddComponent<CanvasGroup>();
            standbyGroup.alpha = 1f;

            Image dim = NewImage("Dim", standby, null, Dim); Stretch(dim.rectTransform);

            TMP_Text title = NewText("Title", standby, bold, 76f, Color.white, TextAlignmentOptions.Center);
            title.text = "穹頂觀測艙";
            Place(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -150f), new Vector2(1400f, 110f));

            TMP_Text sub = NewText("Subtitle", standby, regular, 28f, SubColor, TextAlignmentOptions.Center);
            sub.text = "SPACE CUPOLA　·　從太空看地球";
            sub.characterSpacing = 8f;
            Place(sub.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -225f), new Vector2(1400f, 50f));

            TMP_Text prompt = NewText("Prompt", standby, regular, 34f, Accent, TextAlignmentOptions.Center);
            prompt.text = "推動你面前的搖桿，開始環繞地球";
            Place(prompt.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 470f), new Vector2(1400f, 60f));

            // 左：小孩　右：大人（對應現場擺放位置）
            ControlSessionUI.Card child = NewCard("Card_Child", standby, new Vector2(-320f, 200f), "小孩專用", "KIDS　·　較低的搖桿", regular, bold, rounded);
            ControlSessionUI.Card adult = NewCard("Card_Adult", standby, new Vector2( 320f, 200f), "大人專用", "ADULTS　·　較高的搖桿", regular, bold, rounded);

            TMP_Text notice = NewText("StaffNotice", standby, regular, 20f, NoticeColor, TextAlignmentOptions.Center);
            Place(notice.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 48f), new Vector2(1400f, 36f));
            notice.text = "";

            // ── 操控中 ──
            RectTransform playing = NewRect("Playing", root); Stretch(playing);
            CanvasGroup playingGroup = playing.gameObject.AddComponent<CanvasGroup>();
            playingGroup.alpha = 0f;

            Image badgeBg = NewImage("Badge", playing, rounded, PanelColor);
            badgeBg.type = Image.Type.Sliced;
            Place(badgeBg.rectTransform, new Vector2(0f, 1f), new Vector2(48f, -40f), new Vector2(380f, 56f), pivot: new Vector2(0f, 1f));
            Image badgeAccent = NewImage("Accent", badgeBg.rectTransform, null, Accent);
            badgeAccent.rectTransform.anchorMin = new Vector2(0f, 0f);
            badgeAccent.rectTransform.anchorMax = new Vector2(0f, 1f);
            badgeAccent.rectTransform.pivot = new Vector2(0f, 0.5f);
            badgeAccent.rectTransform.sizeDelta = new Vector2(6f, -16f);
            badgeAccent.rectTransform.anchoredPosition = new Vector2(14f, 0f);
            TMP_Text badge = NewText("Text", badgeBg.rectTransform, bold, 26f, Color.white, TextAlignmentOptions.MidlineLeft);
            Stretch(badge.rectTransform, new Vector2(32f, 0f), new Vector2(-16f, 0f));
            badge.text = "大人專用｜操控中";

            Image ring = NewImage("ExitRing", playing, knob, Accent);
            ring.type = Image.Type.Filled;
            ring.fillMethod = Image.FillMethod.Radial360;
            ring.fillOrigin = (int)Image.Origin360.Top;
            ring.fillClockwise = true;
            ring.fillAmount = 0f;
            ring.enabled = false;
            Place(ring.rectTransform, new Vector2(0f, 1f), new Vector2(60f, -122f), new Vector2(28f, 28f));

            TMP_Text hint = NewText("ExitHint", playing, regular, 20f, SubColor, TextAlignmentOptions.MidlineLeft);
            Place(hint.rectTransform, new Vector2(0f, 1f), new Vector2(84f, -108f), new Vector2(700f, 30f), pivot: new Vector2(0f, 1f));
            hint.text = "長按搖桿上的退出鍵 2 秒，返回待機畫面";

            // ── 閒置倒數 ──
            Image cdBg = NewImage("Countdown", root, rounded, WarnColor);
            cdBg.type = Image.Type.Sliced;
            Place(cdBg.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -48f), new Vector2(760f, 68f), pivot: new Vector2(0.5f, 1f));
            CanvasGroup cdGroup = cdBg.gameObject.AddComponent<CanvasGroup>();
            cdGroup.alpha = 0f;
            TMP_Text cdText = NewText("Text", cdBg.rectTransform, regular, 30f, Color.white, TextAlignmentOptions.Center);
            Stretch(cdText.rectTransform);
            cdText.text = "無人操作，<b>10</b> 秒後返回待機畫面";

            // ── 工作人員設定 ──
            Image setupBg = NewImage("Setup", root, rounded, new Color(0.02f, 0.03f, 0.06f, 0.94f));
            setupBg.type = Image.Type.Sliced;
            Place(setupBg.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1100f, 440f));
            CanvasGroup setupGroup = setupBg.gameObject.AddComponent<CanvasGroup>();
            setupGroup.alpha = 0f;
            TMP_Text setupText = NewText("Text", setupBg.rectTransform, regular, 44f, Color.white, TextAlignmentOptions.Center);
            Stretch(setupText.rectTransform, new Vector2(40f, 30f), new Vector2(-40f, -30f));
            setupText.textWrappingMode = TextWrappingModes.Normal;
            setupText.enableAutoSizing = false;
            setupText.text = "工作人員設定";

            // ── 連結 ──
            var so = new SerializedObject(ui);
            so.FindProperty("standbyGroup").objectReferenceValue = standbyGroup;
            so.FindProperty("standbyTitle").objectReferenceValue = title;
            so.FindProperty("standbyPrompt").objectReferenceValue = prompt;
            AssignCard(so.FindProperty("childCard"), child);
            AssignCard(so.FindProperty("adultCard"), adult);
            so.FindProperty("staffNotice").objectReferenceValue = notice;
            so.FindProperty("playingGroup").objectReferenceValue = playingGroup;
            so.FindProperty("seatBadge").objectReferenceValue = badge;
            so.FindProperty("exitHint").objectReferenceValue = hint;
            so.FindProperty("exitRing").objectReferenceValue = ring;
            so.FindProperty("countdownGroup").objectReferenceValue = cdGroup;
            so.FindProperty("countdownText").objectReferenceValue = cdText;
            so.FindProperty("setupGroup").objectReferenceValue = setupGroup;
            so.FindProperty("setupText").objectReferenceValue = setupText;
            so.ApplyModifiedPropertiesWithoutUndo();

            return ui;
        }

        private static ControlSessionUI.Card NewCard(string name, RectTransform parent, Vector2 pos, string title, string subtitle,
                                                     TMP_FontAsset regular, TMP_FontAsset bold, Sprite rounded)
        {
            var card = new ControlSessionUI.Card();

            Image bg = NewImage(name, parent, rounded, PanelColor);
            bg.type = Image.Type.Sliced;
            Place(bg.rectTransform, new Vector2(0.5f, 0f), pos, new Vector2(540f, 230f));
            card.root = bg.rectTransform;
            card.background = bg;

            Image accent = NewImage("Accent", bg.rectTransform, null, Accent);
            accent.rectTransform.anchorMin = new Vector2(0f, 1f);
            accent.rectTransform.anchorMax = new Vector2(1f, 1f);
            accent.rectTransform.pivot = new Vector2(0.5f, 1f);
            accent.rectTransform.sizeDelta = new Vector2(-48f, 6f);
            accent.rectTransform.anchoredPosition = new Vector2(0f, -14f);
            card.accent = accent;

            TMP_Text t = NewText("Title", bg.rectTransform, bold, 56f, Color.white, TextAlignmentOptions.Center);
            Place(t.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -40f), new Vector2(500f, 76f), pivot: new Vector2(0.5f, 1f));
            t.text = title;
            card.title = t;

            TMP_Text s = NewText("Subtitle", bg.rectTransform, regular, 22f, SubColor, TextAlignmentOptions.Center);
            Place(s.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -118f), new Vector2(500f, 34f), pivot: new Vector2(0.5f, 1f));
            s.text = subtitle;
            card.subtitle = s;

            TMP_Text st = NewText("Status", bg.rectTransform, regular, 28f, Accent, TextAlignmentOptions.Center);
            Place(st.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 26f), new Vector2(500f, 44f), pivot: new Vector2(0.5f, 0f));
            st.text = "推動搖桿開始  →";
            card.status = st;

            return card;
        }

        private static void AssignCard(SerializedProperty p, ControlSessionUI.Card c)
        {
            p.FindPropertyRelative("root").objectReferenceValue = c.root;
            p.FindPropertyRelative("background").objectReferenceValue = c.background;
            p.FindPropertyRelative("accent").objectReferenceValue = c.accent;
            p.FindPropertyRelative("title").objectReferenceValue = c.title;
            p.FindPropertyRelative("subtitle").objectReferenceValue = c.subtitle;
            p.FindPropertyRelative("status").objectReferenceValue = c.status;
        }

        // ── 小工具 ───────────────────────────────────────────────────

        private static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = parent.gameObject.layer;
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            return rt;
        }

        private static Image NewImage(string name, Transform parent, Sprite sprite, Color color)
        {
            RectTransform rt = NewRect(name, parent);
            rt.gameObject.AddComponent<CanvasRenderer>();
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        private static TMP_Text NewText(string name, Transform parent, TMP_FontAsset font, float size, Color color,
                                        TextAlignmentOptions align)
        {
            RectTransform rt = NewRect(name, parent);
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            t.font = font;
            t.fontSharedMaterial = font.material;
            t.fontSize = size;
            t.enableAutoSizing = true;
            t.fontSizeMax = size;
            t.fontSizeMin = Mathf.Max(14f, size * 0.6f);
            t.color = color;
            t.alignment = align;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Ellipsis;
            t.richText = true;
            t.raycastTarget = false;
            return t;
        }

        private static void Stretch(RectTransform rt) => Stretch(rt, Vector2.zero, Vector2.zero);

        private static void Stretch(RectTransform rt, Vector2 offsetMin, Vector2 offsetMax)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = offsetMin;
            rt.offsetMax = offsetMax;
        }

        private static void Place(RectTransform rt, Vector2 anchor, Vector2 pos, Vector2 size, Vector2? pivot = null)
        {
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = pivot ?? new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
        }
    }
}
#endif
