using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BSPTree : MonoBehaviour
{
    [SerializeField] int count = 10;
    [SerializeField] Vector3 Size = Vector3.one;
    private List<City> TargetObjects = new List<City>();
    [SerializeField] public GameObject[] OccludedObjects;
    [SerializeField] public float MinDistanceOcclusion = 0.1f;


    //private void Awake()
    //{
    //    AttachTargetsToSegments();
    //}

    //void AttachTargetsToSegments()
    //{

      
        
    //    foreach (var segment in Segments)
    //    {
    //        foreach (var item in segment.Cityes)
    //        {
    //          //  Debug.Log(segment.name);

    //            item.transform.SetParent(segment.transform);
                    
    //        }
    //        segment.gameObject.SetActive(false);
    //    }
    //}
    
    // Update is called once per frame
   
    
    private void OnDrawGizmosSelected()
    {
        if (Segments.Count == 0)
        {
            Gizmos.color = new Color(0, 0, 1, 0.3f);
            Gizmos.DrawCube(transform.position, transform.lossyScale);

            Gizmos.color = new Color(0, 1, 0, 0.3f);

            for (int i = 0; i < count; i++)
                for (int j = 0; j < count; j++)
                    for (int k = 0; k < count; k++)
                    {

                        Gizmos.DrawCube((transform.position) + transform.right * Size.x * i + transform.up * Size.y * j + transform.forward * Size.z * k, Size);
                    }
        }
        else
        {
            Gizmos.color = new Color(0, 0, 1, 0.3f);
            Gizmos.DrawCube(transform.position, transform.lossyScale);

            Gizmos.color = new Color(0, 1, 0, 0.3f);

            for (int i = 0; i < Segments.Count; i++)
            {
                if (!Segments[i].gameObject.activeSelf) continue;
                Gizmos.DrawWireCube(Segments[i].transform.position, Segments[i].transform.lossyScale);
            }
        }

        foreach (var item in OccludedObjects)
        {
            Gizmos.DrawSphere(item.transform.position, MinDistanceOcclusion);
        }

        
        
    }
    public List<BSPTreeSegment> Segments = new List<BSPTreeSegment>();
    public void Generate()
    {
        Segments.Clear();
        while (transform.childCount > 0)
        {
            DestroyImmediate(transform.GetChild(0).gameObject);
        }
            TargetObjects.Clear();
            TargetObjects.AddRange(FindObjectsOfType<City>());
            Debug.Log("Found " + TargetObjects.Count);



        int sum = 0;
        for (int i = 0; i < count; i++)
            for (int j = 0; j < count; j++)
                for (int k = 0; k < count; k++)
                {

                    Vector3 bbox = (transform.position) + transform.right * Size.x * i + transform.up * Size.y * j + transform.forward * Size.z * k;
                    BSPTreeSegment segment = new GameObject().AddComponent<BSPTreeSegment>();
                 
                    segment.transform.SetParent(transform);
                    segment.transform.position = bbox;

                    for (int z = 0; z < TargetObjects.Count; z++)
                    {

                        //float xx = Mathf.Abs(-TargetObjects[z].transform.position.x + bbox.x );
                        //float yy = Mathf.Abs(-TargetObjects[z].transform.position.y + bbox.y );
                        //float zz = Mathf.Abs(-TargetObjects[z].transform.position.z + bbox.z) ;
                        float xx = -TargetObjects[z].transform.position.x + bbox.x;
                        float yy = -TargetObjects[z].transform.position.y + bbox.y;
                        float zz = -TargetObjects[z].transform.position.z + bbox.z ;
                        
                        if(xx>0 &&  xx<=Size.x )
                        if(yy>0 &&  yy<=Size.y )
                        if(zz>0 &&  zz<=Size.z )
                                {
                                    segment.Cityes.Add(TargetObjects[z]);
                                    sum++;
                                    
                                }
                    }
                if (segment.Cityes.Count > 0) 
                    {
                        segment.name = "Segment " + i + ":" + j + ":" + k + " objects: " + segment.Cityes.Count;
                        segment.transform.localScale = Size;
                        Segments.Add(segment);
                    }
                    else
                    {
                        DestroyImmediate(segment.gameObject);
                    }
        
                }
        Debug.Log("Summmed objects: " + sum);
    }
    public void Clear()
    {
        Segments.Clear();
        while (transform.childCount > 0)
        {
            DestroyImmediate(transform.GetChild(0).gameObject);
        }
    }
}