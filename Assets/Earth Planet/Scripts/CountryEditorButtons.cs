// Space Cupola：此檔為編輯器專用（CustomEditor），以 UNITY_EDITOR 包住，避免輸出執行檔時找不到 UnityEditor 而失敗。
#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(Country))]
public class CountryEditorButtons : Editor
{

    public override void OnInspectorGUI()
    {


        Country myScript = (Country)target;
        if (GUILayout.Button("Bake Mesh Colors In Editor"))
        {
            myScript.BakeColorWealthToMesh();
        }


        DrawDefaultInspector();
    }
}
#endif
