using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Country : MonoBehaviour
{
    [SerializeField] public Color ColorCountry;
    [SerializeField] public Color WealthColor;
    [SerializeField] public string Name;
    [SerializeField] public float Population;
    [SerializeField] public float Wealth;
    [SerializeField] public MeshRenderer meshRenderer;
    [SerializeField] public MeshFilter meshFilter;
    private bool _Hovered;
    public bool Hovered
    {
        get => _Hovered;
        set
        {
            //if(value==true)
            //   gameObject.layer = 0;
            //else
            //   gameObject.layer = 4;
            //_Hovered = value;
            
        }
    } 
    private void OnValidate()
    {
        if (meshRenderer == null) meshRenderer = GetComponent<MeshRenderer>();
        if (meshFilter == null) meshFilter = GetComponent<MeshFilter>();
       
    }


    Color[] colors;
    Vector3[] vertices;
   
    public void BakeColorWealthToMesh()
    {

        Logger.TimerStart();
        colors = new Color[meshFilter.sharedMesh.vertices.Length];

   

        for (int i = 0; i < meshFilter.sharedMesh.vertices.Length; i++)
            colors[i] = new Color(ColorCountry.r,ColorCountry.g,ColorCountry.b,WealthColor.r);
 
        meshFilter.sharedMesh.colors = colors;
        
        Mesh mesh = new Mesh();
        mesh.vertices = meshFilter.sharedMesh.vertices;
        mesh.triangles = meshFilter.sharedMesh.triangles;
        mesh.uv = meshFilter.sharedMesh.uv;
        mesh.colors = colors;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        mesh.name = gameObject.name + "_Baked";
        meshFilter.sharedMesh = mesh;

        Logger.TimerEnd("BakeMeshCountryes");
    }
    // Update is called once per frame
     


}
