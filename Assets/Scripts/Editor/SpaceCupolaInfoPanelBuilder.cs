// SpaceCupolaInfoPanelBuilder.cs
// 建立整合資訊面板（取代原本左下城市面板與右下國家面板）。
//
// 套用位置：Assets/Scripts/Editor/SpaceCupolaInfoPanelBuilder.cs（必須在 Editor 資料夾內）
// 使用方式：Tools > Space Cupola > 3. 建立整合資訊面板
//           （需先執行 1. 建立中文字體資產）
//
// 版面（1920×1080 參考解析度，左下角）：
//   ┌──────────────────────────────┐
//   │ 最近城市 NEAREST CITY         │ ← 小標（依內容切換：城市／國家／海域）
//   │ Tokyo                         │ ← 標題（城市名；無城市時為國家）
//   │ ────────                      │ ← 極光色細線
//   │ 國家 Country         日本     │
//   │ 座標 Coordinates  35.7°N 139.7°E │
//   │ 氣候帶 Climate        溫帶     │
//   │ 人口 Population  13,960,000   │
//   └──────────────────────────────┘
//
// 以 VerticalLayoutGroup + ContentSizeFitter 自動排列與決定高度，
// 日後增減列不需要重算座標。
// 所有建立與停用動作記錄在同一個 Undo 群組，一次 Ctrl+Z 可還原。

#if UNITY_EDITOR
using System.Text;
using SpaceCupola.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace SpaceCupola.EditorTools
{
    public static class SpaceCupolaInfoPanelBuilder
    {
        private const string PanelName = "LocationInfoPanel";
        private const string UndoName  = "Space Cupola 整合資訊面板";

        // ── 版面 ──
        private const float PanelWidth   = 460f;
        private const float MarginX      = 48f;
        private const float MarginY      = 48f;
        private const float RowHeight    = 40f;
        private const float LabelRatio   = 0.42f;

        // ── 配色（深藍半透明底 + 極光綠點綴）──
        private static readonly Color BackgroundColor = new Color(0.04f, 0.07f, 0.13f, 0.82f);
        private static readonly Color AccentColor     = new Color(0.45f, 1.00f, 0.80f, 1.00f);
        private static readonly Color CaptionColor    = new Color(0.45f, 1.00f, 0.80f, 0.90f);
        private static readonly Color TitleColor      = Color.white;
        private static readonly Color LabelColor      = new Color(0.70f, 0.78f, 0.88f, 1.00f);
        private static readonly Color ValueColor      = Color.white;
        private static readonly Color DividerColor    = new Color(1f, 1f, 1f, 0.10f);

        [MenuItem("Tools/Space Cupola/3. 建立整合資訊面板")]
        private static void Build()
        {
            TMP_FontAsset regular = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(SpaceCupolaFontAssetBuilder.RegularAssetPath);
            TMP_FontAsset bold    = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(SpaceCupolaFontAssetBuilder.BoldAssetPath);
            if (regular == null || bold == null)
            {
                EditorUtility.DisplayDialog("Space Cupola",
                    "找不到中文字體資產。\n請先執行 Tools > Space Cupola > 1. 建立中文字體資產。", "OK");
                return;
            }

            WindowLayerInfo oldCountryPanel = Object.FindFirstObjectByType<WindowLayerInfo>(FindObjectsInactive.Include);
            Canvas canvas = oldCountryPanel != null ? oldCountryPanel.GetComponentInParent<Canvas>(true) : null;
            if (canvas == null) canvas = Object.FindFirstObjectByType<Canvas>(FindObjectsInactive.Include);
            if (canvas == null)
            {
                EditorUtility.DisplayDialog("Space Cupola", "場景中找不到 Canvas。", "OK");
                return;
            }
            canvas = canvas.rootCanvas;

            Transform existing = canvas.transform.Find(PanelName);
            string msg = existing != null
                ? "整合資訊面板已存在，將刪除後重新建立。\n\n"
                : "";
            msg += "• 在左下角建立整合資訊面板（城市／國家／座標／氣候帶／人口）\n" +
                   "• 停用原本的右下國家面板（GeoPoliticData）與左下城市面板（CitySelected Data）\n" +
                   "• 確保畫面中央有準心\n\n" +
                   "舊面板只是停用、不會刪除；全部可用 Ctrl+Z 還原。";
            if (!EditorUtility.DisplayDialog("Space Cupola", msg, "建立", "取消")) return;

            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(UndoName);

            var report = new StringBuilder();
            report.AppendLine("=== Space Cupola 整合資訊面板 ===");

            if (existing != null)
            {
                Undo.DestroyObjectImmediate(existing.gameObject);
                report.AppendLine("已刪除舊的整合資訊面板");
            }

            LocationInfoPanel panel = CreatePanel(canvas, regular, bold);
            report.AppendLine($"已建立：{canvas.name}/{PanelName}（左下角，寬 {PanelWidth}，高度自動）");

            DeactivateOldPanels(canvas, oldCountryPanel, report);
            SpaceCupolaUiLocalizer.EnsureReticle(canvas, report);

            Undo.CollapseUndoOperations(group);
            EditorSceneManager.MarkSceneDirty(canvas.gameObject.scene);
            Selection.activeGameObject = panel.gameObject;

            report.AppendLine();
            report.AppendLine("完成。按 Play 後用滑鼠右鍵拖曳轉動地球，確認面板內容隨準心更新；滿意再存檔。");
            Debug.Log(report.ToString(), panel);
        }

        // ─────────────────────────────────────────────────────────────

        private static LocationInfoPanel CreatePanel(Canvas canvas, TMP_FontAsset regular, TMP_FontAsset bold)
        {
            int layer = canvas.gameObject.layer;

            // ── 根物件 ──
            var root = new GameObject(PanelName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image),
                                      typeof(CanvasGroup), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            Undo.RegisterCreatedObjectUndo(root, UndoName);
            root.layer = layer;

            var rt = (RectTransform)root.transform;
            rt.SetParent(canvas.transform, false);
            rt.anchorMin = rt.anchorMax = Vector2.zero;       // 左下角
            rt.pivot = Vector2.zero;
            rt.anchoredPosition = new Vector2(MarginX, MarginY);
            rt.sizeDelta = new Vector2(PanelWidth, 300f);     // 高度會由 ContentSizeFitter 決定

            var bg = root.GetComponent<Image>();
            bg.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Background.psd");   // 內建圓角底圖
            bg.type = Image.Type.Sliced;
            bg.color = BackgroundColor;
            bg.raycastTarget = false;

            var cg = root.GetComponent<CanvasGroup>();
            cg.interactable = false;
            cg.blocksRaycasts = false;

            var vlg = root.GetComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(24, 24, 18, 18);
            vlg.spacing = 0f;
            vlg.childAlignment = TextAnchor.UpperLeft;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            var fitter = root.GetComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;

            // ── 小標、標題、細線 ──
            TMP_Text caption = CreateText(rt, "Caption", regular, 17f, CaptionColor, HorizontalAlignmentOptions.Left);
            caption.text = "目前位置  <size=70%>LOCATION</size>";
            AddLayout(caption.gameObject, 24f);

            TMP_Text title = CreateText(rt, "Title", bold, 40f, TitleColor, HorizontalAlignmentOptions.Left, minSize: 22f);
            title.text = "—";
            AddLayout(title.gameObject, 54f);

            AddSpacer(rt, "Spacer", 6f, layer);
            CreateImage(rt, "AccentLine", AccentColor, 2f, layer, widthFraction: 0.18f);
            AddSpacer(rt, "Spacer", 8f, layer);

            // ── 四列（依使用者指定順序）──
            TMP_Text country = CreateRow(rt, "Row_Country",     "國家",   "Country",     regular, divider: true);
            TMP_Text coords  = CreateRow(rt, "Row_Coordinates", "座標",   "Coordinates", regular, divider: true);
            TMP_Text climate = CreateRow(rt, "Row_Climate",     "氣候帶", "Climate",     regular, divider: true);
            TMP_Text pop     = CreateRow(rt, "Row_Population",  "人口",   "Population",  regular, divider: false);

            // ── 資料腳本 ──
            var panel = root.AddComponent<LocationInfoPanel>();
            var so = new SerializedObject(panel);
            so.FindProperty("caption").objectReferenceValue          = caption;
            so.FindProperty("title").objectReferenceValue            = title;
            so.FindProperty("countryValue").objectReferenceValue     = country;
            so.FindProperty("coordinatesValue").objectReferenceValue = coords;
            so.FindProperty("climateValue").objectReferenceValue     = climate;
            so.FindProperty("populationValue").objectReferenceValue  = pop;
            so.ApplyModifiedPropertiesWithoutUndo();   // 物件本身已在 Undo 中建立

            LayoutRebuilder.ForceRebuildLayoutImmediate(rt);
            return panel;
        }

        private static TMP_Text CreateRow(RectTransform parent, string name, string zh, string en,
                                          TMP_FontAsset font, bool divider)
        {
            int layer = parent.gameObject.layer;
            var row = new GameObject(name, typeof(RectTransform));
            row.layer = layer;
            var rrt = (RectTransform)row.transform;
            rrt.SetParent(parent, false);
            AddLayout(row, RowHeight);

            TMP_Text label = CreateText(rrt, "Label", font, 21f, LabelColor, HorizontalAlignmentOptions.Left, minSize: 14f);
            label.text = $"{zh}  <size=68%><alpha=#99>{en}</size>";
            Stretch(label.rectTransform, 0f, LabelRatio);

            TMP_Text value = CreateText(rrt, "Value", font, 23f, ValueColor, HorizontalAlignmentOptions.Right, minSize: 14f);
            value.text = "—";
            Stretch(value.rectTransform, LabelRatio, 1f);

            if (divider)
            {
                var line = new GameObject("Divider", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                line.layer = layer;
                var lrt = (RectTransform)line.transform;
                lrt.SetParent(rrt, false);
                lrt.anchorMin = new Vector2(0f, 0f);
                lrt.anchorMax = new Vector2(1f, 0f);
                lrt.pivot = new Vector2(0.5f, 0f);
                lrt.sizeDelta = new Vector2(0f, 1f);
                lrt.anchoredPosition = Vector2.zero;
                var img = line.GetComponent<Image>();
                img.color = DividerColor;
                img.raycastTarget = false;
            }
            return value;
        }

        private static TMP_Text CreateText(RectTransform parent, string name, TMP_FontAsset font, float size,
                                           Color color, HorizontalAlignmentOptions align, float minSize = -1f)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);

            var t = go.AddComponent<TextMeshProUGUI>();
            t.font = font;
            t.fontSharedMaterial = font.material;
            t.fontSize = size;
            t.color = color;
            t.horizontalAlignment = align;
            t.verticalAlignment = VerticalAlignmentOptions.Middle;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Ellipsis;
            t.richText = true;
            t.raycastTarget = false;
            if (minSize > 0f)
            {
                t.enableAutoSizing = true;
                t.fontSizeMax = size;
                t.fontSizeMin = minSize;
            }
            return t;
        }

        private static void CreateImage(RectTransform parent, string name, Color color, float height, int layer,
                                        float widthFraction)
        {
            // 外層吃版面高度，內層只佔左側一段寬度 —— 做出短短一截的點綴線
            var holder = new GameObject(name, typeof(RectTransform));
            holder.layer = layer;
            holder.transform.SetParent(parent, false);
            AddLayout(holder, height);

            var line = new GameObject("Line", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            line.layer = layer;
            var lrt = (RectTransform)line.transform;
            lrt.SetParent(holder.transform, false);
            lrt.anchorMin = new Vector2(0f, 0f);
            lrt.anchorMax = new Vector2(widthFraction, 1f);
            lrt.offsetMin = lrt.offsetMax = Vector2.zero;
            var img = line.GetComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
        }

        private static void AddSpacer(RectTransform parent, string name, float height, int layer)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = layer;
            go.transform.SetParent(parent, false);
            AddLayout(go, height);
        }

        private static void AddLayout(GameObject go, float height)
        {
            var le = go.GetComponent<LayoutElement>();
            if (le == null) le = go.AddComponent<LayoutElement>();
            le.minHeight = height;
            le.preferredHeight = height;
        }

        private static void Stretch(RectTransform rt, float xMin, float xMax)
        {
            rt.anchorMin = new Vector2(xMin, 0f);
            rt.anchorMax = new Vector2(xMax, 1f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        private static void DeactivateOldPanels(Canvas canvas, WindowLayerInfo countryPanel, StringBuilder report)
        {
            if (countryPanel != null && countryPanel.gameObject.activeSelf)
            {
                Undo.RecordObject(countryPanel.gameObject, UndoName);
                countryPanel.gameObject.SetActive(false);
                report.AppendLine($"已停用舊國家面板：{countryPanel.name}");
            }

            foreach (Transform t in canvas.GetComponentsInChildren<Transform>(true))
            {
                if (t.name != "CitySelected Data" || !t.gameObject.activeSelf) continue;
                Undo.RecordObject(t.gameObject, UndoName);
                t.gameObject.SetActive(false);
                report.AppendLine("已停用舊城市面板：CitySelected Data");
            }
        }
    }
}
#endif
