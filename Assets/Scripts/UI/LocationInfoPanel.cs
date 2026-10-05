// LocationInfoPanel.cs
// 整合資訊面板：取代原本左下（城市）與右下（國家）兩個面板。
//
// 套用位置：Assets/Scripts/UI/LocationInfoPanel.cs
// 面板外觀由 Editor 工具建立：Tools > Space Cupola > 3. 建立整合資訊面板
//
// 顯示內容（依使用者指定順序）：
//   標題  —— 準心附近的城市名；附近沒有城市時改顯示國家／海域名稱
//   國家  —— 城市所屬國家，或準心所在國家
//   座標  —— 城市座標，或準心所在地表點的經緯度
//   氣候帶 —— 準心所在陸地的氣候帶
//   人口  —— 城市人口
//
// 資料來源：
//   * 城市：CityManager.SelectedCityLabel（已改為準心自動選取，含滯後防閃爍）
//   * 國家、氣候帶：WorldMapManager.CurrentHoveredCountry / HoveredEarthUVCoord（準心射線）
//   * 準心經緯度：本腳本自行以射線與地球球體求交點後換算
//
// ── 經緯度公式（以 1862 個城市反推驗證，非推測）──────────────────
// 在 UnitEarth 本地空間、半徑 R = 0.25：
//   緯度 = asin(z / r)
//   經度 = atan2(-x, -y) + 0.520°
// 補償 0.520° 後，1861 個城市誤差皆為 0.0000°（唯一離群點為同名行政區比對錯誤）。
// CityTransformRoot 在 UnitEarth 下為零旋轉，城市本地座標即 UnitEarth 座標。
// UnitEarth 會繞本地 Z 軸自轉，但本地座標不受影響。

using SpaceCupola.Localization;
using TMPro;
using UnityEngine;

namespace SpaceCupola.UI
{
    public class LocationInfoPanel : MonoBehaviour
    {
        [Header("文字元件（由 Editor 工具自動指定）")]
        [SerializeField] private TMP_Text caption;
        [SerializeField] private TMP_Text title;
        [SerializeField] private TMP_Text countryValue;
        [SerializeField] private TMP_Text coordinatesValue;
        [SerializeField] private TMP_Text climateValue;
        [SerializeField] private TMP_Text populationValue;

        [Header("顯示")]
        [Tooltip("附近沒有城市、標題改顯示國家時，使用「中文 English」雙語。")]
        [SerializeField] private bool bilingualCountryTitle = true;

        [Tooltip("文字更新間隔（秒）。文字只在內容改變時才重建，但查詢本身也不必每幀做。")]
        [Range(0f, 0.5f)]
        [SerializeField] private float refreshInterval = 0.1f;

        [SerializeField] private string emptyText         = "—";
        [SerializeField] private string captionCity       = "最近城市  <size=70%>NEAREST CITY</size>";
        [SerializeField] private string captionCountry    = "國家／地區  <size=70%>COUNTRY</size>";
        [SerializeField] private string captionOcean      = "海域  <size=70%>OCEAN</size>";
        [SerializeField] private string captionNone       = "目前位置  <size=70%>LOCATION</size>";
        [SerializeField] private string noCountryTitle    = "公海區域";

        [Header("地球座標換算（已驗證，勿隨意更改）")]
        [SerializeField] private float earthRadius = 0.25f;
        [SerializeField] private float longitudeOffsetDegrees = 0.52f;

        private float _timer;
        private UnitEarth _earth;

        private void OnEnable() => _timer = 0f;

        private void Update()
        {
            _timer -= Time.unscaledDeltaTime;
            if (_timer > 0f) return;
            _timer = refreshInterval;
            Refresh();
        }

        public void Refresh()
        {
            WorldMapManager wm = WorldMapManager.instance;
            CityManager cm = CityManager.instance;

            // 選取中的城市標籤可能已被回收（Unity 的 == null 會成立）
            City city = null;
            if (cm != null && cm.SelectedCityLabel != null) city = cm.SelectedCityLabel.city;

            Country hovered = wm != null ? wm.CurrentHoveredCountry : null;
            Country country = (city != null && city.country != null) ? city.country : hovered;
            string countryKey = CountryKey(country);                 // 英文國名（處理 UK / XK 空白問題）
            bool isOcean = country != null && LocalizationTable.IsOcean(countryKey);

            // 準心經緯度：座標欄與「台灣範圍判定」都會用到
            bool hasLatLon = TryGetCrosshairLatLon(out float lat, out float lon);

            // 準心在台灣本島或澎湖範圍內時，國家一律為台灣。
            // 場景沒有台灣的國家網格，射線可能打到其他國家的網格；以經緯度判定不受網格影響。
            bool inTaiwan = city == null && hasLatLon && IsInTaiwan(lat, lon);

            // ── 標題與小標 ──
            if (city != null)
            {
                SetText(caption, captionCity);
                SetText(title, SpaceCupola.Cities.CityDirectory.TitleText(city));   // 「東京 Tokyo」
            }
            else if (inTaiwan)
            {
                SetText(caption, captionCountry);
                SetText(title, bilingualCountryTitle
                    ? LocalizationTable.ComposeBilingual(TaiwanZh, TaiwanEn)
                    : TaiwanZh);
            }
            else if (country != null)
            {
                SetText(caption, isOcean ? captionOcean : captionCountry);
                SetText(title, bilingualCountryTitle
                    ? LocalizationTable.Bilingual(countryKey)
                    : LocalizationTable.Country(countryKey));
            }
            else
            {
                SetText(caption, captionNone);
                SetText(title, noCountryTitle);
            }

            // ── 國家 ──
            // 選到城市時以城市資料的國名為準：台灣城市沒有對應的國家網格（country 為 null），
            // 且 CSV 國名與網格國名寫法不同（Vietnam / Viet Nam），城市資料已統一譯好。
            string cityCountry = city != null ? SpaceCupola.Cities.CityDirectory.CountryText(city) : null;
            if (!string.IsNullOrEmpty(cityCountry))
                SetText(countryValue, cityCountry);
            else if (inTaiwan)
                SetText(countryValue, TaiwanZh);
            else
                SetText(countryValue, country != null && !isOcean
                    ? LocalizationTable.Country(countryKey)
                    : emptyText);

            // ── 座標 ──
            if (city != null)
                SetText(coordinatesValue, FormatLatLon(city.Coordinates.x, city.Coordinates.y));
            else if (hasLatLon)
                SetText(coordinatesValue, FormatLatLon(lat, lon));
            else
                SetText(coordinatesValue, emptyText);

            // ── 氣候帶：只在準心落在陸地時查（海面上查氣候貼圖會得到錯誤的最近色） ──
            SetText(climateValue, (hovered != null && !LocalizationTable.IsOcean(CountryKey(hovered)))
                ? LookupClimate(wm)
                : emptyText);

            // ── 人口：城市人口 ──
            SetText(populationValue, city != null && city.PopulationCount > 0
                ? city.PopulationCount.ToString("N0")
                : emptyText);
        }

        // ── 台灣範圍 ────────────────────────────────────────────────
        // 只涵蓋本島（120.0°E 以東）與澎湖。福建沿岸（廈門 ~118.1°E、平潭 ~119.8°E / 25.5°N）
        // 都在範圍外，不會誤判。金門、馬祖緊鄰大陸沿岸且面積極小，不列入以免誤判對岸。
        private const string TaiwanZh = "台灣";
        private const string TaiwanEn = "Taiwan";

        private static bool IsInTaiwan(float lat, float lon)
        {
            bool mainIsland = lat >= 21.85f && lat <= 25.35f && lon >= 120.0f  && lon <= 122.05f;
            bool penghu     = lat >= 23.15f && lat <= 23.80f && lon >= 119.30f && lon <= 119.75f;
            return mainIsland || penghu;
        }

        // ── 國名空白的網格 ──────────────────────────────────────────
        // 原套件以 ISO 國碼比對國名，但英國網格叫「UK」（ISO 為 GB）、科索沃「XK」不在清單中，
        // 兩者的 Country.Name 皆為空白 —— 準心移到英國時面板會一片空白。以物件名稱補回。
        private static readonly System.Collections.Generic.Dictionary<string, string> CodeFallback =
            new System.Collections.Generic.Dictionary<string, string>
            {
                { "UK", "United Kingdom" },
                { "XK", "Kosovo" },
            };

        private static string CountryKey(Country c)
        {
            if (c == null) return null;
            if (!string.IsNullOrEmpty(c.Name)) return c.Name;
            return CodeFallback.TryGetValue(c.gameObject.name, out string en) ? en : c.gameObject.name;
        }

        private string LookupClimate(WorldMapManager wm)
        {
            if (wm == null || wm.WorldLayersTextures == null || wm.WorldLayersTextures.Count <= 6) return emptyText;
            Texture2D tex = wm.WorldLayersTextures[6];
            if (tex == null || wm.ClimatZonesNames == null) return emptyText;

            int idx = wm.GetZone(tex, wm.HoveredEarthUVCoord);
            if (idx < 0 || idx >= wm.ClimatZonesNames.Count) return emptyText;
            return LocalizationTable.ClimateZone(wm.ClimatZonesNames[idx]);
        }

        /// <summary>準心射線與地球球體的交點經緯度。不依賴碰撞體，海面上也能算。</summary>
        private bool TryGetCrosshairLatLon(out float lat, out float lon)
        {
            lat = lon = 0f;

            Camera cam = Camera.main;
            if (cam == null) return false;
            if (_earth == null) _earth = FindObjectOfType<UnitEarth>();
            if (_earth == null) return false;

            Transform t = _earth.transform;
            Vector3 center = t.position;
            float radius = earthRadius * t.lossyScale.x;

            Ray ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            Vector3 oc = ray.origin - center;
            float b = Vector3.Dot(oc, ray.direction);
            float c = oc.sqrMagnitude - radius * radius;
            float disc = b * b - c;
            if (disc < 0f) return false;               // 準心沒對到地球

            float dist = -b - Mathf.Sqrt(disc);         // 最近的交點（面向相機那一側）
            if (dist < 0f) return false;

            Vector3 local = t.InverseTransformPoint(ray.origin + ray.direction * dist);
            float r = local.magnitude;
            if (r < 1e-6f) return false;

            lat = Mathf.Asin(Mathf.Clamp(local.z / r, -1f, 1f)) * Mathf.Rad2Deg;
            lon = Mathf.Atan2(-local.x, -local.y) * Mathf.Rad2Deg + longitudeOffsetDegrees;
            lon = Mathf.Repeat(lon + 180f, 360f) - 180f;
            return true;
        }

        private static string FormatLatLon(float lat, float lon)
        {
            string ns = lat >= 0f ? "N" : "S";
            string ew = lon >= 0f ? "E" : "W";
            return $"{Mathf.Abs(lat):0.0}°{ns}  {Mathf.Abs(lon):0.0}°{ew}";
        }

        /// <summary>內容相同時不重設，避免 TMP 每次都重建網格。</summary>
        private static void SetText(TMP_Text target, string value)
        {
            if (target == null) return;
            if (target.text != value) target.text = value;
        }
    }
}
