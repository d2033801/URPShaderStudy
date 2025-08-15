using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Random = UnityEngine.Random;
#if UNITY_EDITOR
using UnityEditor;
#endif

[Serializable]
public class GrassInstance
{
    public Vector3 position;
    public float rotation;
    public float height;
    public Color color;
}

[ExecuteInEditMode]
public class GrassPainter : MonoBehaviour
{
    [Header("��������")]
    public Mesh grassMesh;
    public Material grassMaterial;

    [Header("Mesh ��Դ����")]
    public GameObject targetObject;

    [Header("��ˢ����")]
    [Range(0.1f, 5f)]
    public float brushSize = 1.0f;
    [Range(1, 10)]
    public int grassPerBrushStroke = 3;
    public LayerMask paintableLayers = ~0;

    [Header("������")]
    [Range(0.3f, 2f)]
    public float minHeight = 0.8f;
    [Range(0.3f, 2f)]
    public float maxHeight = 1.2f;

    [Header("��ǰ��ɫ")]
    public Color currentGrassColor = new Color(0.2f, 0.8f, 0.3f, 1f);

    // �����ݻᱻ���л�������
    public List<GrassInstance> grassInstances = new List<GrassInstance>();

    private int colorIndex = 0;
    public Color[] availableColors = new Color[]
    {
        new Color(0.2f, 0.8f, 0.3f, 1f),
        new Color(0.8f, 0.8f, 0.2f, 1f),
        new Color(0.8f, 0.3f, 0.2f, 1f),
        new Color(0.2f, 0.5f, 0.8f, 1f)
    };

    const int kMaxInstancesPerBatch = 1023; // DrawMeshInstanced ����

    void OnEnable()
    {
        // ȷ�����ʹ�ѡ GPU Instancing����ֹû������һ�ζ�����������
        if (grassMaterial != null && !grassMaterial.enableInstancing)
            grassMaterial.enableInstancing = true;

        RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
    }

    void OnDisable()
    {
        RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
    }

    private void OnDrawGizmosSelected()
    {
#if UNITY_EDITOR
        if (SceneView.lastActiveSceneView == null)
            return;
        if (Event.current == null || Event.current.type != EventType.Repaint)
            return;

        Ray ray = HandleUtility.GUIPointToWorldRay(Event.current.mousePosition);
        if (Physics.Raycast(ray, out var hit, 100f, paintableLayers))
        {
            Color gizmoColor = currentGrassColor;
            gizmoColor.a = 0.3f;
            Gizmos.color = gizmoColor;
            Gizmos.DrawSphere(hit.point, brushSize);
        }
#endif
    }

    private void Update()
    {
        // ֻ�ڱ༭��ģʽ�»���&ͿĨ��������ԭ�߼����ֲ��䣩
        if (Application.isPlaying)
            return;

        if (Input.GetKeyDown(KeyCode.C))
        {
            colorIndex = (colorIndex + 1) % availableColors.Length;
            currentGrassColor = availableColors[colorIndex];
        }

        if (Input.GetMouseButtonDown(0))
        {
            AddGrass();
        }
        else if (Input.GetMouseButtonDown(1))
        {
            ModifyGrassColor();
        }

        // �༭������ sharedMesh������ʱ�� mesh
        if (targetObject != null)
        {
            MeshFilter mf = targetObject.GetComponent<MeshFilter>();
            if (mf != null)
            {
#if UNITY_EDITOR
                if (!Application.isPlaying)
                    grassMesh = mf.sharedMesh;
                else
#endif
                    grassMesh = mf.mesh;
            }
        }
    }

    public void AddGrass()
    {
        // �༭��̬���� SceneView ��ʰȡ��������ԭ����д�����������þͲ�����
        Ray ray;
#if UNITY_EDITOR
        if (!Application.isPlaying && SceneView.lastActiveSceneView != null)
        {
            ray = HandleUtility.GUIPointToWorldRay(Event.current != null ? Event.current.mousePosition : Input.mousePosition);
        }
        else
#endif
        {
            // ����
            var cam = Camera.main ?? Camera.current;
            if (cam == null) return;
            ray = cam.ScreenPointToRay(Input.mousePosition);
        }

        AddGrass(ray);
    }

    public void AddGrass(Ray ray)
    {
        if (Physics.Raycast(ray, out RaycastHit hit, 100f, paintableLayers))
        {
            for (int i = 0; i < grassPerBrushStroke; i++)
            {
                Vector2 randomOffset = Random.insideUnitCircle * brushSize;
                Vector3 offset = new Vector3(randomOffset.x, 0, randomOffset.y);

                if (Physics.Raycast(hit.point + offset + hit.normal * 0.1f, -hit.normal, out RaycastHit checkHit, 0.2f, paintableLayers))
                {
                    grassInstances.Add(new GrassInstance
                    {
                        position = checkHit.point,
                        rotation = Random.Range(0f, 360f),
                        height = Random.Range(minHeight, maxHeight),
                        color = currentGrassColor
                    });
                }
            }
        }
    }

    public void ModifyGrassColor()
    {
        Ray ray;
#if UNITY_EDITOR
        if (!Application.isPlaying && SceneView.lastActiveSceneView != null)
        {
            ray = HandleUtility.GUIPointToWorldRay(Event.current != null ? Event.current.mousePosition : Input.mousePosition);
        }
        else
#endif
        {
            var cam = Camera.main ?? Camera.current;
            if (cam == null) return;
            ray = cam.ScreenPointToRay(Input.mousePosition);
        }

        if (Physics.Raycast(ray, out RaycastHit hit, 100f, paintableLayers))
        {
            for (int i = 0; i < grassInstances.Count; i++)
            {
                if (Vector3.Distance(grassInstances[i].position, hit.point) < brushSize)
                {
                    grassInstances[i].color = currentGrassColor;
                }
            }
        }
    }

    // ���� �ؼ��޸ģ��� URP ����Ⱦ�ص����� ���� //
    private void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
    {
        // �����޹����������Ԥ��/������ظ����ƣ�
        if (camera.cameraType == CameraType.Preview || camera.cameraType == CameraType.Reflection)
            return;

        DrawGrass();
    }

    private void DrawGrass()
    {
        if (grassMaterial == null || grassMesh == null) return;
        int count = grassInstances.Count;
        if (count == 0) return;

        // Ŀ���������ţ������Ҫ�������Ÿı�ݸ߶ȣ��ɲ��뵽ʵ�����Ի����
        // ��ǰ shader ���Ѿ��� _GrassHeight ���� Y ���ţ������������õ�λ���ż��ɣ�
        // Vector3 baseScale = targetObject != null ? targetObject.transform.lossyScale : Vector3.one;

        // �� 1023 ����
        int drawn = 0;
        while (drawn < count)
        {
            int batchCount = Mathf.Min(kMaxInstancesPerBatch, count - drawn);

            // ע�⣺��Ȼ���Ǵ��˾��󣬵� shader ��û�� unity_ObjectToWorld�����Ծ�����Ϊ��λ����
            var matrices = new Matrix4x4[batchCount];
            var positions = new Vector4[batchCount];
            var colors = new Vector4[batchCount];
            var heights = new float[batchCount];

            for (int i = 0; i < batchCount; i++)
            {
                var inst = grassInstances[drawn + i];

                // ��λ���������� shader ��ʹ��ʵ�������Խ���λ��/��ת/�߶ȣ�
                matrices[i] = Matrix4x4.identity;

                positions[i] = new Vector4(inst.position.x, inst.position.y, inst.position.z, inst.rotation);
                colors[i] = inst.color;
                heights[i] = inst.height;
            }

            var props = new MaterialPropertyBlock();
            props.SetVectorArray("_GrassPosition", positions);
            props.SetVectorArray("_GrassColor", colors);
            props.SetFloatArray("_GrassHeight", heights);

            Graphics.DrawMeshInstanced(
                grassMesh,
                0,
                grassMaterial,
                matrices,
                batchCount,
                props,
                ShadowCastingMode.On,
                true // ������Ӱ��URP �´˲�������Ӱ��̫�࣬������һ�£�
            );

            drawn += batchCount;
        }
    }

    [ContextMenu("������в�")]
    public void ClearAllGrass()
    {
        grassInstances.Clear();
    }
}
