using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class UnitEarth : MonoBehaviour
{


    private void Update()
    {
         
            transform.Rotate(Vector3.back, Time.deltaTime);

        
    }
}
