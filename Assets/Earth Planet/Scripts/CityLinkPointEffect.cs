using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CityLinkPointEffect : MonoBehaviour
{
    [SerializeField] public CityConnectionEffect c;
    [SerializeField] public Vector3 FixRotation;
    [SerializeField] public AnimationCurve AnimationCurve = new AnimationCurve(new Keyframe(0, 0), new Keyframe(1, 1));

    Vector3 startScale;
    void Start()
    {
        startScale = transform.localScale;
        transform.localScale = Vector3.zero;

        transform.position = c.b.position * 1.001f;
        transform.parent= c.b;
        transform.LookAt(transform.position*10);
        transform.Rotate(FixRotation);
       
    } 
    float Timer = 0;
    void Update()
    {


            Timer += Time.unscaledDeltaTime * GlobalEffectsController.instance.OverallSpeed / c.dist;
            transform.localScale = Vector3.Lerp(Vector3.zero, startScale, AnimationCurve.Evaluate(Timer));
            
               
            
          //  if(AnimationCurve.Evaluate(Timer)<0.01f) Destroy(gameObject);
    }
    
}
