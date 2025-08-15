Shader "URP/GrassRenderer"
{
    Properties
    {
        _BaseMap("Texture", 2D) = "white" {}
        _BaseColor("Color", Color) = (1, 1, 1, 1)
        _WindStrength("Wind Strength", Range(0, 1)) = 0.5
        _WindFrequency("Wind Frequency", Range(0, 5)) = 1.0
        _BendFactor("Bend Factor", Range(0, 1)) = 0.7
        _Cutoff("Alpha Cutoff", Range(0.0, 1.0)) = 0.5
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "TransparentCutout"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "AlphaTest"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Cull Back
            ZWrite On
            ZTest LEqual
            AlphaToMask On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma prefer_hlslcc gles
            #pragma exclude_renderers d3d11_9x
            #pragma target 3.5

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonMaterial.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            UNITY_INSTANCING_BUFFER_START(GrassProps)
                UNITY_DEFINE_INSTANCED_PROP(float4, _GrassPosition) // xyz: position, w: rotation
                UNITY_DEFINE_INSTANCED_PROP(float4, _GrassColor)
                UNITY_DEFINE_INSTANCED_PROP(float, _GrassHeight)
            UNITY_INSTANCING_BUFFER_END(GrassProps)

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID // 添加这行
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID // 添加这行
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            float4 _BaseMap_ST;
            float4 _BaseColor;
            float _WindStrength;
            float _WindFrequency;
            float _BendFactor;
            float _Cutoff;

            float3 ApplyWind(float3 position, float rotation, float height)
            {
                float time = _TimeParameters.y;
                float windX = sin(time * _WindFrequency + rotation) * _WindStrength * height;
                float windZ = cos(time * _WindFrequency * 0.7 + rotation) * _WindStrength * 0.5 * height;
                float bend = (position.y / height) * _BendFactor;
                return float3(windX * bend, 0, windZ * bend);
            }

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                float4 positionData = UNITY_ACCESS_INSTANCED_PROP(GrassProps, _GrassPosition);
                float height = UNITY_ACCESS_INSTANCED_PROP(GrassProps, _GrassHeight);

                // 旋转
                float rotation = positionData.w;
                float3 pos = input.positionOS.xyz;

                // Y轴缩放
                pos.y *= height;

                // 旋转矩阵（绕Y轴）
                float s = sin(radians(rotation));
                float c = cos(radians(rotation));
                float3x3 rotY = float3x3(
                    c, 0, -s,
                    0, 1,  0,
                    s, 0,  c
                );
                pos = mul(rotY, pos);

                // 风偏移
                float3 windOffset = ApplyWind(pos, rotation, height);

                // 实例化位置
                float3 worldPos = positionData.xyz + pos + windOffset;

                // 变换到裁剪空间
                float4 positionWS = float4(worldPos, 1);
                output.positionCS = TransformWorldToHClip(positionWS.xyz);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                half4 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv) * _BaseColor;
                clip(albedo.a - _Cutoff);
                return albedo;
            }
            ENDHLSL
        }
    }
    Fallback "Hidden/Universal Render Pipeline/FallbackError"
}