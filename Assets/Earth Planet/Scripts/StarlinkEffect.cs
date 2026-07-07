using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;

public class StarlinkEffect : MonoBehaviour
{

    [SerializeField] ParticleSystem StarLinkPS;
    [SerializeField] List<StarLinkOrbit> orbits = new List<StarLinkOrbit>();
    private void Awake()
    {

        for (int i = 0; i < orbits.Count; i++)
        {
            Instantiate(StarLinkPS, orbits[i].transform);
        }

        transform.SetParent(FindObjectOfType<UnitEarth>().transform);
    }
}
