// The bank of cloud the arena opens out of, and the one thing it is allowed to do that
// the backdrop is not: blend.
//
// WHY THIS IS NOT Backdrop.shader, which the intro used to borrow. That one clips on alpha
// and sits in the AlphaTest queue, because a backdrop has to be OCCLUDED by the arena --
// it is scenery a hundred metres behind the fight and the level has to be able to stand in
// front of it, which needs depth and the opaque pass.
//
// The intro cloud is the exact opposite thing: it is a metre from the lens, it is meant to
// be in front of absolutely everything in the world, and it has to be able to FADE. Drawn
// as a cutout it can do none of that. Every edge is a hard line, so the art had to be a
// paper cut-out with round holes punched in it -- which is what it was -- and the layers
// could only ever be switched off rather than dissolved, because a cutout at 49% alpha is
// not a faint cloud, it is no cloud.
//
// The note in RoundIntroSmoke that argued for the cutout said a blended quad draws after
// the opaque pass and would therefore land over the interface. That is true of a world
// camera's own render and false of this project's: every canvas in the game is
// ScreenSpaceOverlay, which Unity composites after the camera has finished entirely. The
// HUD cannot be covered by a quad in the world whatever queue it is in, so the constraint
// that forced the cut-out never applied here.
//
// No fog, for the same reason Backdrop.shader has none: the arenas run linear fog that is
// fully opaque by about forty metres, and a shader that let it in would have the cloud
// come out the colour of the horizon.
//
// ZWrite OFF and Cull OFF. The layers overlap each other by design -- four banks at four
// distances -- and writing depth would make whichever drew first punch a hole in the ones
// behind it. Sorting is by distance from the camera, which is what the Transparent queue
// does on its own and what the four depths already give it.

Shader "Gulag Runners / Cloud"
{
    Properties
    {
        [MainTexture] _BaseMap ("Cloud", 2D) = "white" {}
        [MainColor]   _BaseColor ("Tint", Color) = (1, 1, 1, 1)
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "Cloud"
            Tags { "LightMode" = "UniversalForward" }

            Cull Off
            ZWrite Off
            ZTest Always
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                half4 cloud = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);

                // The tint's own alpha is the layer's fade, multiplied into the texture's
                // own shape. That is what lets the last bank dissolve instead of being
                // switched off, which a cutout could never do.
                return half4(cloud.rgb * _BaseColor.rgb, cloud.a * _BaseColor.a);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
