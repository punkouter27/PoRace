// H.3 strain glow: an additive Fresnel shell plus a flat fill, drawn as an extra material on a strained leg link so
// the heat reads at broadcast distance (HDR colour, so bloom picks it up). StrainMonitor attaches it only while the
// link is hot and drives _RimColor / _Fill per renderer. A custom shader instead of URP Lit's _EMISSION keyword: a
// keyword only enabled at runtime is stripped from player builds.
Shader "PoRace/StrainRim" {
  Properties {
    [HDR] _RimColor ("Rim Color", Color) = (1, 0.15, 0.05, 1)
    _RimPower ("Rim Power", Range(0.5, 8)) = 2.5
    _Fill ("Fill", Range(0, 1)) = 0.1
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
        half4 _RimColor; half _RimPower; half _Fill;
      CBUFFER_END
      struct A { float4 pos : POSITION; float3 n : NORMAL; };
      struct V { float4 pos : SV_POSITION; float3 n : TEXCOORD0; float3 v : TEXCOORD1; };
      V vert(A i) {
        V o; float3 w = TransformObjectToWorld(i.pos.xyz);
        o.pos = TransformWorldToHClip(w); o.n = TransformObjectToWorldNormal(i.n); o.v = GetWorldSpaceViewDir(w); return o;
      }
      half4 frag(V i) : SV_Target {
        half f = pow(1.0h - saturate(dot(normalize(i.n), normalize(i.v))), _RimPower);
        return half4(_RimColor.rgb * (f + _Fill), 0);
      }
      ENDHLSL
    }
  }
}
