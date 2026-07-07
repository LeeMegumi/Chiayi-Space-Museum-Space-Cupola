using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "CityManagerSettings", menuName = "ScriptableObjects/CityManagerSettings", order = 1)]
public class CityManagerScriptableObject : ScriptableObject
{
    [SerializeField] public AnimationCurve CityLabelFadeByDistanceFromCenter = new AnimationCurve(new Keyframe(1, 1), new Keyframe(1, 1));
    [SerializeField] public float SpeedByDistanceFromCenter = 1;
    [SerializeField] public AnimationCurve CityLabelFadeByCameraZoom = new AnimationCurve(new Keyframe(1, 1), new Keyframe(1, 1));
    

    [SerializeField] public AnimationCurve CityLabelFadeByCityPopulation = new AnimationCurve(new Keyframe(1, 1), new Keyframe(1, 1));

    [SerializeField] public float RadiusFromCenterToShowCity = 1;
    [SerializeField] public float CityLabelsMax = 30;
}
