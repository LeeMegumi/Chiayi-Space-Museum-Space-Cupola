using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class City : MonoBehaviour
{
    public Vector2 Coordinates;
    public int PopulationCount;
    public string Name;
    public Country country;
    public float distanceToCam;
    public bool ToHide=false;
    

    
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawSphere(transform.position, 0.1f);

    }
     
}
