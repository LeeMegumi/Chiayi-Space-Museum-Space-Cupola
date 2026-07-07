using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class WindowLayerInfo : MonoBehaviour
{
    public static WindowLayerInfo instance;

    [SerializeField] public List<TMPro.TextMeshProUGUI> layersText;
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
    }

    private void Update()
    {
       
            country = WorldMapManager.instance.CurrentHoveredCountry;
            uvCoords = WorldMapManager.instance.HoveredEarthUVCoord;
        
        layersText[0].text = country ? country.Name : "No Country";
        
        layersText[1].text = country ? country.Population.ToString() + "K" : "N/A";

        layersText[2].text = country ? country.Wealth + "$" : "N/A";

        layersText[3].text = WorldMapManager.instance.GetPercentByTexture(WorldMapManager.instance.WorldLayersTextures[3], uvCoords).ToString() + "%";

        layersText[4].text = WorldMapManager.instance.GetPercentByTexture(WorldMapManager.instance.WorldLayersTextures[4], uvCoords).ToString() + "%";

        layersText[5].text = WorldMapManager.instance.GetPercentByTexture(WorldMapManager.instance.WorldLayersTextures[5], uvCoords).ToString() + "%";

        layersText[6].text = WorldMapManager.instance.ClimatZonesNames[WorldMapManager.instance.GetZone(WorldMapManager.instance.WorldLayersTextures[6], uvCoords)];

        layersText[7].text = WorldMapManager.instance.ReligionNames[WorldMapManager.instance.GetReligion(WorldMapManager.instance.WorldLayersTextures[7], uvCoords)];

    
    }
}
