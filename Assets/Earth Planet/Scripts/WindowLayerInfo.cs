using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using SpaceCupola.Localization;

public class WindowLayerInfo : MonoBehaviour
{
    public static WindowLayerInfo instance;

    [SerializeField] public List<TMPro.TextMeshProUGUI> layersText;

    [Header("中文化")]
    [Tooltip("國名顯示為「中文主、英文副」，例如：日本 Japan")]
    [SerializeField] private bool bilingualCountryName = true;

    [Tooltip("氣候帶／宗教也顯示雙語。這兩項的中文較長，預設只顯示中文以免擠壓面板。")]
    [SerializeField] private bool bilingualAttributes = false;

    [Header("無資料時的顯示字串")]
    [SerializeField] private string noCountryText = "公海區域";
    [SerializeField] private string notAvailableText = "—";

    [Header("隱藏欄位")]
    [Tooltip("隱藏人口與財富。這兩筆資料的單位存疑（人口值看起來是百萬而非千），" +
             "展場顯示錯誤數字比不顯示更糟，故預設關閉。")]
    [SerializeField] private bool hidePopulationAndWealth = true;

    [Tooltip("選填：人口那一列的父物件（含標籤與數值）。指定後會整列隱藏；" +
             "留空則只清空數值、標籤仍在。")]
    [SerializeField] private GameObject populationRow;

    [Tooltip("選填：財富那一列的父物件。")]
    [SerializeField] private GameObject wealthRow;

    Country country;
    Vector2 uvCoords;

    private float maxWealth = float.MaxValue;

    void Awake()
    {
        instance = this;

        List<Country> countries = WorldMapManager.instance.countries;
        float maxWealth = 0;
        for (int i = 0; i < countries.Count; i++)
        {
            Country country = countries[i];
            if (country.Wealth > maxWealth) maxWealth = country.Wealth;
        }
        this.maxWealth = maxWealth;

        if (hidePopulationAndWealth)
        {
            if (populationRow != null) populationRow.SetActive(false);
            if (wealthRow != null) wealthRow.SetActive(false);
        }
    }

    private void Update()
    {
        country = WorldMapManager.instance.CurrentHoveredCountry;
        uvCoords = WorldMapManager.instance.HoveredEarthUVCoord;

        // 海洋物件也帶有 Country 元件，但人口／財富沒有意義，需分開處理。
        bool isOcean = country != null && LocalizationTable.IsOcean(country.Name);

        // --- 0：名稱（國家或海洋）---
        layersText[0].text = country != null
            ? (bilingualCountryName
                ? LocalizationTable.Bilingual(country.Name)
                : LocalizationTable.Country(country.Name))
            : noCountryText;

        // --- 1：人口 ---
        // --- 2：財富 ---
        // 單位存疑，預設隱藏（見 hidePopulationAndWealth 的說明）。
        if (hidePopulationAndWealth)
        {
            layersText[1].text = string.Empty;
            layersText[2].text = string.Empty;
        }
        else
        {
            layersText[1].text = (country != null && !isOcean)
                ? country.Population.ToString() + "K"
                : notAvailableText;

            layersText[2].text = (country != null && !isOcean)
                ? country.Wealth + "$"
                : notAvailableText;
        }

        // --- 3~5：由貼圖取樣的百分比（科技／交通／天災）---
        layersText[3].text = WorldMapManager.instance.GetPercentByTexture(WorldMapManager.instance.WorldLayersTextures[3], uvCoords).ToString() + "%";

        layersText[4].text = WorldMapManager.instance.GetPercentByTexture(WorldMapManager.instance.WorldLayersTextures[4], uvCoords).ToString() + "%";

        layersText[5].text = WorldMapManager.instance.GetPercentByTexture(WorldMapManager.instance.WorldLayersTextures[5], uvCoords).ToString() + "%";

        // --- 6：氣候帶 ---
        layersText[6].text = LookupZoneName(
            WorldMapManager.instance.ClimatZonesNames,
            WorldMapManager.instance.GetZone(WorldMapManager.instance.WorldLayersTextures[6], uvCoords),
            isClimate: true);

        // --- 7：宗教 ---
        layersText[7].text = LookupZoneName(
            WorldMapManager.instance.ReligionNames,
            WorldMapManager.instance.GetReligion(WorldMapManager.instance.WorldLayersTextures[7], uvCoords),
            isClimate: false);
    }

    /// <summary>
    /// 查表取得氣候帶／宗教的中文名稱。
    /// GetZone() 與 GetReligion() 在對應色彩清單為空時會回傳 -1，
    /// 原本會直接 IndexOutOfRange，此處加上防護。
    /// </summary>
    private string LookupZoneName(List<string> names, int index, bool isClimate)
    {
        if (names == null || index < 0 || index >= names.Count) return notAvailableText;

        string english = names[index];
        string localized = isClimate
            ? LocalizationTable.ClimateZone(english)
            : LocalizationTable.Religion(english);

        return bilingualAttributes
            ? LocalizationTable.ComposeBilingual(localized, english)
            : localized;
    }
}
