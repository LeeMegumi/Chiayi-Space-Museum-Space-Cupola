// Space Cupola：此檔為編輯器專用（CustomEditor），以 UNITY_EDITOR 包住，避免輸出執行檔時找不到 UnityEditor 而失敗。
#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(WorldMapManager))]

public class WorldMapManagerEditorButtons : Editor 
{

    public override void OnInspectorGUI()
    {


        WorldMapManager myScript = (WorldMapManager)target;
        if (GUILayout.Button("Bake Mesh Colors In Editor"))
        {
            myScript.SetMeshVertexColorsForCountry();
        }


        DrawDefaultInspector();
    }
}
#endif
