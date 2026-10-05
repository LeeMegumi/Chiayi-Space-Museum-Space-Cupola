using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.IO;
using UnityEngine.UI;
using System.Linq;
using UnityEngine.UIElements;
using SpaceCupola.Localization;

public class CityManager : MonoBehaviour
{

    [SerializeField] public CityManagerScriptableObject settings;

    [SerializeField] TextAsset cityCoordsFile;
    [SerializeField] CityLabel cityLabel;

    [SerializeField] Transform CityTransformRoot;
    MeshRenderer MR;
    [SerializeField] public List<City> Citys = new List<City>();
    List<Country> countries = new List<Country>();

    float _radius = 5;
    [SerializeField] Transform CanvasCityLabelsRoot;
    [SerializeField] Transform TargetDistanceTransform;
    public static CityManager instance;
    private void Awake()
    {
        Logger.TimerStart();

        instance = this;

        if (Citys.Count == 0)
        {
            SetCityes();
        }

        // Space Cupola：以修正後的城市資料覆寫（原資料名稱為行政區、人口座標為該區隨機城市，
        // 兩者不一致）。並加入台灣其餘城市與嘉義。詳見 CityDirectory.cs。
        SpaceCupola.Cities.CityDirectory.Apply(Citys, CityTransformRoot);

        Logger.TimerEnd("CityManagerAwake");
        CG.alpha = 0;
    }
    int z = 0;
    private void Update()
    {
        CalculateCityDistance();

        if (citySelectionMode == CitySelectionMode.ScreenCenter) SelectCityAtScreenCenter();
        else SelectCityByMouseClick();

        UpdateCityPanelFade();
    }
    void CalculateCityDistance()
    {

        for (int i = 0; i < Citys.Count; i++)
        {
            Citys[i].distanceToCam = Vector3.Distance(Citys[i].transform.position, TargetDistanceTransform.position);
        }
        Citys = Citys.OrderBy(x => x.distanceToCam).ToList();
        List<City> CurrentTopCity = new List<City>();

        CurrentTopCity.Clear();
        for (int i = 0; i < Citys.Count; i++)
            if (CurrentTopCity.Count < settings.CityLabelsMax)

                if (!Citys[i].ToHide)
                    if (Citys[i].distanceToCam < settings.RadiusFromCenterToShowCity)
                    {
                        CurrentTopCity.Add(Citys[i]);
                    }


        List<CityLabel> ToRemove = new List<CityLabel>();
        for (int i = 0; i < Labels.Count; i++)
        {
            if (!CurrentTopCity.Contains(Labels[i].city))
            {
                Labels[i].city.ToHide = true;
                Labels[i].HideTimer = 0.5f;
                ToRemove.Add(Labels[i]);
            }
        }
        for (int i = 0; i < ToRemove.Count; i++)
        {
            Labels.Remove(ToRemove[i]);
        }
        for (int i = Labels.Count; i < CurrentTopCity.Count; i++)
            if (i < settings.CityLabelsMax)
            {
                Labels.Add(Instantiate(cityLabel));
                Labels.Last().city = CurrentTopCity[i];
                Labels.Last().name = CurrentTopCity[i].Name;
                // 地球上的城市標籤顯示中文名（字型由 TMP 全域備援字型 LINE Seed TW 提供）
                Labels.Last().t.text = SpaceCupola.Cities.CityDirectory.LabelText(CurrentTopCity[i]);
                Labels.Last().rect.anchoredPosition = new Vector2(-1000,-1000);
                Labels.Last().city.ToHide = false;
                Labels.Last().HideTimer = 0;
                Labels.Last().transform.SetParent(CanvasCityLabelsRoot);
            }
        for (int i = 0; i < Labels.Count; i++)
        {
            Labels[i].DistanceFadeMult = Mathf.Clamp((Labels[i].city.distanceToCam - CurrentTopCity.First().distanceToCam) / (0.001f + (CurrentTopCity.Last().distanceToCam - CurrentTopCity.First().distanceToCam)), 0.01f, 1);
        }

    }

    List<CityLabel> Labels = new List<CityLabel>();
    #region Selection City
    [SerializeField] public CanvasGroup CG;
    [SerializeField] public TMPro.TextMeshProUGUI CityName;
    [SerializeField] public TMPro.TextMeshProUGUI CityPopulation;
    [SerializeField] public TMPro.TextMeshProUGUI CityCountry;
    [SerializeField] public TMPro.TextMeshProUGUI CityCoordinates;
    public CityLabel _selectedCityLabel;
    public CityLabel SelectedCityLabel
    {
        get
        {
            return _selectedCityLabel;
        }
        set
        {
            if(value != null && value.city != null)
            {
                _selectedCityLabel = value;
                City c = value.city;
                CityName.text = c.Name;

                // 人口：千分位；無資料（解析失敗為 -1 或 0）時顯示破折號
                CityPopulation.text = c.PopulationCount > 0 ? c.PopulationCount.ToString("N0") : "—";

                // 所屬國家：部分城市載入時比對不到國家，country 為 null。
                // 原本會直接 NullReference（手動點選才觸發，改自動選取後準心掃過就會觸發）。
                CityCountry.text = c.country != null ? LocalizationTable.Country(c.country.Name) : "—";

                // 座標：原本用 " 當度數符號（顯示成 25.2" 55.3"），改為標準經緯度寫法
                CityCoordinates.text = FormatCoordinates(c.Coordinates.x, c.Coordinates.y);

                _cityPanelTargetAlpha = 1f;
            }
            else
            {
                _selectedCityLabel = null;
                // 只把目標透明度設為 0，文字保留到淡出結束，避免淡出途中內容先消失
                _cityPanelTargetAlpha = 0f;
            }
        }
    }

    // ===== Space Cupola：畫面中央自動選取城市 =====

    public enum CitySelectionMode { ScreenCenter, MouseClick }

    [Header("城市選取")]
    [Tooltip("ScreenCenter：準心靠近城市時自動顯示（搖桿操控用）。\nMouseClick：原本的滑鼠點選。")]
    [SerializeField] CitySelectionMode citySelectionMode = CitySelectionMode.ScreenCenter;

    [Tooltip("準心與城市標籤的距離在此範圍內才會選取（佔螢幕高度的比例）。")]
    [Range(0.01f, 0.2f)]
    [SerializeField] float centerSelectRadius = 0.06f;

    [Tooltip("候選城市需連續維持多久才切換（秒），避免搖桿移動時面板內容閃爍。")]
    [Range(0f, 1f)]
    [SerializeField] float citySettleTime = 0.25f;

    [Tooltip("城市面板淡入／淡出所需時間（秒）。")]
    [Range(0.01f, 2f)]
    [SerializeField] float cityPanelFadeTime = 0.35f;

    float _cityPanelTargetAlpha = 0f;
    UnitEarth _earth;
    CityLabel _pendingCityLabel;
    bool _hasPendingCity;
    float _pendingCityTimer;

    void SelectCityAtScreenCenter()
    {
        // 選取中的標籤已被回收（城市離開顯示範圍、標籤物件被 Destroy）時，
        // Unity 的 == null 會成立，但 C# 參照仍在 —— 必須主動清掉，否則面板會停在舊城市。
        if (_selectedCityLabel == null && _cityPanelTargetAlpha > 0f) SelectedCityLabel = null;

        CityLabel candidate = FindLabelNearScreenCenter();
        CityLabel current = _selectedCityLabel != null ? _selectedCityLabel : null;

        if (candidate == current)
        {
            _hasPendingCity = false;
            return;
        }

        if (!_hasPendingCity || candidate != _pendingCityLabel)
        {
            _hasPendingCity = true;
            _pendingCityLabel = candidate;
            _pendingCityTimer = 0f;
            return;
        }

        _pendingCityTimer += Time.unscaledDeltaTime;
        if (_pendingCityTimer < citySettleTime) return;

        SelectedCityLabel = _pendingCityLabel;
        _hasPendingCity = false;
    }

    CityLabel FindLabelNearScreenCenter()
    {
        Camera cam = Camera.main;
        if (cam == null) return null;

        Vector2 center = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        float maxDist = Screen.height * centerSelectRadius;

        if (_earth == null) _earth = FindObjectOfType<UnitEarth>();   // 快取，避免每幀搜尋場景
        Vector3 earthCenter = _earth != null ? _earth.transform.position : Vector3.zero;
        Vector3 camPos = cam.transform.position;

        CityLabel best = null;
        float bestDist = maxDist;

        for (int i = 0; i < Labels.Count; i++)
        {
            CityLabel label = Labels[i];
            if (label == null || label.city == null) continue;

            Vector3 cityPos = label.city.transform.position;

            // 只選朝向相機這一面的城市。地球背面、正好在準心後方的城市
            // 投影到螢幕上也會落在中央，必須排除。
            Vector3 surfaceNormal = cityPos - earthCenter;
            if (Vector3.Dot(surfaceNormal, camPos - cityPos) <= 0f) continue;

            Vector3 screen = cam.WorldToScreenPoint(cityPos);
            if (screen.z <= 0f) continue;

            float d = Vector2.Distance(center, new Vector2(screen.x, screen.y));
            if (d < bestDist)
            {
                bestDist = d;
                best = label;
            }
        }
        return best;
    }

    void UpdateCityPanelFade()
    {
        if (CG == null) return;
        float step = Time.unscaledDeltaTime / Mathf.Max(0.01f, cityPanelFadeTime);
        CG.alpha = Mathf.MoveTowards(CG.alpha, _cityPanelTargetAlpha, step);
    }

    static string FormatCoordinates(float lat, float lon)
    {
        string ns = lat >= 0f ? "N" : "S";
        string ew = lon >= 0f ? "E" : "W";
        return $"{Mathf.Abs(lat):0.0}°{ns}  {Mathf.Abs(lon):0.0}°{ew}";
    }
    void SelectCityByMouseClick()
    {
        if (Input.GetMouseButton(0))
        {
            foreach (var item in Labels)
            {
                if (item.Select(Input.mousePosition))
                {
                    SelectedCityLabel = item;
                    return;
                }
            }
            SelectedCityLabel = null;
        }
    }
    #endregion

    #region CityGenerateInEditor

    public void SetCityes()
    {

        MR = FindObjectOfType<WorldMapManager>().GetComponent<MeshRenderer>();
        countries.Clear();
        countries.AddRange(FindObjectsOfType<Country>());



        while (CityTransformRoot.childCount > 0)
            DestroyImmediate(CityTransformRoot.GetChild(0).gameObject);
        Citys.Clear();
        string[] nms = cityCoordsFile.text.Split('\n');
        foreach (var str in nms)
        {



            string[] values = str.ToString().Split(';');

            if (values.Length < 1) continue;
            if (values[0] == "id") continue;
            City city = new GameObject().AddComponent<City>();
            city.transform.localScale = Vector3.one * 0.1f;

            city.country = checkCountry(values[1]);


            city.Name = values[2].Trim('\"');
            city.name = city.Name;

            // if City name already have then continue 
            if (Citys.Exists(X => X.Name == city.name))
            {
                DestroyImmediate(city.gameObject);
                continue;
            }
            city.country = WorldMapManager.instance.countries.Find(X => X.Name == values[1].Trim('\"'));

            int popCount = -1;
            string pop = values[values.Length - 1].Trim('\"');
            pop = pop.Trim('\\');
            pop = pop.Trim('"');
            pop = pop.Trim('\r');
            pop = pop.Trim('\\');
            pop = pop.Trim('\\');
            int.TryParse(pop.Substring(0, pop.Length - 1), out popCount);
            city.PopulationCount = popCount;


            float coord = 0;
            pop = values[values.Length - 3];
            pop = pop.Trim('\\');
            pop = pop.Trim('"');
            pop = pop.Trim('\r');
            pop = pop.Trim('\\');
            pop = pop.Trim('\\');
            float.TryParse(pop.Replace(".", ","), out coord);
            city.Coordinates.x = coord;

            pop = values[values.Length - 2];
            pop = pop.Trim('\\');
            pop = pop.Trim('"');
            pop = pop.Trim('\r');
            pop = pop.Trim('\\');
            pop = pop.Trim('\\');
            float.TryParse(pop.Replace(".", ","), out coord);
            city.Coordinates.y = coord;
            city.transform.SetParent(CityTransformRoot);
            city.transform.localScale = Vector3.one;

            float ltR = city.Coordinates.x * Mathf.Deg2Rad;
            float lnR = city.Coordinates.y * Mathf.Deg2Rad;

            float xPos = (_radius) * Mathf.Cos(ltR) * Mathf.Cos(lnR);
            float zPos = (_radius) * Mathf.Cos(ltR) * Mathf.Sin(lnR);
            float yPos = (_radius) * Mathf.Sin(ltR);
            city.transform.position = new Vector3(xPos, yPos, zPos);
            Citys.Add(city);
        }
        Debug.Log("Citys count: " + Citys.Count);
        CalculateMaximumPopulationSizeInCityes();
    }

    public Country checkCountry(string name)
    {
        foreach (var item in countries)
        {
            if (item.name == name)
                return item;
        }
        return null;
    }
    public void CalculateMaximumPopulationSizeInCityes()
    {
        Logger.TimerStart();
        City cit = null;
        float MaximumPopulationSizeInCityes = 0;
        foreach (var city in Citys)
        {
            if (city.PopulationCount > MaximumPopulationSizeInCityes)
            {
                MaximumPopulationSizeInCityes = city.PopulationCount;
                cit = city;
            }
        }
        Logger.TimerEnd("CalculateMaximumPopulationSizeInCityes");
        Debug.Log("MaximumPopulationSizeInCityes: " + MaximumPopulationSizeInCityes + " city: " + cit.name);
    }

    #endregion
}
