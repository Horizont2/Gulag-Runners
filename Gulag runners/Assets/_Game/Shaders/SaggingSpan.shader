// A hanging thing that gives under weight: the cage, its chains, the plank bridges.
//
// Stepping onto a slung cage and having it not move is the moment a game stops feeling
// expensive. The cage in this arena hangs on four chains between two trees, and until
// now it was as rigid as the mountain behind it.
//
// This bends it in the VERTEX stage and nowhere else, which is the only way it can be
// done here. The simulation is deterministic fixed-point and the collision under these
// props was baked out of their colliders once, at load (docs/06). Anything that moved a
// transform would move the art away from the collision on one machine and not the other,
// and two phones would be playing different matches. A vertex shader cannot do that: it
// never writes back, the sim never reads it, and the worst a bug in here can do is look
// wrong.
//
// So the sag is a lie, and the size of the lie matters. Keep _LoadSag near the few
// centimetres a foot sinks into a slung floor. Push it to a quarter of a metre and the
// fighters visibly hover, because their feet are still standing on the box the bake made.
//
// Two shapes, because two different things hang here:
//
//   SPAN (the cage floor, the bridge planks). Fixed at both ends, free in between. It
//   carries a resting droop -- nothing slung is ever flat -- plus the deflection of a
//   taut line under a point load, which is two straight segments meeting under the foot.
//   That is the real shape: a loaded rope is a V, not a curve, and drawing a smooth
//   parabola under a standing fighter is the tell that nobody looked.
//
//   HANGER (the chains). Fixed at the top only. It cannot bend usefully at this size, so
//   it stretches along its own length and leans towards the load, pivoting about the
//   anchor. A chain that stayed vertical while the cage under it dropped would read as
//   the cage having come loose.
//
// The rig -- which way the span runs, how long it is, which way is down in object space
// -- is measured off each mesh by Tools > Gulag Runners > Setup > Set up sagging spans.
// It cannot be guessed: these props came out of several packs with different axis
// conventions, and a chain whose length the shader thinks runs along X stretches
// sideways into the tree.
Shader "Gulag Runners/Sagging Span"
{
    Properties
    {
        [MainTexture] _BaseMap("Base map", 2D) = "white" {}
        [MainColor]   _BaseColor("Base colour", Color) = (1,1,1,1)
        _Smoothness("Smoothness", Range(0,1)) = 0.15
        _Metallic("Metallic", Range(0,1)) = 0

        [Header(Rig    measured by the setup tool)][Space(4)]
        _SpanOrigin("Fixed end, object space", Vector) = (0,0,0,0)
        _SpanAxis("Along the span, object space unit", Vector) = (1,0,0,0)
        _SpanLength("Span length, object units", Float) = 1
        _SagDir("Which way is down, object space", Vector) = (0,-1,0,0)

        [Header(Shape)][Space(4)]
        _RestSag("Droop with nobody on it, metres", Range(0,0.5)) = 0.035
        _LoadSag("Extra under a full load, metres", Range(0,0.5)) = 0.085
        _EndStiffness("How hard the ends hold, 1 is pinned", Range(0,1)) = 0.85
        _LoadSpread("How wide the weight spreads, span fraction", Range(0.02,0.5)) = 0.12

        [Header(Load    written by SaggingSpan each frame)][Space(4)]
        _LoadAt("Where the weight is, 0 to 1 along the span", Range(0,1)) = 0.5
        _LoadAmount("How much of it there is", Range(0,1)) = 0
        _Sway("Sideways lean of a hanger, metres", Range(0,0.5)) = 0

        [Header(Mode)][Space(4)]
        [Toggle(_HANGER)] _Hanger("Hangs from one end (a chain)", Float) = 0
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
            float4 _BaseMap_ST;
            half4  _BaseColor;
            half   _Smoothness;
            half   _Metallic;
            float4 _SpanOrigin;
            float4 _SpanAxis;
            float  _SpanLength;
            float4 _SagDir;
            float  _RestSag;
            float  _LoadSag;
            half   _EndStiffness;
            half   _LoadSpread;
            half   _LoadAt;
            half   _LoadAmount;
            float  _Sway;
            float  _Hanger;
        CBUFFER_END

        // How far along the span a vertex sits, 0 at the fixed end and 1 at the far one.
        float SpanT(float3 positionOS)
        {
            float3 along = positionOS - _SpanOrigin.xyz;
            return saturate(dot(along, normalize(_SpanAxis.xyz + 1e-6))
                            / max(_SpanLength, 1e-4));
        }

        // The deflection of a taut line carrying a point load: two straight segments
        // meeting under the load, normalised to 1 directly beneath it.
        //
        // The smoothstep is the foot, not the rope. A real point load is a corner, but a
        // fighter is not a point -- they are a boot's width of span -- so the corner is
        // rounded over _LoadSpread. Without it the cage creases to a mathematical V under
        // a standing player and the crease slides as they walk.
        float PointLoad(float t, float at)
        {
            float denom = max(at * (1.0 - at), 0.02);
            float tent = min(t * (1.0 - at), at * (1.0 - t)) / denom;

            float near = 1.0 - smoothstep(0.0, max(_LoadSpread, 1e-3), abs(t - at));
            return lerp(saturate(tent), 1.0, near * 0.35);
        }

        // How far a point along a slung span drops, in metres.
        //
        // _EndStiffness mixes between the whole thing sinking and the ends staying put.
        // It has to be a MIX and not a multiplier over the shape, which is what it was
        // first: multiplying the point load by an end-pinning envelope drags the crease
        // away from the foot, and with the weight at 0.15 along the span the deepest
        // point came out at 0.31 -- the cage bending near the middle while the fighter
        // stood near the end. The tent already goes to zero at both ends, so the ends
        // never needed pinning twice.
        float SpanDrop(float t)
        {
            float shaped = _RestSag * 4.0 * t * (1.0 - t)
                         + _LoadSag * _LoadAmount * PointLoad(t, _LoadAt);
            float sunk = _RestSag + _LoadSag * _LoadAmount;
            return lerp(sunk, shaped, _EndStiffness);
        }

        // Everything that happens to one vertex. Returns the offset in OBJECT space.
        float3 SpanOffset(float3 positionOS, out float slope)
        {
            float t = SpanT(positionOS);
            float3 down = normalize(_SagDir.xyz + 1e-6);
            slope = 0.0;

#ifdef _HANGER
            // Held at the top only. It cannot bend at a chain's thickness, so it grows
            // along its own length and leans after the weight it is carrying.
            float drop = (_RestSag + _LoadSag * _LoadAmount) * t;
            float3 lean = normalize(cross(down, float3(0, 0, 1)) + 1e-6);
            return down * drop + lean * (_Sway * t);
#else
            float drop = SpanDrop(t);

            // The surface tilts as it sags, and a plank that bends while its shading
            // stays flat is worse than one that does not bend. Sampling the profile a
            // little either side is the cheapest honest way to get the slope, and it
            // costs nothing a closed form would save at four vertices per plank.
            float step = 0.02;
            slope = (SpanDrop(saturate(t + step)) - drop) / (step * max(_SpanLength, 1e-4));

            return down * drop;
#endif
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex SagVert
            #pragma fragment SagFrag
            #pragma shader_feature_local _HANGER
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS   : TEXCOORD2;
                float  fogCoord   : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings SagVert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float slope;
                float3 moved = input.positionOS.xyz + SpanOffset(input.positionOS.xyz, slope);

                // Tilt the normal by the slope of the sag, about the axis across the span.
                float3 axis = normalize(_SpanAxis.xyz + 1e-6);
                float3 across = normalize(cross(axis, normalize(_SagDir.xyz + 1e-6)) + 1e-6);
                float3 bentOS = normalize(input.normalOS - axis * slope);
                bentOS = normalize(lerp(input.normalOS, bentOS,
                                        saturate(abs(dot(input.normalOS,
                                                         normalize(_SagDir.xyz + 1e-6))))));

                VertexPositionInputs p = GetVertexPositionInputs(moved);
                VertexNormalInputs n = GetVertexNormalInputs(bentOS);

                output.positionCS = p.positionCS;
                output.positionWS = p.positionWS;
                output.normalWS = n.normalWS;
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.fogCoord = ComputeFogFactor(p.positionCS.z);
                return output;
            }

            half4 SagFrag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
                half3 albedo = tex.rgb * _BaseColor.rgb;
                float3 normalWS = normalize(input.normalWS);

                InputData lighting = (InputData)0;
                lighting.positionWS = input.positionWS;
                lighting.positionCS = input.positionCS;
                lighting.normalWS = normalWS;
                lighting.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                lighting.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                lighting.fogCoord = input.fogCoord;
                lighting.bakedGI = SampleSH(normalWS);
                lighting.normalizedScreenSpaceUV =
                    GetNormalizedScreenSpaceUV(input.positionCS);
                lighting.shadowMask = half4(1, 1, 1, 1);

                SurfaceData surface = (SurfaceData)0;
                surface.albedo = albedo;
                surface.metallic = _Metallic;
                surface.smoothness = _Smoothness;
                surface.normalTS = half3(0, 0, 1);
                surface.occlusion = 1.0;
                surface.alpha = 1.0;

                half4 colour = UniversalFragmentPBR(lighting, surface);
                colour.rgb = MixFog(colour.rgb, input.fogCoord);
                return colour;
            }
            ENDHLSL
        }

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
            #pragma shader_feature_local _HANGER
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

                // The SAME displacement as the lit pass. A cage whose shadow stays rigid
                // while the cage itself gives is worse than one that never moved.
                float slope;
                float3 moved = input.positionOS.xyz + SpanOffset(input.positionOS.xyz, slope);

                float3 positionWS = TransformObjectToWorld(moved);
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
            #pragma shader_feature_local _HANGER
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
                float slope;
                float3 moved = input.positionOS.xyz + SpanOffset(input.positionOS.xyz, slope);
                output.positionCS = TransformObjectToHClip(moved);
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
            #pragma shader_feature_local _HANGER
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
                float slope;
                float3 moved = input.positionOS.xyz + SpanOffset(input.positionOS.xyz, slope);
                output.positionCS = TransformObjectToHClip(moved);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            half4 DepthNormalsFrag(Varyings input) : SV_Target
            {
                return half4(NormalizeNormalPerPixel(normalize(input.normalWS)), 0.0);
            }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Lit"
}
