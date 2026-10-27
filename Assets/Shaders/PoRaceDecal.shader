// H.5 contact-aligned marks (scuffs, impact rings, blob shadows): unlit, alpha blended, texture x _BaseColor x
// vertex colour (particles fade through vertex colour, pooled quads through _BaseColor). Depth offset keeps the quad
// above the surface it lies on.
Shader "PoRace/Decal" {
  Properties {
    _BaseMap ("Texture", 2D) = "white" {}
    _BaseColor ("Color", Color) = (1, 1, 1, 1)
  }
  SubShader {
    Tags { "RenderType" = "Transparent" "Queue" = "Transparent-10" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
    Pass {
      Name "Decal"
      Tags { "LightMode" = "UniversalForward" }
      Blend SrcAlpha OneMinusSrcAlpha
      ZWrite Off
      Cull Off
      Offset -2, -2
      HLSLPROGRAM
      #pragma vertex vert
      #pragma fragment frag
      #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
      TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
      CBUFFER_START(UnityPerMaterial)
        float4 _BaseMap_ST; half4 _BaseColor;
      CBUFFER_END
      struct A { float4 pos : POSITION; float2 uv : TEXCOORD0; half4 col : COLOR; };
      struct V { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; half4 col : COLOR; };
      V vert(A i) { V o; o.pos = TransformObjectToHClip(i.pos.xyz); o.uv = TRANSFORM_TEX(i.uv, _BaseMap); o.col = i.col; return o; }
      half4 frag(V i) : SV_Target { return SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv) * _BaseColor * i.col; }
      ENDHLSL
    }
  }
}
