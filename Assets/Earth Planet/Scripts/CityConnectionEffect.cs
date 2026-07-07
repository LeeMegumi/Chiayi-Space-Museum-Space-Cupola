using System;
using System.Collections;
using System.Collections.Generic; 
using UnityEngine;
using UnityEngine.UIElements;
using System.Linq;

public class CityConnectionEffect : MonoBehaviour
{

    [SerializeField] public ParticleSystem PS;
    public float minDistanceToGenerate = 1;
    public Transform a;
    public Transform b;
    public float Vypuklost = 1; 
    public List<CityConnectionEffect> Childs = new List<CityConnectionEffect>();
    float Timer = 0;
    public int childsCount = 0;
    [SerializeField] CityLinkPointEffect PointEffect; 
    public float dist;
    bool isEnd=false;
    
    public void SetLifeTime()
    {
        dist = Vector3.Distance(a.position, b.position);


        var psmain = PS.main;
        psmain.startLifetime = dist / GlobalEffectsController.instance.OverallSpeed;
            
    }
    void Update()
    {


        if (isEnd) return;

        Timer +=Time.unscaledDeltaTime* GlobalEffectsController.instance.OverallSpeed/ dist;       
        Vector3 center = (a.transform.position + b.transform.position) / 2;
        Vector3 point = Vector3.Lerp(a.position, b.position, Timer);
        
        transform.position = point + center * (GetParabola(Timer) * dist * Vypuklost);
         
         if(Timer >=1)
        {
            isEnd= true;
            GlobalEffectsController.instance.GetDestination(b);


            GlobalEffectsController.instance.links.Remove(this);

            Destroy(gameObject, 30);
            Destroy(PointEffect.gameObject, 12);


        }
    }


    float GetParabola(float x)
    {
        return (float)(-4 * Math.Pow(x, 2) + 4 * x);
    }
   
}
