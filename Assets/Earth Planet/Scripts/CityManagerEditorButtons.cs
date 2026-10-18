// Space Cupola：此檔為編輯器專用（CustomEditor），以 UNITY_EDITOR 包住，避免輸出執行檔時找不到 UnityEditor 而失敗。
#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(CityManager))]
public class CityManagerInspectorButton : Editor
{ 

    public override void OnInspectorGUI()
    {
      

        CityManager myScript = (CityManager)target;
        if (GUILayout.Button("Rebuild Cityes from JSON"))
        {
            myScript.SetCityes();
        }


        DrawDefaultInspector();
    }

}
#endif
