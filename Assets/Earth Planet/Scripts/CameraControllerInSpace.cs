using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent (typeof (Camera))]
public class CameraControllerInSpace : MonoBehaviour
{
    [HideInInspector][SerializeField] public Camera thisCamera;
    [Range(0.1f,2)] [SerializeField] private float Speed = 1;

    [Range (0.0f,1)][SerializeField] float distanceToEarthFly = 0.8f;
    
    private float FlyToTimer;
    [Header("Fly Time to Target")]
    [Range(0,10)][SerializeField] public float FlyToTime=2;
    [Header("Fly Speed Curve")]
    [SerializeField] public AnimationCurve FlyToCurve;
    [SerializeField] ParticleSystem FlyToEffect;

    bool flyBack = false;
    private float zoom;
    private Vector3 startPos, currentPos;
    public static CameraControllerInSpace instance;
    private Vector3 TargetObjectRotation;
    private Transform TargetObjectTransform;
    private Transform Pivot;

    private Vector3 targetPositionOverUnit;
    private Vector3 StartPositionOverUnit;
    private Quaternion targetRotationOverUnit;
    private Quaternion StartRotationOverUnit;

    private Transform _flyToUnit;
    public  Transform FlyToUnit
    {
        get => _flyToUnit;
        set
        {

            if (value != null) SetTargets(value);
            else if (FlyToEffect != null) FlyToEffect.Stop();
            FlyToTimer = 0;
            _flyToUnit= value;
        }
    }

    public void SetTargets(Transform thisValue)
    {
        if (FlyToEffect != null) FlyToEffect.Stop();
        if (FlyToEffect != null) FlyToEffect.Play();
        
        targetPositionOverUnit = Vector3.Lerp(transform.position, thisValue.transform.position, distanceToEarthFly);
        StartPositionOverUnit = thisCamera.transform.position;
        Transform temp = new GameObject().transform;
        temp.position = thisCamera.transform.position;
        temp.LookAt(thisValue.transform.position);
        targetRotationOverUnit = temp.rotation;
        StartRotationOverUnit = thisCamera.transform.rotation;
    }

    private void Awake()
    {
        if (instance == null)
        {
            instance = this;
            IniCamera();
        }
        else
        {
            DestroyImmediate(this.gameObject);
            return;
        }
      
    }
    public void IniCamera()
    {
        instance = this;
        if (thisCamera==null) thisCamera=GetComponent<Camera>();
        if (FlyToEffect != null) FlyToEffect.Stop();
        transform.SetParent(Pivot = new GameObject("Pivot").transform);
        TargetObjectRotation = transform.rotation.eulerAngles;
        TargetObjectTransform = new GameObject().transform;
        TargetObjectTransform.name = "TargetLockedObjectForCamera";
        TargetObjectTransform.SetParent(FindObjectOfType<UnitEarth>().transform);


    }
    void Update()
    {

        TurnCameraRotate();


        if (flyBack)
        {
            FlyBack();
            return;
        }

        if (FlyToUnit != null)
        {
            FlyTo();
            return;
        }
        
        
        Zoom();

        if (isrotate == true)
        {
            Pivot.rotation = Quaternion.Lerp(Pivot.rotation, Quaternion.Euler(Pivot.rotation.eulerAngles + Vector3.up * SpeedRotate), 10 * Time.unscaledDeltaTime);
            return;
        }


        NearEarth();
    }
    private void FlyBack()
    {
        FlyToTimer += Time.unscaledDeltaTime / FlyToTime;
        if (FlyToTimer < 1)
        {

            thisCamera.transform.position = Vector3.Lerp(targetPositionOverUnit,StartPositionOverUnit, FlyToCurve.Evaluate(FlyToTimer));
            thisCamera.transform.rotation = Quaternion.Lerp(targetRotationOverUnit,StartRotationOverUnit, FlyToCurve.Evaluate(FlyToTimer));

        }
        else flyBack=false;

    }
    private void FlyTo()
    {
        FlyToTimer += Time.unscaledDeltaTime/FlyToTime;
        if (FlyToTimer<1)
        {

            thisCamera.transform.position = Vector3.Lerp(StartPositionOverUnit, targetPositionOverUnit, FlyToCurve.Evaluate( FlyToTimer));
            thisCamera.transform.rotation= Quaternion.Lerp(StartRotationOverUnit, targetRotationOverUnit, FlyToCurve.Evaluate( FlyToTimer));
               
        }
        else FlyToUnit = null;
        

    }

    bool locked = false;
    
    public void LockToPlanet()
    {
        
        if (locked == false)
        {
            RaycastHit hit;
            if (Physics.Raycast(thisCamera.transform.position, thisCamera.transform.forward, out hit, thisCamera.farClipPlane))
            {
                TargetObjectTransform.position= hit.point;
            }

            locked = true;
        }
        else
        {
            locked = false;
        }
    }

    private void NearEarth()
    {
        
        
        if (Input.GetMouseButtonDown(1))
            {
                locked = false;
                startPos = Input.mousePosition;
                currentPos = Pivot.rotation.eulerAngles;
            } else
        if (Input.GetMouseButton(1))
            {
                Vector3 temp = ((Input.mousePosition - startPos) / Screen.width) * 100;

                TargetObjectRotation = new Vector3( currentPos.x- temp.y, currentPos.y + temp.x, 0 )  ;
                TargetObjectRotation = new Vector3(ClampedAngle( TargetObjectRotation .x), ClampedAngle(TargetObjectRotation.y), ClampedAngle(TargetObjectRotation.z) );

            }
        if (locked!=false)
        {
           // Pivot.transform.LookAt(-TargetObject.transform.position);
            TargetObjectRotation = Quaternion.LookRotation(-TargetObjectTransform.transform.position).eulerAngles;
            //return;
        }

         
        Pivot.rotation = Quaternion.Lerp(Pivot.rotation, Quaternion.Euler( TargetObjectRotation), 10 * Time.unscaledDeltaTime );
        

    }

    [SerializeField] float SpeedRotate = 1;

    private float ClampedAngle(float angleInDegrees)
    {
        if (angleInDegrees >= 360f)
        {
            return angleInDegrees - (360f * (int)(angleInDegrees / 360f));
        }
        else if (angleInDegrees >= 0f)
        {
            return angleInDegrees;
        }
        else
        {
            float f = angleInDegrees / -360f;
            int i = (int)f;
            if (f != i)
                ++i;
            return angleInDegrees + (360f * i);
        }
    }
    private void Zoom()
    {
        zoom += Speed * Input.mouseScrollDelta.y;
        if (zoom != 0) Pivot.localScale *= 1 - 0.1f * zoom * Time.unscaledDeltaTime*5;

        Pivot.localScale = Vector3.one * (Mathf.Clamp(Pivot.localScale.x, 0.45f, 4));
        zoom = Mathf.Lerp(zoom, 0, Time.unscaledDeltaTime*5  );
        if (Input.GetMouseButtonDown(2)) zoom = 0;
    }
    private void Reset()
    {
        thisCamera = GetComponent<Camera>();
        FlyToCurve =  AnimationCurve.EaseInOut(0,0,1,1);
    }


    bool isrotate = false;
    void TurnCameraRotate()
    {
        if (Input.GetKeyUp(KeyCode.F11))
        {
            if (isrotate == false)
            {
                isrotate = true;
            }
            else
                isrotate = false;


        }
    }
}


