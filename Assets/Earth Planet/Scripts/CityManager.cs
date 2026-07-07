using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.IO;
using UnityEngine.UI;
using System.Linq;
using UnityEngine.UIElements;

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

        Logger.TimerEnd("CityManagerAwake");
        CG.alpha = 0;
    }
    int z = 0;
    private void Update()
    {
        CalculateCityDistance();
        SelectCityByMouseClick();
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
                Labels.Last().t.text = CurrentTopCity[i].Name;
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
            if(value != null)
            {
                _selectedCityLabel = value;
                CityName.text = value.city.Name;
                CityPopulation.text = value.city.PopulationCount.ToString();
                CityCountry.text = value.city.country.Name;
                CityCoordinates.text = value.city.Coordinates.x.ToString("00.0")+"\" "+ value.city.Coordinates.y.ToString("00.0")+"\"";
                CG.alpha = 1;
            }
            else
            {
                _selectedCityLabel = null;
                CG.alpha = 0;
            }
        }
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
