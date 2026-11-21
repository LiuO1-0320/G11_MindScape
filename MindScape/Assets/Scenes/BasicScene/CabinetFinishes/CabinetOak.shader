Shader "MindScape/Cabinet Oak"
{
 Properties { _BaseMap ("Wood grain", 2D) = "white" {} }
 SubShader {
 Tags {"RenderPipeline"="UniversalPipeline" "RenderType"="Opaque"}
 Pass {
 HLSLPROGRAM
 #pragma vertex vert
 #pragma fragment frag
 #pragma multi_compile_instancing
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
 TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
 struct A {float4 p:POSITION;float3 n:NORMAL;float2 uv:TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID};
 struct V {float4 p:SV_POSITION;float2 uv:TEXCOORD0;float shade:TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO};
 V vert(A v){
   V o; UNITY_SETUP_INSTANCE_ID(v);UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
   o.p=TransformObjectToHClip(v.p.xyz);o.uv=v.uv;
   float3 n=TransformObjectToWorldNormal(v.n);
   o.shade=.55+.28*saturate(n.y)+.18*(1-abs(n.y))+.09*saturate(dot(n,normalize(float3(-.3,.65,-.6))));
   return o;
 }
 half4 frag(V i):SV_Target {return half4(SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,i.uv).rgb*i.shade,1);}
 ENDHLSL
 }
 }
}
