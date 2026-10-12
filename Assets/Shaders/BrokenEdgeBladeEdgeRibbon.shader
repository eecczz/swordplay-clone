Shader "Swordplay/Broken Edge Blade Edge Ribbon"
{
    Properties
    {
        [HDR] _GlowColor("Outline Flame Color", Color) = (0.03,1.5,2.5,0.94)
        _Intensity("Intensity", Range(0,3)) = 1.4
        _Writhe("Living Edge Motion", Range(0,2)) = 1.28
        _FlameSpeed("Flame Speed", Range(0,12)) = 6.2
        _FlameDetail("Flame Detail", Range(1,40)) = 16
        _OutlineWidth("Outline Width", Float) = 0.1
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+34" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "BladeEdgeFire"
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

            CBUFFER_START(UnityPerMaterial)
                half4 _GlowColor;
                half _Intensity;
                half _Writhe;
                half _FlameSpeed;
                half _FlameDetail;
                float _OutlineWidth;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half pulse : TEXCOORD1;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                half phase = input.uv.x * max(6.0h, _FlameDetail) * 1.37h - _Time.y * _FlameSpeed;
                half coarse = 0.5h + 0.5h * sin(phase);
                half fine = 0.5h + 0.5h * sin(phase * 2.31h + 1.7h);
                half motion = (coarse * 0.68h + fine * 0.32h - 0.5h) * _Writhe;
                float3 positionOS = input.positionOS.xyz;
                positionOS += normalize(input.normalOS) * (_OutlineWidth * 0.22h * motion * input.uv.y);
                output.positionCS = TransformObjectToHClip(positionOS);
                output.uv = input.uv;
                output.pulse = coarse * 0.7h + fine * 0.3h;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half rootSoftness = smoothstep(0.0h, 0.16h, input.uv.y);
                half outerSoftness = 1.0h - smoothstep(0.82h, 1.0h, input.uv.y);
                half alpha = _GlowColor.a * lerp(0.72h, 1.0h, rootSoftness) * lerp(1.0h, 0.74h, 1.0h - outerSoftness);
                half brightness = _Intensity * (0.86h + input.pulse * 0.28h);
                return half4(_GlowColor.rgb * brightness, alpha);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
