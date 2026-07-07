using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using System.Linq;

public class GlobalEffectsController : MonoBehaviour
{

    [SerializeField] StarlinkEffect Starlink;
    [SerializeField] CityConnectionEffect CityConnection;
    [SerializeField] Transform CityConnectionsTransform;
    [SerializeField] float MinDistanceToConnections = 0.25f;
    [SerializeField] float MaxDistanceToConnections = 1;
    [SerializeField] Dropdown drop;
    List<string> options;
    [SerializeField] List <City> CityArrayToTravelManually = new List<City> ();
    [SerializeField] public int MaximumConnectionsCount = 100;
    [SerializeField] public float ArcHeight=0.5f;
     
    [Range (1,10)]
    [SerializeField] int ProbabilitySpawnCount = 3;
    [Range(0.1f, 10)]
    [SerializeField] public float OverallSpeed;
    public int CurrentState = 0;

    [SerializeField] MagneticFieldEffect MagneticField;

    public static GlobalEffectsController instance;
    void Start()
    {
        instance= this;
        drop.onValueChanged.AddListener(OnChange);
        CityConnectionsTransform.SetParent(FindObjectOfType<UnitEarth>().transform);
        MagneticField.transform.SetParent(CityConnectionsTransform.parent);
        if (PlayerPrefs.HasKey("Effect"))
        {
            OnChange(PlayerPrefs.GetInt("Effect"));
        }else 
        OnChange(0);
    }
    private void OnChange(int id)
    {

        if (id == 0)         
        {
            CurrentState = 0;
            Starlink.gameObject.SetActive(false);
            CityConnection.gameObject.SetActive(false);
            MagneticField.gameObject.SetActive(false);
        }
        if (id == 1)
        {
            CurrentState = 1;
            Starlink.gameObject.SetActive(true);
            CityConnection.gameObject.SetActive(false);
            MagneticField.gameObject.SetActive(false);
        }
        if (id == 2)
        {
            CurrentState = 2;
            Starlink.gameObject.SetActive(false);
            CityConnection.gameObject.SetActive(true);
            MagneticField.gameObject.SetActive(false);
            Generate();
        }
         if (id == 3)
        {
            CurrentState = 3;
            Starlink.gameObject.SetActive(false);
            CityConnection.gameObject.SetActive(false);
            MagneticField.gameObject.SetActive(true);
        }

        drop.value = id;
        PlayerPrefs.SetInt("Effect", id);
        PlayerPrefs.Save();
        Debug.Log("Effect: " + id);

    }
    private void OnDestroy()
    {
        drop.onValueChanged.RemoveListener(OnChange); 
    }


    public List<CityConnectionEffect> links = new List<CityConnectionEffect>();

    void Generate()
    {
        for (int i = 0; i < links.Count; i++)
        {
            Destroy(links[i].gameObject);
        }
        links.Clear();

        for (int i = 0; i < CityArrayToTravelManually.Count; i++)
        {
            MakeNew(CityArrayToTravelManually[i].transform);
        }
        if (links.Count == 0)
        {
             MakeNew(CityManager.instance.Citys[Random.Range(0, CityManager.instance.Citys.Count)].transform);
        }
 
    }

    void MakeNew(Transform a, Transform b)
    {
        CityConnectionEffect c= Instantiate(CityConnection, CityConnectionsTransform);
        c.a = a;
        c.b = b; 
        c.Vypuklost = ArcHeight;
        links.Add(c);
    }
    void MakeNew(Transform a)
    {
       
        int rnd = 0;
        int count = 0;
        while (rnd==0 && count<50)
        {
            count++;
            rnd = UnityEngine.Random.Range(0, CityManager.instance.Citys.Count);
            float dist = Vector3.Distance(CityManager.instance.Citys[rnd].transform.position, a.transform.position);
            if (dist > MinDistanceToConnections && dist < MaxDistanceToConnections)
            {
                break;
            }
            rnd = 0;
        }
        CityConnectionEffect c = Instantiate(CityConnection, CityConnectionsTransform);
        c.a = a;
        c.b = CityManager.instance.Citys[rnd].transform;
        c.transform.position = c.a.transform.position;
        c.Vypuklost = ArcHeight; 
        c.PS.Stop();
        c.PS.Play();
        
        links.Add(c);

        c.SetLifeTime();


    }
    public void GetDestination(Transform a)
    {
        int rnd=Random.Range(1, ProbabilitySpawnCount);

        for (int i = 0; i < rnd; i++)
       
        if (MaximumConnectionsCount > links.Count)
        {
            MakeNew(a);
        }
    }


}
