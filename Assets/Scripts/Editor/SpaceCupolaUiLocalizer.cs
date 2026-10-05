// SpaceCupolaUiLocalizer.cs
// 把場景中的資訊面板換成中文字體、翻譯靜態標籤，並重新排版。
//
// 套用位置：Assets/Scripts/Editor/SpaceCupolaUiLocalizer.cs（必須在 Editor 資料夾內）
// 使用方式：選單 Tools > Space Cupola > 2. 套用中文 UI 與排版
//           （需先執行 1. 建立中文字體資產）
//
// 所有修改皆記錄於同一個 Undo 群組 —— 一次 Ctrl+Z 即可整批還原。
// 可重複執行：已翻譯的標籤不會再被處理，版面設定會被重新套用成相同結果。
//
// ── 為什麼要重排版，而不只是換字 ─────────────────────────────────
// 原版每列（GridLayoutGroup 的格子）寬 165.66，但：
//   Label 框寬 300、錨在格子左緣 → 超出格子
//   Value 框寬 200、錨點在格子「右緣之外」開始 → 整個在格子外
// 英文是長度剛好湊得起來才看似正常。中文字寬不同，會直接錯位。
// 因此改為：格子寬 = 整個 Grid 寬，左半標籤、右半數值，兩者都限制在格子內並自動縮字。

#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using SpaceCupola.Localization;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace SpaceCupola.EditorTools
{
    public static class SpaceCupolaUiLocalizer
    {
        // ── 版面參數（1920×1080 參考解析度）──
        private const float RowHeight  = 40f;
        private const float RowSpacing = 6f;
        private const float LabelRatio = 0.5f;    // 標籤佔一列寬度的比例
        private const float LabelMaxFontSize = 24f;
        private const float LabelMinFontSize = 14f;
        private const float TitleMaxFontSize = 42f;
        private const float TitleMinFontSize = 20f;

        private const string ReticleName = "CenterReticle";

        // 靜態標籤的英文副標：去掉冒號、修正原資產的拼字（Climat → Climate）
        private static readonly Dictionary<string, string> SecondaryEnglish = new Dictionary<string, string>
        {
            { "Population Count:", "Population" },
            { "Coordinates:",      "Coordinates" },
            { "Country:",          "Country" },
            { "Religion",          "Religion" },
            { "Religion:",         "Religion" },
            { "Climat",            "Climate" },
            { "Climat:",           "Climate" },
            { "Disaster:",         "Disaster" },
            { "Transport:",        "Transport" },
            { "Science:",          "Science" },
            { "Wealth:",           "Wealth" },
        };

        // 執行期會被程式覆寫的佔位文字：只換成中文，不加英文副標
        private static readonly HashSet<string> PlaceholderOnly = new HashSet<string>
        {
            "Country Name", "Selected City", "Name", "No Country", "N/A", "Lock Camera",
        };

        [MenuItem("Tools/Space Cupola/2. 套用中文 UI 與排版")]
        private static void Apply()
        {
            TMP_FontAsset regular = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(SpaceCupolaFontAssetBuilder.RegularAssetPath);
            TMP_FontAsset bold    = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(SpaceCupolaFontAssetBuilder.BoldAssetPath);
            if (regular == null || bold == null)
            {
                EditorUtility.DisplayDialog("Space Cupola",
                    "找不到中文字體資產。\n請先執行 Tools > Space Cupola > 1. 建立中文字體資產。", "OK");
                return;
            }

            WindowLayerInfo info = Object.FindFirstObjectByType<WindowLayerInfo>(FindObjectsInactive.Include);
            if (info == null)
            {
                EditorUtility.DisplayDialog("Space Cupola", "場景中找不到 WindowLayerInfo（國家資訊面板）。", "OK");
                return;
            }

            Canvas canvas = info.GetComponentInParent<Canvas>(true);
            if (canvas != null) canvas = canvas.rootCanvas;
            if (canvas == null)
            {
                EditorUtility.DisplayDialog("Space Cupola", "找不到國家資訊面板所在的 Canvas。", "OK");
                return;
            }

            if (!EditorUtility.DisplayDialog("Space Cupola",
                    $"將修改 Canvas「{canvas.name}」底下的文字與版面：\n\n" +
                    "• 所有 TMP 文字改用 LINE Seed TW\n" +
                    "• 靜態標籤翻成「中文 English」\n" +
                    "• 資訊列重排為左標籤、右數值，自動縮字\n" +
                    "• 隱藏人口／財富兩列並縮短面板\n" +
                    "• 在畫面中央加入準心\n\n" +
                    "全部記錄在同一個 Undo，可用 Ctrl+Z 還原。",
                    "套用", "取消"))
                return;

            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Space Cupola 中文 UI 與排版");

            var report = new StringBuilder();
            report.AppendLine("=== Space Cupola 中文 UI 與排版 ===");

            ApplyFontsAndTranslations(canvas, regular, bold, report);
            ApplyRowLayouts(canvas, report);
            HidePopulationAndWealth(info, report);
            FitPanelHeight(info, report);
            EnsureReticle(canvas, report);

            Undo.CollapseUndoOperations(undoGroup);
            EditorSceneManager.MarkSceneDirty(canvas.gameObject.scene);

            report.AppendLine();
            report.AppendLine("完成。請在 Game 視圖（1920×1080）目視確認後存檔；不滿意可 Ctrl+Z 整批還原。");
            Debug.Log(report.ToString(), canvas);
        }

        // ─────────────────────────────────────────────────────────────

        private static bool IsTitle(TMP_Text t) =>
            t.transform.parent != null && t.transform.parent.name == "Top";

        private static void ApplyFontsAndTranslations(Canvas canvas, TMP_FontAsset regular, TMP_FontAsset bold, StringBuilder report)
        {
            int fontChanged = 0, translated = 0;

            foreach (TMP_Text t in canvas.GetComponentsInChildren<TMP_Text>(true))
            {
                Undo.RecordObject(t, "Space Cupola 中文 UI");

                bool title = IsTitle(t);
                TMP_FontAsset target = title ? bold : regular;
                if (t.font != target)
                {
                    t.font = target;
                    t.fontSharedMaterial = target.material;
                    fontChanged++;
                }

                // 粗細改由字重檔（Bd）表現，不再用假粗體
                t.fontStyle = FontStyles.Normal;
                t.richText = true;                                   // 雙語副標需要 <size>
                t.textWrappingMode = TextWrappingModes.NoWrap;
                t.overflowMode = TextOverflowModes.Ellipsis;
                t.enableAutoSizing = true;
                t.fontSizeMax = title ? TitleMaxFontSize : LabelMaxFontSize;
                t.fontSizeMin = title ? TitleMinFontSize : LabelMinFontSize;

                string source = t.text != null ? t.text.Trim() : string.Empty;
                if (TryTranslate(source, title, out string result))
                {
                    t.text = result;
                    translated++;
                    report.AppendLine($"  翻譯：{GetPath(t.transform, canvas.transform)}  「{source}」→「{result}」");
                }

                EditorUtility.SetDirty(t);
            }

            report.AppendLine($"字體：{fontChanged} 個文字元件改用 LINE Seed TW（標題用 Bd，其餘用 Rg）");
            report.AppendLine($"翻譯：{translated} 個靜態標籤");
        }

        private static bool TryTranslate(string source, bool isTitle, out string result)
        {
            result = source;
            if (string.IsNullOrEmpty(source)) return false;

            string zh = LocalizationTable.UiLabel(source);
            if (zh == source) return false;   // 查無譯名（含已翻譯過的中文）

            if (isTitle || PlaceholderOnly.Contains(source))
            {
                result = zh;
            }
            else
            {
                string en = SecondaryEnglish.TryGetValue(source, out string mapped)
                    ? mapped
                    : source.TrimEnd(':', '：').Trim();
                result = LocalizationTable.ComposeBilingual(zh, en);
            }
            return true;
        }

        /// <summary>把資訊面板的每一列改成「左半標籤、右半數值」。</summary>
        private static void ApplyRowLayouts(Canvas canvas, StringBuilder report)
        {
            int grids = 0, rows = 0;

            foreach (GridLayoutGroup grid in canvas.GetComponentsInChildren<GridLayoutGroup>(true))
            {
                // 只處理「單欄」的資訊列表（SpeedManager 的按鈕列是 FixedRowCount，略過）
                if (grid.constraint != GridLayoutGroup.Constraint.FixedColumnCount || grid.constraintCount != 1)
                    continue;

                var gridRect = (RectTransform)grid.transform;
                float width = gridRect.rect.width;
                if (width < 1f) width = 380f;   // 物件停用時 rect 可能為 0，給合理預設

                Undo.RecordObject(grid, "Space Cupola 中文 UI");
                grid.cellSize = new Vector2(width, RowHeight);
                grid.spacing  = new Vector2(0f, RowSpacing);
                EditorUtility.SetDirty(grid);
                grids++;

                foreach (Transform row in grid.transform)
                {
                    var texts = new List<TMP_Text>();
                    foreach (Transform child in row)
                    {
                        var t = child.GetComponent<TMP_Text>();
                        if (t != null) texts.Add(t);
                    }
                    if (texts.Count < 2) continue;

                    TMP_Text label = texts[0];
                    TMP_Text value = texts[1];

                    SetStretch(label.rectTransform, new Vector2(0f, 0f), new Vector2(LabelRatio, 1f), new Vector2(0f, 0.5f));
                    SetStretch(value.rectTransform, new Vector2(LabelRatio, 0f), new Vector2(1f, 1f), new Vector2(1f, 0.5f));

                    Undo.RecordObject(label, "Space Cupola 中文 UI");
                    Undo.RecordObject(value, "Space Cupola 中文 UI");
                    label.horizontalAlignment = HorizontalAlignmentOptions.Left;
                    label.verticalAlignment   = VerticalAlignmentOptions.Middle;
                    value.horizontalAlignment = HorizontalAlignmentOptions.Right;
                    value.verticalAlignment   = VerticalAlignmentOptions.Middle;
                    EditorUtility.SetDirty(label);
                    EditorUtility.SetDirty(value);
                    rows++;
                }
            }

            report.AppendLine($"排版：{grids} 個資訊列表、{rows} 列改為左標籤／右數值（列高 {RowHeight}、間距 {RowSpacing}）");
        }

        private static void SetStretch(RectTransform rt, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot)
        {
            Undo.RecordObject(rt, "Space Cupola 中文 UI");
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot     = pivot;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            EditorUtility.SetDirty(rt);
        }

        /// <summary>依 WindowLayerInfo.hidePopulationAndWealth 隱藏兩列，並把列物件填入其欄位。</summary>
        private static void HidePopulationAndWealth(WindowLayerInfo info, StringBuilder report)
        {
            var so = new SerializedObject(info);
            SerializedProperty hide     = so.FindProperty("hidePopulationAndWealth");
            SerializedProperty popProp  = so.FindProperty("populationRow");
            SerializedProperty wlthProp = so.FindProperty("wealthRow");
            if (hide == null || popProp == null || wlthProp == null)
            {
                report.AppendLine("隱藏列：WindowLayerInfo 欄位不存在（腳本版本不符？），略過");
                return;
            }

            GridLayoutGroup grid = info.GetComponentInChildren<GridLayoutGroup>(true);
            if (grid == null) { report.AppendLine("隱藏列：找不到資訊列表，略過"); return; }

            Transform pop  = grid.transform.Find("Population Count");
            Transform wlth = grid.transform.Find("Wealth");

            if (pop  != null) popProp.objectReferenceValue  = pop.gameObject;
            if (wlth != null) wlthProp.objectReferenceValue = wlth.gameObject;
            so.ApplyModifiedProperties();   // 會自動記錄 Undo

            if (!hide.boolValue)
            {
                report.AppendLine("隱藏列：hidePopulationAndWealth 為關閉，只填入欄位、不隱藏");
                return;
            }

            foreach (Transform row in new[] { pop, wlth })
            {
                if (row == null) continue;
                Undo.RecordObject(row.gameObject, "Space Cupola 中文 UI");
                row.gameObject.SetActive(false);
            }
            report.AppendLine($"隱藏列：{(pop ? "人口" : "")}{(pop && wlth ? "、" : "")}{(wlth ? "財富" : "")}（單位存疑，見 WindowLayerInfo 說明）");
        }

        /// <summary>依可見列數縮放國家資訊面板高度，並固定其底緣位置。</summary>
        private static void FitPanelHeight(WindowLayerInfo info, StringBuilder report)
        {
            var panel = info.transform as RectTransform;
            GridLayoutGroup grid = info.GetComponentInChildren<GridLayoutGroup>(true);
            if (panel == null || grid == null) return;

            int visible = 0;
            foreach (Transform row in grid.transform) if (row.gameObject.activeSelf) visible++;
            if (visible == 0) return;

            var gridRect = (RectTransform)grid.transform;
            // 面板與列表之間的上下留白（標題列 + 邊距），沿用原本設計
            float inset = panel.rect.height - gridRect.rect.height;
            if (inset <= 0f) inset = 71.12f;

            float newHeight = inset + visible * RowHeight + (visible - 1) * RowSpacing;

            Undo.RecordObject(panel, "Space Cupola 中文 UI");
            float oldHeight = panel.rect.height;
            float bottom = panel.anchoredPosition.y - oldHeight * panel.pivot.y;   // 原本的底緣

            panel.pivot = new Vector2(panel.pivot.x, 0f);
            panel.sizeDelta = new Vector2(panel.sizeDelta.x, newHeight);
            panel.anchoredPosition = new Vector2(panel.anchoredPosition.x, bottom);
            EditorUtility.SetDirty(panel);

            report.AppendLine($"面板：國家資訊面板高度 {oldHeight:F0} → {newHeight:F0}（{visible} 列可見），底緣固定在 {bottom:F0}");
        }

        /// <summary>在畫面正中央加入十字準心，讓觀眾知道資訊來自哪個位置。</summary>
        internal static void EnsureReticle(Canvas canvas, StringBuilder report)
        {
            if (canvas.transform.Find(ReticleName) != null)
            {
                report.AppendLine("準心：已存在，略過");
                return;
            }

            var root = new GameObject(ReticleName, typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(root, "Space Cupola 中文 UI");
            root.layer = canvas.gameObject.layer;
            var rt = (RectTransform)root.transform;
            rt.SetParent(canvas.transform, false);
            rt.SetAsFirstSibling();   // 置於其他面板之下
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(48f, 48f);

            // 四段短線，中間留空，不遮住正中央的國家
            var bars = new (Vector2 pos, Vector2 size)[]
            {
                (new Vector2(-16f, 0f), new Vector2(14f, 2f)),
                (new Vector2( 16f, 0f), new Vector2(14f, 2f)),
                (new Vector2(0f, -16f), new Vector2(2f, 14f)),
                (new Vector2(0f,  16f), new Vector2(2f, 14f)),
            };
            for (int i = 0; i < bars.Length; i++)
            {
                var bar = new GameObject($"Bar{i}", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                Undo.RegisterCreatedObjectUndo(bar, "Space Cupola 中文 UI");
                bar.layer = root.layer;
                var brt = (RectTransform)bar.transform;
                brt.SetParent(rt, false);
                brt.anchorMin = brt.anchorMax = new Vector2(0.5f, 0.5f);
                brt.pivot = new Vector2(0.5f, 0.5f);
                brt.anchoredPosition = bars[i].pos;
                brt.sizeDelta = bars[i].size;

                var img = bar.GetComponent<Image>();
                img.color = new Color(1f, 1f, 1f, 0.75f);
                img.raycastTarget = false;
            }

            report.AppendLine("準心：已在畫面中央建立 CenterReticle");
        }

        private static string GetPath(Transform t, Transform root)
        {
            var parts = new List<string>();
            for (Transform c = t; c != null && c != root; c = c.parent) parts.Add(c.name);
            parts.Reverse();
            return string.Join("/", parts);
        }
    }
}
#endif
