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
    void Update()
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

      SelectCountry();
    }

    void SelectCountry()
    {
        PlaceUnitPoint();

        RaycastHit hit;
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);

        if (Physics.Raycast(ray, out hit, 1000))
        {

            if (hit.collider.gameObject == null) return;
            Country tempCountry = countries.Find(X => X.gameObject == hit.collider.gameObject);
            if (tempCountry != null)
            {
                if (tempCountry != CurrentHoveredCountry)
                {
                    if (CurrentHoveredCountry != null) CurrentHoveredCountry.Hovered = false;
                    CurrentHoveredCountry = tempCountry;
                    HoveredEarthUVCoord = hit.textureCoord;
                }

                CurrentHoveredCountry.Hovered = true;
                return;
            }
            else
            {
                if (CurrentHoveredCountry != null) CurrentHoveredCountry.Hovered = false;
                CurrentHoveredCountry = null;
            }
        }
        else
        {
            if (CurrentHoveredCountry != null) CurrentHoveredCountry.Hovered = false;
            CurrentHoveredCountry = null;
        }


    }

    public Vector2 HoveredEarthUVCoord;
    public Vector2 SelectedEarthUVCoord;


    void PlaceUnitPoint()
    {
        if (Input.GetMouseButton(0))
        {
            RaycastHit hit;
        if (Physics.Raycast(Camera.main.ScreenPointToRay(Input.mousePosition), out hit, 1000 ))
        {
             

           


                CurrentSelectedCountry = CurrentHoveredCountry;
                SelectedEarthUVCoord = HoveredEarthUVCoord;


             UnitPoint.transform.position = hit.point;
             UnitPoint.transform.SetParent(FindObjectOfType<UnitEarth>().transform);
            }
         
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
