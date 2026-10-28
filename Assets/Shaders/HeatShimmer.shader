// B2 motor heat shimmer: a camera-facing quad over a hot joint that refracts the scene behind it (URP camera opaque
// texture, which HeatShimmer turns on for the view camera) with a moving sine wobble inside a soft round mask, plus a
// faint warm tint. HeatShimmer drives _Strength per quad (0..1 from joint effort over 80 %). PC only (GfxTier).
Shader "PoRace/HeatShimmer" {
  Properties {
    _Strength ("Strength", Range(0, 1)) = 1
    _Distortion ("Distortion", Range(0, 0.05)) = 0.01
    _Tint ("Tint", Color) = (1, 0.6, 0.35, 1)
  }
  SubShader {
    Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
    Pass {
      Name "Shimmer"
      Tags { "LightMode" = "UniversalForward" }
      Blend SrcAlpha OneMinusSrcAlpha
      ZWrite Off
      ZTest LEqual
      Cull Off
      HLSLPROGRAM
      #pragma vertex vert
      #pragma fragment frag
      #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
      #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"
      CBUFFER_START(UnityPerMaterial)
        half _Strength; half _Distortion; half4 _Tint;
      CBUFFER_END
      struct A { float4 pos : POSITION; float2 uv : TEXCOORD0; };
      struct V { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float4 sp : TEXCOORD1; };
      V vert(A i) { V o; o.pos = TransformObjectToHClip(i.pos.xyz); o.uv = i.uv; o.sp = ComputeScreenPos(o.pos); return o; }
      half4 frag(V i) : SV_Target {
        float2 c = i.uv * 2 - 1; half mask = saturate(1 - dot(c, c)); mask = mask * mask * _Strength;
        float t = _Time.y;
        float2 wob = float2(sin(i.uv.y * 23 + t * 9) + sin(i.uv.x * 17 - t * 6.3), cos(i.uv.x * 19 + t * 7.7) + sin(i.uv.y * 29 - t * 11));
        float2 uv = i.sp.xy / i.sp.w + wob * (_Distortion * mask);
        half3 col = SampleSceneColor(uv);
        col = lerp(col, col * _Tint.rgb * 1.4, 0.25 * mask);
        return half4(col, saturate(mask * 1.5));
      }
      ENDHLSL
    }
  }
}
