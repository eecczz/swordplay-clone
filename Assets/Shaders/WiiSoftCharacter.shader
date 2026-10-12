Shader "Swordplay/Wii Soft Character"
{
    Properties
    {
        // Keep the standard URP Simple Lit property layout.  The WebGL player can
        // therefore use URP's battle-tested passes while the style controller
        // continues to drive the familiar Wii-specific controls below.
        [MainTexture] _BaseMap("Texture", 2D) = "white" {}
        [MainColor] _BaseColor("Color", Color) = (1,1,1,1)
        _Cutoff("Alpha Clipping", Range(0,1)) = 0.5
        _Smoothness("Smoothness", Range(0,1)) = 0.34
        _SpecColor("Soft Highlight", Color) = (0.42,0.42,0.42,1)
        _SpecGlossMap("Specular Map", 2D) = "white" {}
        _SmoothnessSource("Smoothness Source", Float) = 0
        _SpecularHighlights("Specular Highlights", Float) = 1
        [HideInInspector] _BumpScale("Scale", Float) = 1
        [NoScaleOffset] _BumpMap("Normal Map", 2D) = "bump" {}
        [HDR] _EmissionColor("Emission Color", Color) = (0,0,0,0)
        [NoScaleOffset] _EmissionMap("Emission Map", 2D) = "white" {}

        // Inspector-facing style values are retained for existing material assets.
        // URP Simple Lit supplies the soft, round specular response on WebGL.
        _ShadowTint("Soft Shadow", Color) = (0.58,0.68,0.70,1)
        _HighlightColor("Head Highlight", Color) = (1,0.985,0.94,1)
        _HighlightSize("Highlight Size", Range(0,1)) = 0.42
        _HighlightStrength("Highlight Strength", Range(0,2)) = 0.8
        _RimStrength("Soft Rim", Range(0,1)) = 0.18

        _Surface("__surface", Float) = 0
        _Blend("__blend", Float) = 0
        _Cull("__cull", Float) = 2
        [ToggleUI] _AlphaClip("__clip", Float) = 0
        [HideInInspector] _SrcBlend("__src", Float) = 1
        [HideInInspector] _DstBlend("__dst", Float) = 0
        [HideInInspector] _SrcBlendAlpha("__srcA", Float) = 1
        [HideInInspector] _DstBlendAlpha("__dstA", Float) = 0
        [HideInInspector] _ZWrite("__zw", Float) = 1
        [HideInInspector] _BlendModePreserveSpecular("_BlendModePreserveSpecular", Float) = 1
        [HideInInspector] _AlphaToMask("__alphaToMask", Float) = 0
        [HideInInspector] _AddPrecomputedVelocity("_AddPrecomputedVelocity", Float) = 0
        [ToggleUI] _ReceiveShadows("Receive Shadows", Float) = 1
        _QueueOffset("Queue offset", Float) = 0

        [HideInInspector] _MainTex("BaseMap", 2D) = "white" {}
        [HideInInspector] _Color("Base Color", Color) = (1,1,1,1)
        [HideInInspector] _Shininess("Smoothness", Float) = 0
        [HideInInspector] _GlossinessSource("GlossinessSource", Float) = 0
        [HideInInspector] _SpecSource("SpecularHighlights", Float) = 0
        [HideInInspector][NoScaleOffset] unity_Lightmaps("unity_Lightmaps", 2DArray) = "" {}
        [HideInInspector][NoScaleOffset] unity_LightmapsInd("unity_LightmapsInd", 2DArray) = "" {}
        [HideInInspector][NoScaleOffset] unity_ShadowMasks("unity_ShadowMasks", 2DArray) = "" {}
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "UniversalMaterialType" = "SimpleLit"
            "IgnoreProjector" = "True"
        }
        LOD 300

        // Reuse URP 17's own low-end passes. These are the same WebGL-tested
        // programs used by Universal Render Pipeline/Simple Lit materials.
        UsePass "Universal Render Pipeline/Simple Lit/ForwardLit"
        UsePass "Universal Render Pipeline/Simple Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Simple Lit/DepthOnly"
        UsePass "Universal Render Pipeline/Simple Lit/DepthNormals"
        UsePass "Universal Render Pipeline/Simple Lit/Meta"
        UsePass "Universal Render Pipeline/Simple Lit/Universal2D"
    }

    Fallback "Universal Render Pipeline/Simple Lit"
}
