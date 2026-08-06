Shader "Swordplay/Wii Soft Character"
{
    Properties
    {
        [MainTexture] _BaseMap("Texture", 2D) = "white" {}
        [MainColor] _BaseColor("Color", Color) = (1,1,1,1)
        _ShadowTint("Soft Shadow", Color) = (0.58,0.68,0.70,1)
        _HighlightColor("Head Highlight", Color) = (1,0.985,0.94,1)
        _HighlightSize("Highlight Size", Range(0,1)) = 0.42
        _HighlightStrength("Highlight Strength", Range(0,2)) = 0.8
        _RimStrength("Soft Rim", Range(0,1)) = 0.18
        [HideInInspector] _SrcBlend("Source Blend", Float) = 1
        [HideInInspector] _DstBlend("Destination Blend", Float) = 0
        [HideInInspector] _ZWrite("Depth Write", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" "RenderPipeline"="UniversalPipeline" }
        LOD 200

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Cull Back
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half4 _ShadowTint;
                half4 _HighlightColor;
                half _HighlightSize;
                half _HighlightStrength;
                half _RimStrength;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                float4 shadowCoord : TEXCOORD3;
                half fogFactor : TEXCOORD4;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                VertexPositionInputs pos = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs nrm = GetVertexNormalInputs(input.normalOS);
                output.positionCS = pos.positionCS;
                output.positionWS = pos.positionWS;
                output.normalWS = nrm.normalWS;
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.shadowCoord = GetShadowCoord(pos);
                output.fogFactor = ComputeFogFactor(pos.positionCS.z);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv) * _BaseColor;
                half3 n = normalize(input.normalWS);
                half3 viewDir = GetWorldSpaceNormalizeViewDir(input.positionWS);
                Light mainLight = GetMainLight(input.shadowCoord);

                half ndl = dot(n, mainLight.direction);
                half wrappedLight = smoothstep(-0.28h, 0.62h, ndl);
                half shadow = lerp(0.58h, 1.0h, mainLight.shadowAttenuation);
                half3 lightResponse = lerp(_ShadowTint.rgb, half3(1,1,1), wrappedLight * shadow);
                half3 ambient = max(SampleSH(n), half3(0.18h, 0.21h, 0.20h));

                half3 halfDir = normalize(mainLight.direction + viewDir);
                half ndh = saturate(dot(n, halfDir));
                half exponent = lerp(10.0h, 62.0h, _HighlightSize);
                half roundHighlight = pow(ndh, exponent) * smoothstep(-0.05h, 0.55h, ndl);
                half topFacing = smoothstep(0.18h, 0.82h, n.y);
                roundHighlight *= lerp(0.58h, 1.15h, topFacing);

                half rim = pow(1.0h - saturate(dot(n, viewDir)), 3.2h) * smoothstep(-0.2h, 0.65h, n.y);
                half3 color = tex.rgb * (ambient * 0.48h + lightResponse * mainLight.color * 0.78h);
                color += _HighlightColor.rgb * roundHighlight * _HighlightStrength * mainLight.color;
                color += _HighlightColor.rgb * rim * _RimStrength;
                color = MixFog(color, input.fogFactor);
                return half4(color, tex.a);
            }
            ENDHLSL
        }

        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
    }
    Fallback "Universal Render Pipeline/Lit"
}
