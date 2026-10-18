// SpaceCupolaShadowFix.cs
// 修正放大縮小時地球表面出現的階梯狀鋸齒黑邊（陰影破圖）。
//
// 套用位置：Assets/Scripts/Editor/SpaceCupolaShadowFix.cs（必須在 Editor 資料夾內）
// 使用方式：Tools > Space Cupola > 5. 關閉方向光陰影（修正地球破圖）
//
// ── 原因（展場錄影 + 專案設定交叉確認）────────────────────────────
//   * 場景的 Directional Light 開啟柔和陰影（Soft Shadows）
//   * 地球本體、173 個國家網格、5 個海洋網格全部「投射陰影」
//   * URP（PC_RPAsset）陰影距離 50、4 層 cascade、2048 解析度
// 結果：
//   1. 地球自己遮自己 → 日夜交界多了一條由陰影貼圖畫出的鋸齒邊（本應由 shader 平滑計算）
//   2. 國家網格略高於球面 → 在海岸線外投下一圈錯位的黑邊
//   3. 縮放時切換到不同 cascade 層 → 鋸齒形狀跟著跳動（錄影中拉遠時就消失）
// 夜晚面的城市燈光由 Earth shader 自行計算，不依賴陰影；場景中也沒有其他需要陰影的物件。
// 因此直接關閉方向光的陰影即可，對畫面沒有其他影響，還能省下陰影運算。
//
// 記錄在 Undo 中，可用 Ctrl+Z 還原。

#if UNITY_EDITOR
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SpaceCupola.EditorTools
{
    public static class SpaceCupolaShadowFix
    {
        [MenuItem("Tools/Space Cupola/5. 關閉方向光陰影（修正地球破圖）")]
        private static void Apply()
        {
            Light[] lights = Object.FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            var sb = new StringBuilder();
            int changed = 0;

            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Space Cupola 關閉方向光陰影");

            foreach (Light l in lights)
            {
                if (l.type != LightType.Directional) continue;
                if (l.shadows == LightShadows.None)
                {
                    sb.AppendLine($"  {l.name}：原本就沒有陰影，略過");
                    continue;
                }
                Undo.RecordObject(l, "Space Cupola 關閉方向光陰影");
                sb.AppendLine($"  {l.name}：{l.shadows} → None");
                l.shadows = LightShadows.None;
                EditorUtility.SetDirty(l);
                EditorSceneManager.MarkSceneDirty(l.gameObject.scene);
                changed++;
            }

            Undo.CollapseUndoOperations(group);

            string report = $"[SpaceCupolaShadowFix] 已關閉 {changed} 盞方向光的陰影：\n{sb}" +
                            "請存檔場景後重新輸出。可用 Ctrl+Z 還原。";
            Debug.Log(report);
            EditorUtility.DisplayDialog("Space Cupola",
                changed > 0
                    ? $"已關閉 {changed} 盞方向光的陰影。\n請存檔場景（Ctrl+S）後重新輸出。\n\n可用 Ctrl+Z 還原。"
                    : "場景中的方向光原本就沒有陰影。",
                "OK");
        }
    }
}
#endif
