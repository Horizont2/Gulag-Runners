// Ground for an arena built out of stretched cubes.
//
// The location in Arena_Demo is primitives: the main floor is a Unity Cube scaled
// 4.41 x 0.24 x 30.16, the upper walkways 2.29 x 0.24 x 8.22, the pillars two-unit
// cylinders. A Unity Cube carries one set of UVs running 0..1 across every face, so a
// tiling texture on that floor is stretched a hundred and twenty-five times harder down
// its length than through its thickness, and no value of _BaseMap_ST fixes it: the SAME
// cube has a 4.41 x 30.16 top and a 4.41 x 0.24 front, and one tiling factor cannot
// serve both. That is the stretching, and it is a property of the mesh, not of the
// texture.
//
// So this shader does not read the mesh's UVs at all. It projects the texture from the
// three world axes and blends by the surface normal, which fixes the texel density in
// METRES — one tile is _TopTile metres across wherever it lands. Scale a block, move it,
// swap it for a bigger one: the ground stays the same ground. Two blocks that happen to
// be the same size also stop looking cloned, because each samples the part of the
// texture its own world position lands on.
//
// Flatness is the other half of the complaint, and a texture alone does not fix it. Four
// things here are what make a box read as a slab of forest floor rather than a painted
// cube, and all four are cheap:
//
//   * the TOP is a different material from the SIDES -- forest floor on top, the earth
//     it sits on down the sides. This is the single biggest cue and it costs nothing:
//     the triplanar blend already had to choose a projection, so choosing a texture at
//     the same time is free;
//   * the cap SPILLS over the top edge by a few centimetres (_Overhang), so you see the
//     thickness of the turf rather than a drawn line where two textures meet;
//   * a dark band sits just UNDER that lip (_LipShade), which is the shadow a real
//     overhang casts and the thing that tells the eye the lip has depth;
//   * and the foot of each block darkens (_FootShade), so it sits in the world instead
//     of floating on it.
//
// Both the lip and the foot are measured in world METRES from the object's own top and
// bottom, taken from the object-to-world matrix and _MeshSize. They have to be: a 24 cm
// walkway and a 1.9 m wall want the same four-centimetre lip, and anything expressed as
// a fraction of the object gives the wall a lip eight times too deep.
//
// _MeshSize is the one thing the shader cannot work out for itself. Unity's Cube is one
// unit tall and its Cylinder is TWO -- the same trap SimCollider.LocalBox documents --
// so a cylinder left at the default would put its lip half a block down. The setup tool
// writes it; see Tools > Gulag Runners > Setup > Texture the arena ground.
Shader "Gulag Runners/Arena Ground"
{
    Properties
    {
        [Header(Top    the cap the player walks on)][Space(4)]
        [MainTexture] _TopMap("Top albedo", 2D) = "white" {}
        [Normal] _TopNormal("Top normal", 2D) = "bump" {}
        // HDR so the tint can LIFT as well as darken. Both ground sets in this pack sit
        // at about 0.13 luma, which is a dark forest floor on a dark earth bank: right
        // in hue and far too close in value, so the cap and the side of a block read as
        // one mass. A plain Color clamps at 1 and can only ever make that worse.
        [HDR][MainColor] _TopColor("Top tint", Color) = (1,1,1,1)
        _TopTile("Top tile size, metres", Float) = 2
        _TopNormalStrength("Top normal strength", Range(0,3)) = 1
        _TopSmoothness("Top smoothness", Range(0,1)) = 0.08

        [Header(Sides    the earth the cap sits on)][Space(4)]
        _SideMap("Side albedo", 2D) = "white" {}
        [Normal] _SideNormal("Side normal", 2D) = "bump" {}
        [HDR] _SideColor("Side tint", Color) = (1,1,1,1)
        _SideTile("Side tile size, metres", Float) = 1.6
        _SideNormalStrength("Side normal strength", Range(0,3)) = 1
        _SideSmoothness("Side smoothness", Range(0,1)) = 0.05

        [Header(Where the cap stops)][Space(4)]
        // Above 1 + _TopBlend there is no cap at all, which is what a post wants: the
        // same wood all the way round. At exactly 1 a flat top still comes out half
        // capped, because that is the midpoint of the blend, not the end of it.
        _TopFacing("How far from straight up still counts as top (over 1: no cap)", Range(-1,2)) = 0.45
        _TopBlend("Softness of that edge", Range(0.01,1)) = 0.22
        _Sharpness("Triplanar sharpness", Range(1,16)) = 6

        [Header(The lip    what makes a slab look like a slab)][Space(4)]
        _Overhang("Cap spills down the side, metres", Range(0,0.5)) = 0.05
        _OverhangFade("How abruptly it stops", Range(0.25,6)) = 1.6
        _LipShade("Shadow under the lip", Range(0,1)) = 0.35
        _LipShadeDepth("How far that shadow reaches, metres", Range(0.01,1)) = 0.12

        [Header(Sitting in the world)][Space(4)]
        _FootShade("Darkening at the foot of a block", Range(0,1)) = 0.25
        _FootShadeHeight("How far up it reaches, metres", Range(0.01,2)) = 0.3

        [Header(The mesh    written by the setup tool)][Space(4)]
        _MeshSize("Mesh size in its own space (Unity Cube 1,1,1; Cylinder 1,2,1)", Vector) = (1,1,1,0)

        [Header(Debug)][Space(4)]
        [Toggle(_DEBUG_BLEND)] _DebugBlend("Show the cap mask instead", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
        }
        LOD 200

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4  _TopColor;
            float  _TopTile;
            half   _TopNormalStrength;
            half   _TopSmoothness;
            half4  _SideColor;
            float  _SideTile;
            half   _SideNormalStrength;
            half   _SideSmoothness;
            half   _TopFacing;
            half   _TopBlend;
            half   _Sharpness;
            float  _Overhang;
            half   _OverhangFade;
            half   _LipShade;
            float  _LipShadeDepth;
            half   _FootShade;
            float  _FootShadeHeight;
            float4 _MeshSize;
            float  _DebugBlend;
        CBUFFER_END

        TEXTURE2D(_TopMap);     SAMPLER(sampler_TopMap);
        TEXTURE2D(_TopNormal);  SAMPLER(sampler_TopNormal);
        TEXTURE2D(_SideMap);    SAMPLER(sampler_SideMap);
        TEXTURE2D(_SideNormal); SAMPLER(sampler_SideNormal);

        // Where this fragment sits between the object's own bottom and its own top, in
        // world metres.
        //
        // Taken from the object-to-world matrix rather than from the vertex's local Y,
        // so a block turned on its side still measures from the edge that is actually
        // uppermost. Row 1 of the matrix maps the mesh's three half-extents onto world
        // Y; summing their absolute contributions is the world half-height of the box,
        // which is the same arithmetic SimCollider.RotatedBounds does on the CPU.
        void ObjectSpan(float3 positionWS, out float belowTop, out float aboveFoot)
        {
            float4x4 m = GetObjectToWorldMatrix();
            float3 halfExtents = _MeshSize.xyz * 0.5;
            float halfHeight = abs(m._m10) * halfExtents.x
                             + abs(m._m11) * halfExtents.y
                             + abs(m._m12) * halfExtents.z;
            float centreY = m._m13;
            belowTop = (centreY + halfHeight) - positionWS.y;
            aboveFoot = positionWS.y - (centreY - halfHeight);
        }

        // How much of the three world projections each face takes. Raising the weights to
        // a power before normalising makes the seam between projections narrow: on the
        // flat faces of a box one projection wins outright, and only the edges mix.
        float3 TriplanarWeights(float3 normalWS)
        {
            float3 w = pow(abs(normalWS), _Sharpness);
            return w / max(w.x + w.y + w.z, 1e-4);
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex GroundVert
            #pragma fragment GroundFrag
            #pragma shader_feature_local_fragment _DEBUG_BLEND
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float  fogCoord   : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings GroundVert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs p = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs n = GetVertexNormalInputs(input.normalOS);

                output.positionCS = p.positionCS;
                output.positionWS = p.positionWS;
                output.normalWS = n.normalWS;
                output.fogCoord = ComputeFogFactor(p.positionCS.z);

                return output;
            }

            half4 GroundFrag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                float3 geometricWS = normalize(input.normalWS);
                float3 weights = TriplanarWeights(geometricWS);

                // One tile is _TopTile (or _SideTile) metres across, whatever the block
                // is scaled to. Dividing world position by the tile size is the whole
                // trick: there is no object scale anywhere in this expression.
                float2 uvX = input.positionWS.zy / max(_SideTile, 1e-3);
                float2 uvY = input.positionWS.xz / max(_TopTile, 1e-3);
                float2 uvZ = input.positionWS.xy / max(_SideTile, 1e-3);

                half4 texX = SAMPLE_TEXTURE2D(_SideMap, sampler_SideMap, uvX);
                half4 texY = SAMPLE_TEXTURE2D(_TopMap, sampler_TopMap, uvY);
                half4 texZ = SAMPLE_TEXTURE2D(_SideMap, sampler_SideMap, uvZ);

                // The two side projections, renormalised between themselves. Without the
                // renormalise a vertical face whose normal points between X and Z would
                // come out dark, because the Y share of the weight has been spent on a
                // texture this branch is not using.
                half sideWeight = max(weights.x + weights.z, 1e-4);
                half3 sideAlbedo = (texX.rgb * weights.x + texZ.rgb * weights.z) / sideWeight;
                sideAlbedo *= _SideColor.rgb;

                half3 topAlbedo = texY.rgb * _TopColor.rgb;

                float belowTop, aboveFoot;
                ObjectSpan(input.positionWS, belowTop, aboveFoot);

                // Up-facing surfaces get the cap. Downward ones never do: the underside
                // of a walkway is earth, and a platform with grass on its belly is the
                // most obviously wrong thing this shader could draw.
                half cap = smoothstep(_TopFacing - _TopBlend, _TopFacing + _TopBlend,
                                      geometricWS.y);

                // The cap spills over the edge. On a vertical face the top projection
                // varies along only one axis, so it smears downwards -- which is what
                // turf hanging over a lip looks like, and it costs no extra sample.
                half lip = saturate(1.0 - belowTop / max(_Overhang, 1e-4));
                lip = pow(lip, _OverhangFade) * (1.0 - cap);

                half capMask = max(cap, lip);
                half3 albedo = lerp(sideAlbedo, topAlbedo, capMask);

#ifdef _DEBUG_BLEND
                // White where the cap wins, black where the earth does. If the lip is not
                // a thin band a few centimetres below every top edge, _MeshSize is wrong
                // for this mesh and nothing else in here will look right either.
                return half4(capMask.xxx, 1);
#endif

                // The shadow an overhang casts on what is under it: darkest right beneath
                // the lip, gone by _LipShadeDepth below it. And the dirt that gathers
                // where a block meets the ground it stands on.
                half under = 1.0 - saturate((belowTop - _Overhang)
                                            / max(_LipShadeDepth, 1e-4));
                half foot = 1.0 - saturate(aboveFoot / max(_FootShadeHeight, 1e-4));

                // The deeper of the two, not both multiplied together. On a 24 cm walkway
                // the lip reaches down further than the foot reaches up, so multiplying
                // would put the whole edge in shadow and lose the one thing the shading
                // is for -- saying which way is up.
                half shade = max(under * _LipShade, foot * _FootShade) * (1.0 - cap);
                albedo *= 1.0 - shade;

                // Whiteout blend: each tangent-space normal is swung into the plane it
                // was projected from before the three are mixed. Adding the world normal
                // to xy and taking abs(z) is what stops the far side of a block reading
                // as though it were lit from inside.
                half3 tnX = UnpackNormalScale(
                    SAMPLE_TEXTURE2D(_SideNormal, sampler_SideNormal, uvX),
                    _SideNormalStrength);
                half3 tnY = UnpackNormalScale(
                    SAMPLE_TEXTURE2D(_TopNormal, sampler_TopNormal, uvY),
                    _TopNormalStrength);
                half3 tnZ = UnpackNormalScale(
                    SAMPLE_TEXTURE2D(_SideNormal, sampler_SideNormal, uvZ),
                    _SideNormalStrength);

                tnX = half3(tnX.xy + geometricWS.zy, abs(tnX.z) * geometricWS.x);
                tnY = half3(tnY.xy + geometricWS.xz, abs(tnY.z) * geometricWS.y);
                tnZ = half3(tnZ.xy + geometricWS.xy, abs(tnZ.z) * geometricWS.z);

                float3 normalWS = normalize(tnX.zyx * weights.x +
                                            tnY.xzy * weights.y +
                                            tnZ.xyz * weights.z);

                InputData lighting = (InputData)0;
                lighting.positionWS = input.positionWS;
                lighting.positionCS = input.positionCS;
                lighting.normalWS = normalWS;
                lighting.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                lighting.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                lighting.fogCoord = input.fogCoord;
                // Ambient from the light probes, not from a lightmap. Arena_Demo has no
                // baked lighting data at all -- its m_LightingDataAsset is the built-in
                // null -- and carrying the lightmap path costs two more interpolators
                // and four shader variants for something nothing in this scene uses. If
                // the arena is ever baked, this is the line that has to grow a
                // SAMPLE_GI and the vertex stage a DECLARE_LIGHTMAP_OR_SH.
                lighting.bakedGI = SampleSH(normalWS);

                // Set, not used here: UniversalFragmentPBR reads it itself when
                // _SCREEN_SPACE_OCCLUSION is on. Applying the factor here as well would
                // square it, and a floor occluded twice reads as a floor in a hole.
                lighting.normalizedScreenSpaceUV =
                    GetNormalizedScreenSpaceUV(input.positionCS);
                lighting.shadowMask = half4(1, 1, 1, 1);

                SurfaceData surface = (SurfaceData)0;
                surface.albedo = albedo;
                surface.metallic = 0.0;
                surface.smoothness = lerp(_SideSmoothness, _TopSmoothness, capMask);
                surface.normalTS = half3(0, 0, 1);
                surface.occlusion = 1.0;
                surface.alpha = 1.0;

                half4 colour = UniversalFragmentPBR(lighting, surface);
                colour.rgb = MixFog(colour.rgb, input.fogCoord);
                return colour;
            }
            ENDHLSL
        }

        // The three passes below draw only depth and the geometric normal, so none of
        // them samples a texture. They are written out rather than borrowed with UsePass
        // because every pass of an SRP-batched shader has to see the same
        // UnityPerMaterial layout, and URP/Lit's is not this one.

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings ShadowVert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);

#if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDirectionWS = normalize(_LightPosition - positionWS);
#else
                float3 lightDirectionWS = _LightDirection;
#endif
                float4 positionCS = TransformWorldToHClip(
                    ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
#if UNITY_REVERSED_Z
                positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
#else
                positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
#endif
                output.positionCS = positionCS;
                return output;
            }

            half4 ShadowFrag(Varyings input) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings DepthVert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half4 DepthFrag(Varyings input) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }

            ZWrite On

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex DepthNormalsVert
            #pragma fragment DepthNormalsFrag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS   : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings DepthNormalsVert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            // The geometric normal, not the triplanar one. This pass feeds screen-space
            // ambient occlusion, which works off the shape of the room rather than the
            // grain of its surfaces -- and buying it six texture samples per pixel to
            // nudge an occlusion term is not a trade worth making on a phone.
            half4 DepthNormalsFrag(Varyings input) : SV_Target
            {
                return half4(NormalizeNormalPerPixel(normalize(input.normalWS)), 0.0);
            }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Lit"
}
