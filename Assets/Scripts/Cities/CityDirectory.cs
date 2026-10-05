// CityDirectory.cs
// 執行期以 Resources/CityData_zhTW.json 覆寫場景中的城市資料。
//
// 套用位置：Assets/Scripts/Cities/CityDirectory.cs
// 呼叫時機：CityManager.Awake()（CityDirectory.Apply(Citys, CityTransformRoot)）
//
// ── 為什麼需要覆寫 ────────────────────────────────────────────────
// 原套件 CityManager.SetCityes() 讀的是 CSV 的 region_en（行政區）欄，不是 city_en，
// 且同一行政區只保留 CSV 中的第一筆 —— 結果是「名稱是行政區、人口與座標是該區隨機一個城市」：
//   Tokyo → 人口 143,758（其實是浦安）、California → 73,812（其實是 Alameda）
// 1864 筆中有 1413 筆名稱與資料不一致。另有 CSV 標題列被當成城市讀入（名為 region_en）。
//
// ── 覆寫內容（由 _Claude/scripts/build_city_data.py 產生）──────────
//   regions：每個行政區改用人口最多的城市，名稱／人口／座標一致，附繁中譯名
//   extras ：新增城市點 —— 台灣其餘 13 城與嘉義
//   removeRegions：移除錯誤資料點（region_en）
//
// 原始 prefab 與場景資料完全不動；只在 Play 時改寫記憶體中的 City 元件。
//
// ── 座標換算（1862 城反推驗證，見 LocationInfoPanel.cs）─────────────
// City 的父物件 CityTransformRoot 在 UnitEarth 下為零旋轉，本地空間中：
//   x = −R·cosφ·sin(λ − 0.52°)   y = −R·cosφ·cos(λ − 0.52°)   z = R·sinφ    （R = 0.25）

using System;
using System.Collections.Generic;
using SpaceCupola.Localization;
using UnityEngine;

namespace SpaceCupola.Cities
{
    [Serializable]
    public class CityEntry
    {
        public string region;      // 場景中 City 原本的名稱（行政區）
        public string en;          // CSV 的城市原名
        public string enDisplay;   // 顯示用英文名（例如 City of London → London）
        public string zh;          // 繁中譯名
        public string countryEn;
        public string countryZh;
        public float  lat;
        public float  lon;
        public int    pop;
    }

    public static class CityDirectory
    {
        private const string ResourcePath   = "CityData_zhTW";
        private const float  EarthRadius    = 0.25f;
        private const float  LonOffsetDeg   = 0.52f;

        [Serializable]
        private class DataFile
        {
            public CityEntry[] regions;
            public CityEntry[] extras;
            public string[]    removeRegions;
        }

        private static readonly Dictionary<City, CityEntry> ByCity = new Dictionary<City, CityEntry>();

        /// <summary>取得城市對應的資料（含中文名、國名）。</summary>
        public static bool TryGet(City city, out CityEntry entry)
        {
            entry = null;
            return city != null && ByCity.TryGetValue(city, out entry);
        }

        /// <summary>地球上城市標籤要顯示的文字：有中文用中文，否則沿用原名。</summary>
        public static string LabelText(City city)
        {
            if (TryGet(city, out CityEntry e) && !string.IsNullOrEmpty(e.zh)) return e.zh;
            return city != null ? city.Name : string.Empty;
        }

        /// <summary>面板標題：「中文 English」雙語。</summary>
        public static string TitleText(City city)
        {
            if (TryGet(city, out CityEntry e))
                return LocalizationTable.ComposeBilingual(e.zh, e.enDisplay);
            return city != null ? city.Name : string.Empty;
        }

        /// <summary>面板國家欄：城市資料中的國名（台灣城市為「台灣」）。</summary>
        public static string CountryText(City city)
        {
            return TryGet(city, out CityEntry e) ? e.countryZh : null;
        }

        /// <summary>
        /// 覆寫城市資料。由 CityManager.Awake 在城市清單就緒後呼叫一次。
        /// </summary>
        public static void Apply(List<City> cities, Transform cityRoot)
        {
            ByCity.Clear();
            if (cities == null) return;

            TextAsset asset = Resources.Load<TextAsset>(ResourcePath);
            if (asset == null)
            {
                Debug.LogWarning($"[CityDirectory] 找不到 Resources/{ResourcePath}.json，城市維持原始資料。");
                return;
            }

            DataFile data;
            try { data = JsonUtility.FromJson<DataFile>(asset.text); }
            catch (Exception ex)
            {
                Debug.LogError($"[CityDirectory] 城市資料解析失敗：{ex.Message}");
                return;
            }
            if (data == null) return;

            var byRegion = new Dictionary<string, CityEntry>();
            if (data.regions != null)
                foreach (CityEntry e in data.regions)
                    if (e != null && !string.IsNullOrEmpty(e.region)) byRegion[e.region] = e;

            var remove = new HashSet<string>(data.removeRegions ?? Array.Empty<string>());

            int replaced = 0, removed = 0, added = 0;

            // 由後往前，移除項目時不影響索引
            for (int i = cities.Count - 1; i >= 0; i--)
            {
                City c = cities[i];
                if (c == null) continue;
                if (cityRoot == null) cityRoot = c.transform.parent;

                if (remove.Contains(c.Name))
                {
                    c.gameObject.SetActive(false);
                    cities.RemoveAt(i);
                    removed++;
                    continue;
                }

                if (byRegion.TryGetValue(c.Name, out CityEntry entry))
                {
                    ApplyEntry(c, entry);
                    replaced++;
                }
            }

            if (data.extras != null && cityRoot != null)
            {
                foreach (CityEntry e in data.extras)
                {
                    if (e == null) continue;
                    var go = new GameObject("City_" + e.en);
                    go.transform.SetParent(cityRoot, false);
                    City c = go.AddComponent<City>();
                    ApplyEntry(c, e);
                    cities.Add(c);
                    added++;
                }
            }

            Debug.Log($"[CityDirectory] 城市資料已套用：覆寫 {replaced}、新增 {added}、移除 {removed}（共 {cities.Count} 城）。");
        }

        private static void ApplyEntry(City c, CityEntry e)
        {
            c.Name = string.IsNullOrEmpty(e.enDisplay) ? e.en : e.enDisplay;
            c.gameObject.name = c.Name;
            c.Coordinates = new Vector2(e.lat, e.lon);
            c.PopulationCount = e.pop;
            c.transform.localPosition = LocalPositionFromLatLon(e.lat, e.lon);
            ByCity[c] = e;
        }

        /// <summary>經緯度 → CityTransformRoot（= UnitEarth）本地座標。</summary>
        public static Vector3 LocalPositionFromLatLon(float latDeg, float lonDeg)
        {
            float phi = latDeg * Mathf.Deg2Rad;
            float lam = (lonDeg - LonOffsetDeg) * Mathf.Deg2Rad;
            float cosPhi = Mathf.Cos(phi);
            return new Vector3(
                -EarthRadius * cosPhi * Mathf.Sin(lam),
                -EarthRadius * cosPhi * Mathf.Cos(lam),
                 EarthRadius * Mathf.Sin(phi));
        }
    }
}
