using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BSPTreeCameraOcclusionObjectControl : MonoBehaviour
{
    [SerializeField] Transform OcclusionObject;
    Camera cam;
    void Start()
    {
        cam = GetComponent<Camera>();
    }

    // Update is called once per frame
    void Update()
    {
        PhysicsRaycastToObjectFromCamera();
    }

    void PhysicsRaycastToObjectFromCamera()
    {
        RaycastHit hit;
        if (Physics.Raycast(cam.transform.position, cam.transform.forward, out hit, cam.farClipPlane))
        {
            OcclusionObject.position = hit.point;
        }
    }
    
}
