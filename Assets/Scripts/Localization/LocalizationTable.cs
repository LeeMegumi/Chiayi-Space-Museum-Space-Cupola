// LocalizationTable.cs
// Space Cupola 繁體中文查表器。
//
// 套用位置（需同意後才建立）：
//   Assets/Scripts/Localization/LocalizationTable.cs
// 資料檔：
//   Assets/Resources/Localization_zhTW.json
//
// 設計理由：
//   * 不改 Canvas.prefab 與場景的文字內容 —— 譯名集中在一個 JSON，可單獨維護、可回退。
//   * 使用 Unity 內建 JsonUtility，不引入 Newtonsoft（避免改動 Packages/manifest.json）。
//     JsonUtility 無法反序列化 Dictionary，因此 JSON 採 [{k,v}, ...] 陣列格式，載入後轉字典。
//   * 靜態延遲載入 —— 任何腳本可直接呼叫，不需要在場景中擺放物件或拉引用。

using System.Collections.Generic;
using UnityEngine;

namespace SpaceCupola.Localization
{
    /// <summary>
    /// 繁體中文查表器。首次呼叫時自動從 Resources 載入對照表。
    /// </summary>
    public static class LocalizationTable
    {
        /// <summary>Resources 下的檔名（不含 .json 副檔名）。</summary>
        private const string ResourcePath = "Localization_zhTW";

        /// <summary>雙語顯示時，英文副標的相對字級（TMP rich text 百分比）。</summary>
        private const int SecondaryFontSizePercent = 55;

        #region JSON 對應結構

        [System.Serializable]
        private struct Pair
        {
            public string k;
            public string v;
        }

        [System.Serializable]
        private class TableData
        {
            public string locale;
            public Pair[] countries;
            public Pair[] countriesSpare;
            public string[] oceanKeys;
            public Pair[] climateZones;
            public Pair[] religions;
            public Pair[] uiLabels;
            public Pair[] englishDisplay;   // 雙語顯示用的英文副標（取代生硬的 ISO 正式名）
        }

        #endregion

        private static bool _loaded;
        private static Dictionary<string, string> _countries;
        private static Dictionary<string, string> _climateZones;
        private static Dictionary<string, string> _religions;
        private static Dictionary<string, string> _uiLabels;
        private static Dictionary<string, string> _englishDisplay;
        private static HashSet<string> _oceans;

        /// <summary>對照表是否成功載入。載入失敗時所有查詢會原樣回傳英文。</summary>
        public static bool IsLoaded
        {
            get { EnsureLoaded(); return _countries != null; }
        }

        private static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;

            TextAsset asset = Resources.Load<TextAsset>(ResourcePath);
            if (asset == null)
            {
                Debug.LogWarning($"[LocalizationTable] 找不到 Resources/{ResourcePath}.json，" +
                                 "所有文字將維持英文。");
                return;
            }

            TableData data;
            try
            {
                data = JsonUtility.FromJson<TableData>(asset.text);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[LocalizationTable] 對照表解析失敗：{e.Message}");
                return;
            }

            if (data == null)
            {
                Debug.LogError("[LocalizationTable] 對照表內容為空或格式不符。");
                return;
            }

            _countries    = ToDictionary(data.countries, data.countriesSpare);
            _climateZones = ToDictionary(data.climateZones);
            _religions    = ToDictionary(data.religions);
            _uiLabels     = ToDictionary(data.uiLabels);
            _englishDisplay = ToDictionary(data.englishDisplay);

            _oceans = new HashSet<string>();
            if (data.oceanKeys != null)
                foreach (string key in data.oceanKeys)
                    if (!string.IsNullOrEmpty(key)) _oceans.Add(key);

            Debug.Log($"[LocalizationTable] 已載入 {data.locale}：" +
                      $"{_countries.Count} 國名、{_climateZones.Count} 氣候帶、" +
                      $"{_religions.Count} 宗教、{_uiLabels.Count} UI 標籤。");
        }

        private static Dictionary<string, string> ToDictionary(params Pair[][] sources)
        {
            var dict = new Dictionary<string, string>();
            foreach (Pair[] source in sources)
            {
                if (source == null) continue;
                foreach (Pair p in source)
                {
                    if (string.IsNullOrEmpty(p.k)) continue;
                    dict[p.k] = p.v;                 // 後者覆寫前者
                }
            }
            return dict;
        }

        #region 查詢 API

        /// <summary>國名／海洋名譯名。查不到時原樣回傳輸入值。</summary>
        public static string Country(string english) => Lookup(_countries, english);

        /// <summary>氣候帶譯名。</summary>
        public static string ClimateZone(string english) => Lookup(_climateZones, english);

        /// <summary>宗教譯名。</summary>
        public static string Religion(string english) => Lookup(_religions, english);

        /// <summary>靜態 UI 標籤譯名。</summary>
        public static string UiLabel(string english) => Lookup(_uiLabels, english);

        /// <summary>
        /// 顯示用的英文國名。資料中的 key 是 ISO 正式名（例如 "Korea, Republic of"、
        /// 字面帶跳脫字元的 "Côte d'Ivoire"），直接顯示很生硬或會出現亂碼；
        /// 有對應時改用常用名，沒有則原樣回傳。
        /// </summary>
        public static string EnglishDisplay(string english) => Lookup(_englishDisplay, english);

        /// <summary>此名稱是否為海洋（而非國家）。</summary>
        public static bool IsOcean(string english)
        {
            EnsureLoaded();
            return _oceans != null && !string.IsNullOrEmpty(english) && _oceans.Contains(english);
        }

        private static string Lookup(Dictionary<string, string> dict, string english)
        {
            EnsureLoaded();
            if (string.IsNullOrEmpty(english)) return english;
            if (dict != null && dict.TryGetValue(english, out string zh) && !string.IsNullOrEmpty(zh))
                return zh;
            return english;
        }

        #endregion

        #region 雙語組字

        /// <summary>
        /// 組成「中文主、英文副」的 TMP 字串，例如：日本<size=55%> Japan</size>
        /// 若查不到譯名（中英相同）則只回傳原字串，不會出現重複。
        /// </summary>
        public static string Bilingual(string english)
        {
            string zh = Country(english);
            return ComposeBilingual(zh, EnglishDisplay(english));
        }

        /// <summary>雙語組字的通用版本，可搭配任何一組中英文。</summary>
        public static string ComposeBilingual(string primary, string secondary)
        {
            if (string.IsNullOrEmpty(primary)) return secondary;
            if (string.IsNullOrEmpty(secondary) || primary == secondary) return primary;
            return $"{primary}<size={SecondaryFontSizePercent}%> {secondary}</size>";
        }

        #endregion

        /// <summary>
        /// 強制重新載入對照表。編輯 JSON 後不必重啟 Unity 即可套用。
        /// </summary>
        public static void Reload()
        {
            _loaded = false;
            _countries = null;
            _climateZones = null;
            _religions = null;
            _uiLabels = null;
            _englishDisplay = null;
            _oceans = null;
            EnsureLoaded();
        }
    }
}
