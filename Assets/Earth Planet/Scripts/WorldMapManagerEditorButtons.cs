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
