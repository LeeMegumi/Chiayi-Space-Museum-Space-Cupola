using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using System.IO;
using UnityEngine.EventSystems;

public class WorldMapManager : MonoBehaviour
{
    #region Variables 

    [SerializeField] MeshRenderer EarthRenderer;
    [SerializeField] GameObject Clouds;
    [SerializeField] GameObject Glow;
    [SerializeField] GameObject Ocean;
    [SerializeField] public List<Country> countries;
    [SerializeField] public Material Earth;
    [SerializeField] public Material Population;
    [SerializeField] public Material Science;
    [SerializeField] public Material Transport;
    [SerializeField] public Material Disaster;
    [SerializeField] public Material Climat;
    [SerializeField] public Material Religion;
    [SerializeField] public Material Wealth;
    [SerializeField] public List<Texture2D> WorldLayersTextures;
    [Header ("Use it for different zones on ClimatTexture")]  [SerializeField] public List<Color> ClimatZonesColors;
    [SerializeField] public List<string> ReligionNames;

    [Header ("Use it for different Religions")]  [SerializeField] public List<Color> ReligionColors;
    [Header ("Use it for Wealth Color Interpolation")]  [SerializeField] public List<Color> WealthColors;
    [SerializeField] public List<string> ClimatZonesNames;
    [Header("Use this file with void SetNames()")]
    [SerializeField] public TextAsset CountryNamesJSONFile;
    [SerializeField] public TextAsset CountryPopulationJSonFile;
    [Header("Use this file with void SetPopulationAndWealth()")]
    [SerializeField] public List<Material> EarthMaterialsByTypeOnCountries;
    [Header("Prefab for Select Point on Earth")]
    [SerializeField] GameObject UnitPoint;
    
    public Country CurrentHoveredCountry;
    private Country _currentSelectedCountry;
    public Country CurrentSelectedCountry
    {
        get => _currentSelectedCountry;
        set
        {
            if (_currentSelectedCountry != null)
            {
                _currentSelectedCountry.meshRenderer.sharedMaterial= EarthMaterialsByTypeOnCountries[(int)CurrentState];
            }
            if (value != null)
            {
                value.meshRenderer.sharedMaterial = new Material(value.meshRenderer.sharedMaterial);
                value.meshRenderer.sharedMaterial.SetFloat("_StripesValue", 1);
            }
            _currentSelectedCountry = value;
        }
    }
    public float currentPointValue;
    LayerMask EarthMask;
    public GameObject CurrenUnitPoint;
    public static event Action EventChangeState;
    
    public enum State { Earth = 0, Politic = 1, Population = 2, Science = 3, Transport = 4, Disaster = 5, Climat = 6, Religion = 7, Wealth =8 }
    private State _currentState;
    public State CurrentState
    {
        get => _currentState;
        set
        {
            Ocean.SetActive(true);
            ChangeAllCountriesMaterials(EarthMaterialsByTypeOnCountries[(int)value]);
             
            switch (value)
            {
                case State.Earth:
                    EarthRenderer.sharedMaterial = Earth;

                    
                    Clouds.SetActive(true);
                    Glow.SetActive(true);
                    HideMap();
                    
                    break;
                case State.Politic:
                    EarthRenderer.sharedMaterial = EarthMaterialsByTypeOnCountries[1];
                    ShowMap();
                    Clouds.SetActive(false);
                    Glow.SetActive(false);

                    break;
                case State.Population:
                    EarthRenderer.sharedMaterial = Population;

                    ShowMap();
                    Clouds.SetActive(false);
                    Glow.SetActive(false);
                    break;
                case State.Science:
                    EarthRenderer.sharedMaterial = Science;
                    Glow.SetActive(false);
                    ShowMap();
                    Clouds.SetActive(false);
                    break;
                case State.Transport:
                    EarthRenderer.sharedMaterial = Transport;
                    Glow.SetActive(false);
                    ShowMap();
                    Clouds.SetActive(false);
                    break;
                case State.Disaster:
                    EarthRenderer.sharedMaterial = Disaster;
                    Glow.SetActive(false);
                    Clouds.SetActive(false);
                    ShowMap();
                    break;
                case State.Climat:
                    EarthRenderer.sharedMaterial = Climat;
                    Glow.SetActive(false);
                    Clouds.SetActive(false);
                    ShowMap();
                    break; 
                case State.Religion:
                    EarthRenderer.sharedMaterial = Religion;
                    Glow.SetActive(false);
                    Clouds.SetActive(false);
                    ShowMap();
                    break; 
                case State.Wealth:
                    foreach (var country in countries)
                    { 
                        country.meshRenderer.sharedMaterial = Wealth;
                    }
                    EarthRenderer.sharedMaterial = Wealth;
                    Glow.SetActive(false);
                    Clouds.SetActive(false);
                    Ocean.SetActive(true);
                    ShowMap();
                    
                    break;
                default:
                    break;
            }


            _currentState = value;
          if(EventChangeState!=null)  EventChangeState();
        }

    }
    #endregion
   

    public static WorldMapManager _instance;
    public static WorldMapManager instance
    {
        get
        {
            if (!_instance)
            {
                _instance = FindObjectOfType<WorldMapManager>();
            }
            return _instance;
        }
        set { _instance = value; }
    }
    void Awake()
    {
        if (instance == null) instance = this;
        else if (instance != this) { Destroy(gameObject); return; };

        BuildCountryLookup();
        HideMap();


    }



    void ShowMap()
    {
        Camera.main.cullingMask = ~0;


    }
    void HideMap()
    {
        Camera.main.cullingMask = ~LayerMask.GetMask("Water");

    }
    // ===== Space Cupola：固定為 Earth 檢視模式 =====
    // 檢視模式 Dropdown（連同掛在其上的 LayersController）已移除，
    // 改由這裡在啟動時明確設定 Earth，並停用 F1–F9 的模式切換熱鍵，
    // 避免展場工作人員誤觸鍵盤切到其他模式後無法切回。

    [Header("檢視模式")]
    [Tooltip("固定為 Earth 模式，並停用 F1–F9 切換熱鍵。")]
    [SerializeField] private bool lockToEarthState = true;

    void Start()
    {
        if (lockToEarthState) CurrentState = State.Earth;
    }

    void Update()
    {
        if (!lockToEarthState)
        {
            if (Input.GetKeyDown(KeyCode.F1)) CurrentState = State.Earth;
            if (Input.GetKeyDown(KeyCode.F2)) CurrentState = State.Politic;
            if (Input.GetKeyDown(KeyCode.F3)) CurrentState = State.Population;
            if (Input.GetKeyDown(KeyCode.F4)) CurrentState = State.Science;
            if (Input.GetKeyDown(KeyCode.F5)) CurrentState = State.Transport;
            if (Input.GetKeyDown(KeyCode.F6)) CurrentState = State.Disaster;
            if (Input.GetKeyDown(KeyCode.F7)) CurrentState = State.Climat;
            if (Input.GetKeyDown(KeyCode.F8)) CurrentState = State.Religion;
            if (Input.GetKeyDown(KeyCode.F9)) CurrentState = State.Wealth;
        }

      SelectCountry();
    }

    // ===== 畫面中央偵測（搖桿操控用）=====
    // 原本的偵測是對 Input.mousePosition 射線；改為螢幕正中央，
    // 讓觀眾用搖桿把國家轉到準心上就能看到資訊，不需要點選。

    [Header("畫面中央國家偵測")]
    [Tooltip("開啟：偵測畫面正中央（搖桿操控）。關閉：沿用滑鼠位置（開發除錯用）。")]
    [SerializeField] private bool useScreenCenter = true;

    [Tooltip("國家變更後需連續命中多久才更新面板，避免搖桿移動時國界附近文字閃爍（秒）。")]
    [Range(0f, 1f)]
    [SerializeField] private float hoverSettleTime = 0.15f;

    // 滯後穩定器狀態
    private Country _pendingCountry;        // 待確認的候選國家（可為 null，代表待確認「離開」）
    private bool    _hasPending;            // 是否有待確認的變更
    private float   _pendingTimer;
    private Vector2 _pendingUV;

    // GameObject -> Country 快取。原本每幀對 177 筆做 List.Find + lambda，
    // 在 4K 展場的主迴圈裡沒必要，改為字典查詢。
    private Dictionary<GameObject, Country> _countryByGameObject;

    private void BuildCountryLookup()
    {
        _countryByGameObject = new Dictionary<GameObject, Country>(countries.Count);
        for (int i = 0; i < countries.Count; i++)
        {
            if (countries[i] != null) _countryByGameObject[countries[i].gameObject] = countries[i];
        }
    }

    /// <summary>目前的偵測射線 —— 畫面中央或滑鼠位置。</summary>
    private Ray GetPointerRay()
    {
        if (useScreenCenter)
            return Camera.main.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));

        return Camera.main.ScreenPointToRay(Input.mousePosition);
    }

    /// <summary>沿偵測射線找出國家。未命中或命中非國家物件時回傳 null。</summary>
    private Country RaycastCountry(out Vector2 uv)
    {
        uv = default;

        if (!Physics.Raycast(GetPointerRay(), out RaycastHit hit, 1000)) return null;
        if (hit.collider == null) return null;

        if (_countryByGameObject == null) BuildCountryLookup();
        if (_countryByGameObject.TryGetValue(hit.collider.gameObject, out Country found))
        {
            uv = hit.textureCoord;
            return found;
        }
        return null;
    }

    void SelectCountry()
    {
        PlaceUnitPoint();

        Country candidate = RaycastCountry(out Vector2 uv);

        // 命中同一個國家：不必等待，持續更新 UV。
        // （原本只在國家「改變」時更新 UV，導致氣候／宗教停留在進入該國時的那一點；
        //   中央準心模式下必須跟著準心走才正確。）
        if (candidate == CurrentHoveredCountry)
        {
            _hasPending = false;
            if (candidate != null) HoveredEarthUVCoord = uv;
            return;
        }

        // 偵測到變更 —— 先進待確認區，連續維持 hoverSettleTime 後才真正套用。
        if (!_hasPending || candidate != _pendingCountry)
        {
            _hasPending = true;
            _pendingCountry = candidate;
            _pendingUV = uv;
            _pendingTimer = 0f;
            return;
        }

        _pendingUV = uv;
        _pendingTimer += Time.unscaledDeltaTime;
        if (_pendingTimer < hoverSettleTime) return;

        // 確認變更
        if (CurrentHoveredCountry != null) CurrentHoveredCountry.Hovered = false;
        CurrentHoveredCountry = _pendingCountry;
        if (CurrentHoveredCountry != null)
        {
            HoveredEarthUVCoord = _pendingUV;
            CurrentHoveredCountry.Hovered = true;
        }
        _hasPending = false;
    }

    public Vector2 HoveredEarthUVCoord;
    public Vector2 SelectedEarthUVCoord;


    void PlaceUnitPoint()
    {
        // 標記動作。沿用 GetPointerRay()，因此在中央準心模式下會標在畫面正中央，
        // 與資訊面板顯示的國家一致。
        if (!Input.GetMouseButton(0)) return;

        if (Physics.Raycast(GetPointerRay(), out RaycastHit hit, 1000))
        {
            CurrentSelectedCountry = CurrentHoveredCountry;
            SelectedEarthUVCoord = HoveredEarthUVCoord;

            UnitPoint.transform.position = hit.point;
            UnitPoint.transform.SetParent(FindObjectOfType<UnitEarth>().transform);
        }
    }

    [ContextMenu("Select AllCountryes")]
    void SelectAllCountriesInEditor()
    {
        countries.Clear();
        countries.AddRange(FindObjectsOfType<Country>());
    }
    [ContextMenu("SetRandomColors")]
    void SetRandomColorsAllCountriesInEditor()
    {
        foreach (var item in countries)
        {
            item.ColorCountry = UnityEngine.Random.ColorHSV(0f, 1f, 0f, 1f, 1f, 1f);
        }
    }
    [ContextMenu("SetNames")]
    void SetNames()
    {
        ;
        string[] nms = CountryNamesJSONFile.text.Split('}');
        foreach (var str in nms)


            foreach (var item in countries)
            {
                if (str.Substring(11, 2) == item.name) item.Name = str.Substring(25, str.Length - 26);
                item.meshRenderer = item.GetComponent<MeshRenderer>();
            }

    }[ContextMenu("SetPopulation")]
    void SetPopulationAndWealth()
    {
        
        string[] nms = CountryPopulationJSonFile.text.Split('\n');
        foreach (var str in nms)
        {

            string[] cntr = str.Split('\t');

            foreach (var item in countries)
            {
                if (cntr[0].Trim().ToLower() == item.Name.ToLower())
                {
                    float.TryParse(cntr[3], out item.Population);
                    float.TryParse(cntr[2], out item.Wealth);
                }
            }

        }

    }

    public int GetZone(Texture2D tex, Vector2 uv)
    {
        Color col = tex.GetPixel(Mathf.RoundToInt(uv.x * tex.width), Mathf.RoundToInt(uv.y * tex.height));
        float max = 1000000;

        int result = -1;
        for (int i = 0; i < ClimatZonesColors.Count; i++)
        {
            float temp = Vector3.Distance(new Vector3(col.r, col.g, col.b), new Vector3(ClimatZonesColors[i].r, ClimatZonesColors[i].g, ClimatZonesColors[i].b));
            if (max > temp)
            {
                max = temp;
                result = i;
            }
        }
        return result;
    } public int GetReligion(Texture2D tex, Vector2 uv)
    {
        Color col = tex.GetPixel(Mathf.RoundToInt(uv.x * tex.width), Mathf.RoundToInt(uv.y * tex.height));
        float max = 1000000;

        int result = -1;
        for (int i = 0; i < ReligionColors.Count; i++)
        {
            float temp = Vector3.Distance(new Vector3(col.r, col.g, col.b), new Vector3(ReligionColors[i].r, ReligionColors[i].g, ReligionColors[i].b));
            if (max > temp)
            {
                max = temp;
                result = i;
            }
        }
        return result;
    }
    public int GetPercentByTexture(Texture2D tex, Vector2 uv)
    {
        Color col = tex.GetPixel(Mathf.RoundToInt(uv.x * tex.width), Mathf.RoundToInt(uv.y * tex.height));
        return Mathf.RoundToInt(col.r * 100);
    }

    void ChangeAllCountriesMaterials(Material mat)
    {
        foreach (var item in countries)
        {
            item.meshRenderer.sharedMaterial = mat;
        }
    }
    [ContextMenu("CalculateWealthColorsInterpolation")]
    void CalculateWealthColorsInterpolation()
    {
        Logger.TimerStart();
        float max = 0;
        float min = 1000000000;
        Country country=null;
        for (int i = 0; i < countries.Count; i++)
        {
            if (max < countries[i].Wealth)
            {
                max = countries[i].Wealth;
                country = countries[i];
            }
                if (min > countries[i].Wealth) min = countries[i].Wealth;
        }

        Debug.Log("Max Wealth is = " + max + " " + country.Name);
        max = 100;
        for (int i = 0; i < countries.Count; i++)
        {
            countries[i].WealthColor = Color.Lerp(WealthColors[1], WealthColors[0], countries[i].Wealth / max);
        }
        Logger.TimerEnd("CalculateWealthColorsInterpolation");
    }
    [ContextMenu("SetMeshVertexColorsForCountry")]
    public void SetMeshVertexColorsForCountry()
    {
      
        foreach (var item in countries)
        {
            item.BakeColorWealthToMesh();
        } 
        
    }
    }
