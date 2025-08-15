// GrassPainterEditor.cs
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(GrassPainter))]
public class GrassPainterEditor : Editor
{
    private void OnSceneGUI()
    {
        GrassPainter painter = (GrassPainter)target;
        Event e = Event.current;
        if (e.type == EventType.MouseDown && e.button == 0 && !e.alt)
        {
            Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
            painter.AddGrass(ray);
            e.Use();
        }
        if (e.type == EventType.MouseMove || e.type == EventType.MouseDrag)
        {
            SceneView.RepaintAll();
        }
    }

    public override void OnInspectorGUI()
    {
        base.OnInspectorGUI();

        GrassPainter painter = (GrassPainter)target;
        if (GUILayout.Button("Çå³ýËùÓÐ²Ý"))
        {
            painter.ClearAllGrass();
        }
    }
}