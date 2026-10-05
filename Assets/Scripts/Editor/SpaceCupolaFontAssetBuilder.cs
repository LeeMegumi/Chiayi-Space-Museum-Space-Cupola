// SpaceCupolaFontAssetBuilder.cs
// 從 Assets/Font 的 LINE Seed TW 字型建立 TMP 動態 SDF 字體資產。
//
// 套用位置：Assets/Scripts/Editor/SpaceCupolaFontAssetBuilder.cs（必須在 Editor 資料夾內）
// 使用方式：選單 Tools > Space Cupola > 1. 建立中文字體資產
//
// 設計：
//   * 動態（Dynamic）圖集：繁中字元數以千計，靜態預烘圖集不實際；執行期缺字會自動補。
//   * 預先放入對照表（Localization_zhTW.json）用到的所有字元，避免展場首次顯示時卡頓。
//   * 取樣 64pt / padding 6 / 2048 圖集 / 允許多圖集 —— 1920×1080 下 20–44pt 的 UI 字已足夠清晰。
//   * 重複執行只會補字，不會重建（既有的 UI 引用不會斷）。
//   * 做法比照 TMP 官方的「Create > TextMeshPro > Font Asset」：
//     用公開 API CreateFontAsset(Font, ...) 建立，再把圖集貼圖與材質存成子資產。

#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace SpaceCupola.EditorTools
{
    public static class SpaceCupolaFontAssetBuilder
    {
        public const string RegularTtfPath   = "Assets/Font/LINESeedTW_TTF_Rg.ttf";
        public const string BoldTtfPath      = "Assets/Font/LINESeedTW_TTF_Bd.ttf";
        public const string RegularAssetPath = "Assets/Font/LINESeedTW_Rg SDF.asset";
        public const string BoldAssetPath    = "Assets/Font/LINESeedTW_Bd SDF.asset";

        // 預載字元來源：國名／UI 對照表、城市資料表
        private static readonly string[] CharacterSourcePaths =
        {
            "Assets/Resources/Localization_zhTW.json",
            "Assets/Resources/CityData_zhTW.json",
        };

        private const int SamplingPointSize = 64;
        private const int AtlasPadding      = 6;
        private const int AtlasSize         = 2048;

        // 對照表以外、UI 可能出現的字元
        private const string ExtraCharacters = "—–％%°·・、，。：；（）()「」『』！？…＋－×/0123456789";

        [MenuItem("Tools/Space Cupola/1. 建立中文字體資產")]
        private static void BuildMenu()
        {
            string characters = CollectCharacters();

            var sb = new StringBuilder();
            sb.AppendLine("=== Space Cupola 中文字體資產 ===");
            sb.AppendLine($"預載字元數：{characters.Length}");

            TMP_FontAsset regular = BuildOrUpdate(RegularTtfPath, RegularAssetPath, characters, sb);
            TMP_FontAsset bold    = BuildOrUpdate(BoldTtfPath,    BoldAssetPath,    characters, sb);

            if (regular != null) RegisterGlobalFallback(regular, sb);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            if (regular != null && bold != null)
            {
                sb.AppendLine();
                sb.AppendLine("完成。下一步：Tools > Space Cupola > 2. 套用中文 UI 與排版");
                Debug.Log(sb.ToString());
                EditorGUIUtility.PingObject(regular);
            }
            else
            {
                Debug.LogError(sb.ToString());
            }
        }

        private static TMP_FontAsset BuildOrUpdate(string ttfPath, string assetPath, string characters, StringBuilder sb)
        {
            Font font = AssetDatabase.LoadAssetAtPath<Font>(ttfPath);
            if (font == null)
            {
                sb.AppendLine($"✗ 找不到字型檔：{ttfPath}");
                return null;
            }

            TMP_FontAsset fontAsset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(assetPath);
            bool created = false;

            if (fontAsset == null)
            {
                fontAsset = TMP_FontAsset.CreateFontAsset(
                    font, SamplingPointSize, AtlasPadding, GlyphRenderMode.SDFAA,
                    AtlasSize, AtlasSize, AtlasPopulationMode.Dynamic, enableMultiAtlasSupport: true);

                if (fontAsset == null)
                {
                    sb.AppendLine($"✗ 無法由 {Path.GetFileName(ttfPath)} 建立字體資產（請確認 Include Font Data 已勾選）");
                    return null;
                }

                fontAsset.name = Path.GetFileNameWithoutExtension(assetPath);
                AssetDatabase.CreateAsset(fontAsset, assetPath);
                created = true;
            }

            PersistSubAssets(fontAsset);

            // LINE Seed TW 沒有重音拉丁字母（Ü ä è ï ô ö ü 全缺，已用 fontTools 檢查字型 cmap），
            // 會影響 Malmö、Ürümqi、Côte d'Ivoire 等 10 個英文名稱。
            // 設 TMP 預設字型（LiberationSans，含上述字母）為備援，缺字時自動改用。
            TMP_FontAsset latinFallback = TMP_Settings.defaultFontAsset;
            if (latinFallback != null && latinFallback != fontAsset)
            {
                if (fontAsset.fallbackFontAssetTable == null)
                    fontAsset.fallbackFontAssetTable = new List<TMP_FontAsset>();
                if (!fontAsset.fallbackFontAssetTable.Contains(latinFallback))
                {
                    fontAsset.fallbackFontAssetTable.Add(latinFallback);
                    sb.AppendLine($"    已加入拉丁字母備援：{latinFallback.name}");
                }
            }

            fontAsset.TryAddCharacters(characters, out string missing);

            // 補字可能擴充出新的圖集貼圖，一併存成子資產
            PersistSubAssets(fontAsset);
            EditorUtility.SetDirty(fontAsset);

            int missingCount = string.IsNullOrEmpty(missing) ? 0 : missing.Length;
            int atlasCount = fontAsset.atlasTextures != null ? fontAsset.atlasTextures.Length : 0;
            sb.AppendLine($"{(created ? "✓ 已建立" : "✓ 已更新")}：{assetPath}  " +
                          $"（字元 {fontAsset.characterTable.Count}、圖集 {atlasCount} 張、字型內缺字 {missingCount}）");
            if (missingCount > 0)
                sb.AppendLine($"    此字型沒有、改由備援字型顯示的字元：{missing}");

            return fontAsset;
        }

        /// <summary>
        /// 加入 TMP 全域備援字型。仍使用 LiberationSans 的文字（例如地球上的城市標籤 CityLabel.prefab）
        /// 遇到中文字時會自動改用 LINE Seed TW，不必逐一修改 prefab。
        /// </summary>
        private static void RegisterGlobalFallback(TMP_FontAsset fontAsset, StringBuilder sb)
        {
            TMP_Settings settings = TMP_Settings.instance;
            if (settings == null)
            {
                sb.AppendLine("✗ 找不到 TMP Settings，未加入全域備援字型");
                return;
            }

            var so = new SerializedObject(settings);
            SerializedProperty list = so.FindProperty("m_fallbackFontAssets");
            if (list == null)
            {
                sb.AppendLine("✗ TMP Settings 中找不到 m_fallbackFontAssets 欄位");
                return;
            }

            for (int i = 0; i < list.arraySize; i++)
            {
                if (list.GetArrayElementAtIndex(i).objectReferenceValue == fontAsset)
                {
                    sb.AppendLine("✓ 全域備援字型：已包含 LINE Seed TW，略過");
                    return;
                }
            }

            list.arraySize++;
            list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = fontAsset;
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(settings);
            sb.AppendLine("✓ 全域備援字型：已加入 LINE Seed TW（城市標籤等仍用舊字型的文字可顯示中文）");
        }

        /// <summary>把尚未存檔的圖集貼圖與材質加成字體資產的子資產。</summary>
        private static void PersistSubAssets(TMP_FontAsset fontAsset)
        {
            if (fontAsset.atlasTextures != null)
            {
                for (int i = 0; i < fontAsset.atlasTextures.Length; i++)
                {
                    Texture2D tex = fontAsset.atlasTextures[i];
                    if (tex == null || EditorUtility.IsPersistent(tex)) continue;
                    tex.name = fontAsset.name + (i == 0 ? " Atlas" : $" Atlas {i}");
                    AssetDatabase.AddObjectToAsset(tex, fontAsset);
                }
            }

            Material mat = fontAsset.material;
            if (mat != null && !EditorUtility.IsPersistent(mat))
            {
                mat.name = fontAsset.name + " Material";
                AssetDatabase.AddObjectToAsset(mat, fontAsset);
            }
        }

        /// <summary>收集對照表中出現的所有字元 + 可列印 ASCII + 常用標點。</summary>
        private static string CollectCharacters()
        {
            var set = new HashSet<char>();

            for (char c = (char)32; c <= (char)126; c++) set.Add(c);
            foreach (char c in ExtraCharacters) set.Add(c);
            set.Add('ô');   // Côte d'Ivoire

            foreach (string path in CharacterSourcePaths)
            {
                TextAsset json = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
                if (json == null)
                {
                    Debug.LogWarning($"[SpaceCupolaFontAssetBuilder] 找不到 {path}，略過該來源的預載字元。");
                    continue;
                }
                foreach (char c in json.text)
                    if (!char.IsControl(c)) set.Add(c);
            }

            var list = new List<char>(set);
            list.Sort();
            return new string(list.ToArray());
        }
    }
}
#endif
