Shader "Swordplay/Sword Slash Ribbon"
{
    Properties
    {
        _MainTex("Multi-Arc Trail Mask", 2D) = "white" {}
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
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
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
                // Crop the transparent generation padding so the connected base reaches
                // the blade and the four tips use the full authored ribbon width.
                half2 trailUv = half2(lerp(0.20h, 0.95h, across),
                    lerp(0.02h, 0.98h, age));
                half4 slashMask = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, trailUv);
                half taper = pow(saturate(1.0h - age), 0.7h);
                half tip = 1.0h - smoothstep(0.82h, 1.0h, age);
                // The generated monochrome texture owns both the broad leading mass and
                // the transparent cuts that separate it into several pointed ribbons.
                half shape = saturate(max(slashMask.a,
                    max(slashMask.r, max(slashMask.g, slashMask.b))));
                half alpha = shape * taper * tip * input.color.a * _GlowColor.a;
                clip(alpha - 0.015h);
                half flowHighlight = 0.9h + 0.1h * sin((age - _MotionPhase) * 12.0h);
                half brightness = (0.9h + shape * 0.35h) * flowHighlight;
                return half4(_GlowColor.rgb * _Intensity * brightness, alpha);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
