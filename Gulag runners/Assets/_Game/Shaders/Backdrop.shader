// The parallax layers behind an arena, and the one thing they must not do: take the fog.
//
// There is almost nothing in this shader. It samples one texture, clips on alpha and
// returns the colour. No lighting, no shadows, no vertex work beyond the transform. For
// flat silhouettes 24 to 128 metres away that is the whole job, and docs/06 asks for unlit
// on mobile anyway.
//
// WHY IT IS NOT "Universal Render Pipeline/Unlit", which does the same thing and ships with
// the engine: fog. Every arena runs LINEAR fog that is fully opaque somewhere between 34
// and 46 metres --
//
//     Arena_Mine             9.96 .. 34
//     Arena_Forest           9    .. 38
//     Arena_Nordic fortress  11.22 .. 34
//     loc3_new              14    .. 46
//
// -- and the three backdrop layers sit at 24, 55 and 128. So under any stock shader the
// middle and far layers are not merely tinted, they are ONE HUNDRED PER CENT fog colour:
// the whole backdrop collapses into a single flat wall the colour of the horizon, and the
// parallax that the depths were chosen to produce becomes invisible because there is
// nothing left to see move.
//
// That cannot be fixed on the material. URP's Unlit has `multi_compile_fog` and calls
// MixFog, and the FOG_LINEAR keyword is set GLOBALLY by the engine out of the scene's
// RenderSettings -- there is no per-material switch to turn it off. The only way to opt a
// surface out of fog is for its shader not to apply any, which is what this one does by
// simply never including it.
//
// Losing the fog costs the layers nothing, because they already carry the effect fog would
// have given them: each layer is painted a flat colour lifted towards the arena's own haze
// by its distance (make_backdrops.py). That is the same depth cue, decided by an artist per
// layer instead of by a curve per pixel, and free.
//
// Cull is OFF deliberately. The quads are built facing the camera, so one side is all that
// is ever rasterised and culling would save nothing measurable -- but if a quad is ever
// built with the opposite winding, back-face culling makes the backdrop VANISH, and an
// invisible backdrop is a much worse failure than a cull test nobody needed.

Shader "Gulag Runners / Backdrop"
{
    Properties
    {
        [MainTexture] _BaseMap ("Layer", 2D) = "white" {}
        [MainColor]   _BaseColor ("Tint", Color) = (1, 1, 1, 1)
        _Cutoff ("Alpha cutoff", Range(0, 1)) = 0.5
    }

    SubShader
    {
        // AlphaTest, not Transparent. The layers are opaque geometry with holes in it, so
        // the arena occludes them by depth like any other solid thing. In the Transparent
        // queue they would draw after the opaque pass -- on top of the fight.
        Tags
        {
            "RenderType" = "TransparentCutout"
            "Queue" = "AlphaTest"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "Backdrop"
            Tags { "LightMode" = "UniversalForward" }

            Cull Off
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment
            #pragma target 2.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half _Cutoff;
            CBUFFER_END

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return output;
            }

            half4 Fragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half4 painted = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv)
                                * _BaseColor;

                // The art is strictly 0 or 255 alpha (make_backdrops.py refuses to write
                // anything else), so this is a hard edge, not a feathered one.
                clip(painted.a - _Cutoff);

                return half4(painted.rgb, 1.0h);
            }
            ENDHLSL
        }
    }

    // If the URP shader library is ever unavailable, a stock unlit still shows the art --
    // fogged, but visible, which beats a magenta rectangle.
    Fallback "Universal Render Pipeline/Unlit"
}
