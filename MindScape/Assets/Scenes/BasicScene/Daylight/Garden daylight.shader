Shader "MindScape/Outdoor daylight" {
 SubShader { Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" }
 Pass { Cull Off
 HLSLPROGRAM
 #pragma vertex Vert
 #pragma fragment Frag
 #pragma multi_compile_instancing
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
 struct Attributes {float4 positionOS:POSITION; half4 color:COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID};
 struct Varyings {float4 positionCS:SV_POSITION; half3 color:TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO};
 #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
 Varyings Vert(Attributes i) {Varyings o; UNITY_SETUP_INSTANCE_ID(i); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o); o.positionCS=TransformObjectToHClip(i.positionOS.xyz); o.color=i.color.rgb;
 #ifndef UNITY_COLORSPACE_GAMMA
 o.color=SRGBToLinear(o.color);
 #endif
 return o;}
 half4 Frag(Varyings i):SV_Target { return half4(i.color,1); }
 ENDHLSL
 } } }
