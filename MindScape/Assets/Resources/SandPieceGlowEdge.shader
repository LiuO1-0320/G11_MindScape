Shader "MindScape/SandPieceGlowEdge"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Geometry+5" }
        Pass
        {
            Cull Front
            ZWrite Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Input { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Output { float4 positionCS : SV_POSITION; UNITY_VERTEX_OUTPUT_STEREO };
            float _EdgePixels;
            Output vert(Input v)
            {
                Output o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float4 p = TransformObjectToHClip(v.positionOS.xyz);
                float4 center = TransformObjectToHClip(float3(0,0,0));
                float2 radial = (p.xy * center.w - center.xy * p.w) * _ScaledScreenParams.xy;
                float2 direction = radial * rsqrt(max(dot(radial,radial), 1e-8));
                p.xy += direction * (2.0 * _EdgePixels / _ScaledScreenParams.xy) * p.w;
                o.positionCS = p;
                return o;
            }
            half4 frag(Output i) : SV_Target { return half4(1.6,1.55,1.4,1); }
            ENDHLSL
        }
    }
}
