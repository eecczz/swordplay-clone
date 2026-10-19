Shader "Swordplay/Sword Slash Ribbon"
{
    Properties
    {
        _MainTex("Slashing Bump", 2D) = "white" {}
        _MaskTex("Oval Mask", 2D) = "white" {}
        [HDR] _GlowColor("Glow Color", Color) = (0.15,1.1,2,0.9)
        _Intensity("Intensity", Range(0,3)) = 1.1
        _MotionPhase("Motion Phase", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+36" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "SwordMotionRibbon"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Blend SrcAlpha One
            ZWrite Off
            ZTest LEqual
            Cull Off
            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            TEXTURE2D(_MaskTex); SAMPLER(sampler_MaskTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _MaskTex_ST;
                half4 _GlowColor;
                half _Intensity;
                half _MotionPhase;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                output.color = input.color;
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                half across = saturate(input.uv.x);
                half age = saturate(input.uv.y);
                half oval = SAMPLE_TEXTURE2D(_MaskTex, sampler_MaskTex, half2(across, 0.5h)).r;
                half3 fire = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex,
                    half2(across * 1.35h, age * 1.8h - _MotionPhase)).rgb;
                half taper = pow(saturate(1.0h - age), 0.7h);
                half tip = 1.0h - smoothstep(0.82h, 1.0h, age);
                half textureLight = saturate(max(fire.r, max(fire.g, fire.b)) * 1.35h);
                half alpha = oval * taper * tip * input.color.a * _GlowColor.a;
                clip(alpha - 0.015h);
                half brightness = 0.75h + textureLight * 0.65h;
                return half4(_GlowColor.rgb * _Intensity * brightness, alpha);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
