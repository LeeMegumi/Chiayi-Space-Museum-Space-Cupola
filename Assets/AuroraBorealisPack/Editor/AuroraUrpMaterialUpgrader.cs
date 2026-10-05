// AuroraUrpMaterialUpgrader.cs
// 一次性材質轉換工具：把 AuroraBorealisPack 的舊材質切到 URP 版 shader。
//
// 套用位置（需使用者同意後才建立）：
//   Assets/AuroraBorealisPack/Editor/AuroraUrpMaterialUpgrader.cs
//   ※ 必須放在名為 Editor 的資料夾內，否則會導致 build 失敗。
//
// 用途：
//   原始 shader 的 Property 宣告為 _Falloff，但 fragment 實際採樣的是 _Fallof（typo）。
//   兩者在既有材質中指向不同貼圖，因此單純換 shader 會讓外觀改變。
//   本工具把材質序列化資料中的 _Fallof 貼圖搬到 _Falloff，確保換完 shader 後
//   渲染結果與原本「實際看到的」畫面一致。
//
// 使用方式：Unity 選單 Tools > Aurora Borealis > Upgrade Materials to URP
// 執行前建議先 commit 或備份，轉換會直接寫入 .mat 檔。

#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace AuroraBorealisPack.EditorTools
{
    public static class AuroraUrpMaterialUpgrader
    {
        private const string LegacyShaderName = "AuroraBorealis_Pack/AuroraBorealis";
        private const string UrpShaderName    = "AuroraBorealis_Pack/AuroraBorealis (URP)";

        private const string LegacyFalloffProp = "_Fallof";   // 舊 shader 實際採樣的名稱
        private const string FalloffProp       = "_Falloff";  // URP 版統一使用的名稱

        [MenuItem("Tools/Aurora Borealis/Upgrade Materials to URP")]
        private static void UpgradeMaterials()
        {
            Shader urpShader = Shader.Find(UrpShaderName);
            if (urpShader == null)
            {
                EditorUtility.DisplayDialog(
                    "Aurora URP Upgrader",
                    $"找不到 shader「{UrpShaderName}」。\n請先確認 AuroraBorealis_URP.shader 已匯入專案。",
                    "OK");
                return;
            }

            List<Material> targets = new List<Material>();
            foreach (string guid in AssetDatabase.FindAssets("t:Material"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat != null && mat.shader != null && mat.shader.name == LegacyShaderName)
                    targets.Add(mat);
            }

            if (targets.Count == 0)
            {
                EditorUtility.DisplayDialog(
                    "Aurora URP Upgrader",
                    $"沒有找到使用「{LegacyShaderName}」的材質，不需要轉換。",
                    "OK");
                return;
            }

            string list = string.Join("\n", targets.ConvertAll(m => "  • " + m.name));
            bool proceed = EditorUtility.DisplayDialog(
                "Aurora URP Upgrader",
                $"將轉換以下 {targets.Count} 個材質到 URP shader：\n\n{list}\n\n" +
                "同時會把 _Fallof 的貼圖搬到 _Falloff（修正原始 shader 的 typo）。\n" +
                "此操作會直接寫入 .mat 檔，建議先 commit 或備份。",
                "開始轉換", "取消");

            if (!proceed) return;

            int migratedFalloff = 0;

            foreach (Material mat in targets)
            {
                // 換 shader 前先從序列化資料讀出 _Fallof —— 它不在舊 shader 的
                // Properties 區塊中，Material.GetTexture 讀不到，只能走 SerializedObject。
                Texture legacyTex = ReadSerializedTexture(mat, LegacyFalloffProp);

                Undo.RecordObject(mat, "Upgrade Aurora Material to URP");
                mat.shader = urpShader;

                if (legacyTex != null && mat.HasProperty(FalloffProp))
                {
                    mat.SetTexture(FalloffProp, legacyTex);
                    migratedFalloff++;
                }

                EditorUtility.SetDirty(mat);
            }

            AssetDatabase.SaveAssets();

            Debug.Log($"[Aurora URP Upgrader] 已轉換 {targets.Count} 個材質，" +
                      $"其中 {migratedFalloff} 個搬移了 {LegacyFalloffProp} → {FalloffProp}。");
        }

        /// <summary>
        /// 從材質的序列化資料直接讀取貼圖欄位，可取得不在 shader Properties 中的殘留欄位。
        /// </summary>
        private static Texture ReadSerializedTexture(Material mat, string propertyName)
        {
            SerializedObject so = new SerializedObject(mat);
            SerializedProperty texEnvs =
                so.FindProperty("m_SavedProperties.m_TexEnvs");

            if (texEnvs == null || !texEnvs.isArray) return null;

            for (int i = 0; i < texEnvs.arraySize; i++)
            {
                SerializedProperty entry = texEnvs.GetArrayElementAtIndex(i);
                SerializedProperty key   = entry.FindPropertyRelative("first");
                if (key == null || key.stringValue != propertyName) continue;

                SerializedProperty texProp =
                    entry.FindPropertyRelative("second.m_Texture");
                return texProp != null ? texProp.objectReferenceValue as Texture : null;
            }

            return null;
        }
    }
}
#endif
