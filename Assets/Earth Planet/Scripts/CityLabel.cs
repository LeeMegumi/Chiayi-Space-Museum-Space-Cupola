using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class CityLabel : MonoBehaviour
{
    [SerializeField]public TMPro.TextMeshProUGUI t;
    [SerializeField]public Image image;
    [SerializeField]public RectTransform rect;
    public City city;
    public float HideTimer=0; 
    public Vector3 HidePos;
    CanvasGroup CG;

   public float ZoomMult = 1;
   public float PopulationCuveMult = 1;
    public float DistanceFadeMult = 1;
    void CalcCurve()
    {
        if (city == null) return;

        float clampPop = Mathf.Clamp(city.PopulationCount, 100000, 1000000);
        //ZoomMult = clampPop / 1000000;// CityManager.instance.CurveShowCityByPopulation.Evaluate(clampPop / 1000000) ;
        ZoomMult = CityManager.instance.settings.CityLabelFadeByCameraZoom.Evaluate(Mathf.Clamp01(CameraControllerInSpace.instance.transform.parent.localScale.x - 0.45f/ 3.55f)) ;
  
    }


    public bool Select(Vector2 MousePos)
    {
        if (Vector2.Distance(MousePos, rect.anchoredPosition) < Screen.width / 20) return true;




        return false;
    }

    void Update()
    {

       
        CalcCurve();


        if (city.ToHide)
        {
            HideTimer -= CityManager.instance.settings.SpeedByDistanceFromCenter * Time.unscaledDeltaTime;
        }
        else
        {
            HideTimer += CityManager.instance.settings.SpeedByDistanceFromCenter * Time.unscaledDeltaTime;
            if (city.distanceToCam > CityManager.instance.settings.RadiusFromCenterToShowCity) city.ToHide = true;
        }


       
        float SelectedCityMult = 1;
        if (CityManager.instance.SelectedCityLabel == this)
        {
            t.transform.localScale = image.transform.localScale = Vector3.Lerp(Vector3.zero, Vector3.one * 2, ZoomMult * HideTimer);
        }
        else
        {
            t.transform.localScale = image.transform.localScale = Vector3.Lerp(Vector3.zero, Vector3.one * SelectedCityMult, ZoomMult * HideTimer * Mathf.Clamp(1 - DistanceFadeMult, 0.001f, 1));
        
        }

        if (city != null)
        {
            rect.anchoredPosition = Camera.main.WorldToScreenPoint(city.transform.position);
            if (HideTimer < 0)
            {
                city.ToHide = false;
                Destroy(this.gameObject);

            }
        }
    }
}
