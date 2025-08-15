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
    [Header("基础设置")]
    public Mesh grassMesh;
    public Material grassMaterial;

    [Header("Mesh 来源对象")]
    public GameObject targetObject;

    [Header("笔刷设置")]
    [Range(0.1f, 5f)]
    public float brushSize = 1.0f;
    [Range(1, 10)]
    public int grassPerBrushStroke = 3;
    public LayerMask paintableLayers = ~0;

    [Header("草属性")]
    [Range(0.3f, 2f)]
    public float minHeight = 0.8f;
    [Range(0.3f, 2f)]
    public float maxHeight = 1.2f;

    [Header("当前颜色")]
    public Color currentGrassColor = new Color(0.2f, 0.8f, 0.3f, 1f);

    // 草数据会被序列化到场景
    public List<GrassInstance> grassInstances = new List<GrassInstance>();

    private int colorIndex = 0;
    public Color[] availableColors = new Color[]
    {
        new Color(0.2f, 0.8f, 0.3f, 1f),
        new Color(0.8f, 0.8f, 0.2f, 1f),
        new Color(0.8f, 0.3f, 0.2f, 1f),
        new Color(0.2f, 0.5f, 0.8f, 1f)
    };

    const int kMaxInstancesPerBatch = 1023; // DrawMeshInstanced 限制

    void OnEnable()
    {
        // 确保材质勾选 GPU Instancing（防止没勾导致一次都画不出来）
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
        // 只在编辑器模式下绘制&涂抹交互（你原逻辑保持不变）
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

        // 编辑器下用 sharedMesh，运行时用 mesh
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
        // 编辑器态下用 SceneView 的拾取；保持你原来的写法（能正常用就不动）
        Ray ray;
#if UNITY_EDITOR
        if (!Application.isPlaying && SceneView.lastActiveSceneView != null)
        {
            ray = HandleUtility.GUIPointToWorldRay(Event.current != null ? Event.current.mousePosition : Input.mousePosition);
        }
        else
#endif
        {
            // 兜底
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

    // —— 关键修改：用 URP 的渲染回调来画 —— //
    private void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
    {
        // 过滤无关相机（避免预览/反射等重复绘制）
        if (camera.cameraType == CameraType.Preview || camera.cameraType == CameraType.Reflection)
            return;

        DrawGrass();
    }

    private void DrawGrass()
    {
        if (grassMaterial == null || grassMesh == null) return;
        int count = grassInstances.Count;
        if (count == 0) return;

        // 目标物体缩放（如果需要按其缩放改变草高度，可参与到实例属性或矩阵；
        // 当前 shader 里已经用 _GrassHeight 做了 Y 缩放，因此这里矩阵用单位缩放即可）
        // Vector3 baseScale = targetObject != null ? targetObject.transform.lossyScale : Vector3.one;

        // 按 1023 分批
        int drawn = 0;
        while (drawn < count)
        {
            int batchCount = Mathf.Min(kMaxInstancesPerBatch, count - drawn);

            // 注意：虽然我们传了矩阵，但 shader 里没用 unity_ObjectToWorld，所以矩阵设为单位即可
            var matrices = new Matrix4x4[batchCount];
            var positions = new Vector4[batchCount];
            var colors = new Vector4[batchCount];
            var heights = new float[batchCount];

            for (int i = 0; i < batchCount; i++)
            {
                var inst = grassInstances[drawn + i];

                // 单位矩阵（我们在 shader 中使用实例化属性进行位移/旋转/高度）
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
                true // 接收阴影（URP 下此参数不会影响太多，但保持一致）
            );

            drawn += batchCount;
        }
    }

    [ContextMenu("清除所有草")]
    public void ClearAllGrass()
    {
        grassInstances.Clear();
    }
}
