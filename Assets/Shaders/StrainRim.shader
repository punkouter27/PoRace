// H.3 strain rim: an additive Fresnel shell drawn as an extra material on a strained leg link, so the heat reads at
// broadcast distance. StrainMonitor attaches it only while the link is hot and drives _RimColor per renderer.
Shader "PoRace/StrainRim" {
  Properties {
    [HDR] _RimColor ("Rim Color", Color) = (1, 0.15, 0.05, 1)
    _RimPower ("Rim Power", Range(0.5, 8)) = 2.5
  }
  SubShader {
    Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
    Pass {
      Name "Rim"
      Tags { "LightMode" = "UniversalForward" }
      Blend One One
      ZWrite Off
      ZTest LEqual
      Cull Back
      HLSLPROGRAM
      #pragma vertex vert
      #pragma fragment frag
      #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
      CBUFFER_START(UnityPerMaterial)
        half4 _RimColor; half _RimPower;
      CBUFFER_END
      struct A { float4 pos : POSITION; float3 n : NORMAL; };
      struct V { float4 pos : SV_POSITION; float3 n : TEXCOORD0; float3 v : TEXCOORD1; };
      V vert(A i) {
        V o; float3 w = TransformObjectToWorld(i.pos.xyz);
        o.pos = TransformWorldToHClip(w); o.n = TransformObjectToWorldNormal(i.n); o.v = GetWorldSpaceViewDir(w); return o;
      }
      half4 frag(V i) : SV_Target {
        half f = pow(1.0h - saturate(dot(normalize(i.n), normalize(i.v))), _RimPower);
        return half4(_RimColor.rgb * f, 0);
      }
      ENDHLSL
    }
  }
}
