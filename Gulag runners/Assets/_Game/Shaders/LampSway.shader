// A hung lamp moving in the air, and a room's worth of them not moving together.
//
// Ten lanterns hang down the mine and every one of them was welded in place. A still
// lamp is not neutral: it is the thing that tells you the room is a model. The fire
// inside them already flickers (TorchLight), so the light on the wall moves while the
// lamp that is making it does not, which is worse than neither.
//
// WHAT MOVES. A lamp hangs from a hook and turns about THAT, not about its own pivot --
// PP_Lantern_02's pivot is at its foot and its hook is 0.575 m up, so turning about the
// pivot would swing the hook and leave the body still, which is a lamp screwed to a
// wall by its bottom. So the whole mesh is rotated about the hook, rigidly: a lantern is
// iron, it does not bend, and the only thing that gives is the nail it hangs on.
//
// HOW FAST. At its own period and nothing else. A hanging thing is a pendulum, and a
// pendulum pushed by slow gusts answers at ITS frequency, not theirs -- T = 2*pi*sqrt(L/g),
// and at the 0.35 m from this lantern's hook to its middle that is 1.19 s. Driving it at
// the speed of the wind instead is the single thing that makes swaying scenery look like
// seaweed.
//
// WHY THEY ARE NOT IN STEP, which is the whole of the request and worth being exact about,
// because there are three different answers and only using all three looks like weather:
//
//   PHASE. Each lamp starts somewhere else in its own swing, out of its own position in
//   the world. Two lamps a metre apart are at unrelated points of the same cycle.
//
//   PERIOD. No two are quite the same lamp, so each one's period is jittered a tenth
//   either way. Lamps that start together drift apart within a few swings and never
//   re-synchronise -- which a shared period would, however the phases began.
//
//   THE GUST TRAVELS. This is the one that reads as wind rather than as ten independent
//   wobbles: a gust is a thing that moves THROUGH a room. Its strength is sampled at
//   (time - x / speed), so it arrives at one end of the arena and walks to the other,
//   and what the eye gets is a wave passing down the row. Lamps near each other gust
//   nearly together, which is the part that makes it a draught and not a random number
//   per lamp.
//
// And no component, no property block, nothing to wire per lamp. It is a function of the
// clock and of where the lamp stands, so a lantern dropped into a scene sways the moment
// it is placed, and ten of them never need to be told about each other.
//
// The light does NOT follow, and does not need to: the flame sits 0.21 m above the foot
// and the hook 0.575 m, so even at the full six degrees the glass moves two centimetres,
// which is less than the drift TorchLight already gives it.
Shader "Gulag Runners / Lamp Sway"
{
    Properties
    {
        [MainTexture] _BaseMap("Base map", 2D) = "white" {}
        [MainColor]   _BaseColor("Base colour", Color) = (1,1,1,1)
        _Smoothness("Smoothness", Range(0,1)) = 0.5
        _Metallic("Metallic", Range(0,1)) = 0

        [Header(Where it hangs)][Space(4)]
        _Pivot("The hook, object space", Vector) = (0, 0.575, 0)

        [Header(How it swings)][Space(4)]
        // Seconds per swing: 2 pi sqrt(L/g), which is 1.19 s at this lantern's 0.35 m
        // from hook to middle. It is the lamp's own number, not the wind's.
        _Period("Seconds per swing", Range(0.3, 5)) = 1.19
        _StillDegrees("How far it moves in still air", Range(0, 10)) = 1.2
        _GustDegrees("And at the top of a gust", Range(0, 20)) = 5
        // Towards the camera as well as along the arena, so it traces an ellipse. A flat
        // arc reads as a hinge rather than as something hanging on one point.
        _CrossSwing("Share of the swing that goes across", Range(0, 1)) = 0.35

        [Header(The wind)][Space(4)]
        _GustPeriod("Seconds between gusts", Range(1, 30)) = 7
        _GustTravel("How fast a gust crosses the arena, metres per second", Range(0.5, 40)) = 6
        _Jitter("How much each lamp's own period differs from the rest", Range(0, 0.5)) = 0.12
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
            float4 _Pivot;
            float  _Period;
            float  _StillDegrees;
            float  _GustDegrees;
            float  _CrossSwing;
            float  _GustPeriod;
            float  _GustTravel;
            float  _Jitter;
        CBUFFER_END

        #define TAU 6.2831853

        /// Rodrigues, about a unit axis through the origin.
        float3 Turn(float3 v, float3 axis, float angle)
        {
            float s, c;
            sincos(angle, s, c);
            return v * c + cross(axis, v) * s + axis * dot(axis, v) * (1.0 - c);
        }

        /// Everything that happens to one vertex, as an offset in OBJECT space.
        ///
        /// Worked out per vertex rather than per object because a shader has nowhere
        /// else to put it, and it is a handful of sines: the cost of a lamp that moves
        /// is lower than the cost of looking at one that does not.
        float3 SwayOffset(float3 positionOS)
        {
            float3 pivotOS = _Pivot.xyz;
            float3 hookWS = TransformObjectToWorld(pivotOS);

            // This lamp's own number, out of where it hangs. Stable, because a lamp on a
            // wall does not move, and different for every one of them without anybody
            // seeding anything.
            //
            // All THREE axes, and the arena is the reason. The first version took x and
            // z, on the assumption that lamps are spread along a floor; the mine hangs
            // them in pairs, one storey above another, at the same x and z. Three of
            // its five pairs came out with the same seed, which is the same phase and
            // the same period -- two lamps swinging as one, in a shader written to stop
            // exactly that.
            float seed = frac(sin(dot(hookWS, float3(12.9898, 78.233, 37.719))) * 43758.5453);

            float period = max(_Period, 0.05) * (1.0 + _Jitter * (seed * 2.0 - 1.0));
            float w = TAU / period;
            float phase = seed * TAU;

            // The gust, sampled where this lamp stands: it leaves one end of the arena
            // and walks to the other at _GustTravel. Two waves at unrelated rates, so
            // gusts arrive irregularly rather than on a beat anybody can count.
            float g = TAU / max(_GustPeriod, 0.1);
            float travel = hookWS.x / max(_GustTravel, 0.1);
            float gust = 0.5 + 0.5 * sin(_Time.y * g - travel);
            gust *= 0.65 + 0.35 * sin(_Time.y * g * 0.41 + 1.7 - travel * 0.5);

            float degrees = _StillDegrees + (_GustDegrees - _StillDegrees) * saturate(gust);
            float amp = radians(degrees);

            // Along the arena, and a smaller, slightly faster one across it: together an
            // ellipse, which is what anything hanging on one point actually does. A flat
            // arc reads as a hinge.
            float along = amp * sin(_Time.y * w + phase);
            float across = amp * _CrossSwing * sin(_Time.y * w * 1.03 + phase + 1.5708);

            // The world's axes in the lamp's own space, so a lantern turned to face any
            // wall still swings along the arena and not along its own model's X.
            float3 zOS = normalize(mul((float3x3)unity_WorldToObject, float3(0, 0, 1)) + 1e-6);
            float3 xOS = normalize(mul((float3x3)unity_WorldToObject, float3(1, 0, 0)) + 1e-6);

            float3 arm = positionOS - pivotOS;
            arm = Turn(arm, zOS, along);
            arm = Turn(arm, xOS, across);
            return (pivotOS + arm) - positionOS;
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex SwayVert
            #pragma fragment SwayFrag
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

            Varyings SwayVert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float3 moved = input.positionOS.xyz + SwayOffset(input.positionOS.xyz);

                // The normal turns with the lamp. Taken as the swing of a point one unit
                // off the pivot along the normal, which is the same rotation without
                // having to hand the angles back out of SwayOffset.
                float3 tip = _Pivot.xyz + input.normalOS;
                float3 bentOS = normalize(tip + SwayOffset(tip) - (_Pivot.xyz + SwayOffset(_Pivot.xyz)));

                VertexPositionInputs p = GetVertexPositionInputs(moved);
                VertexNormalInputs n = GetVertexNormalInputs(bentOS);

                output.positionCS = p.positionCS;
                output.positionWS = p.positionWS;
                output.normalWS = n.normalWS;
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.fogCoord = ComputeFogFactor(p.positionCS.z);
                return output;
            }

            half4 SwayFrag(Varyings input) : SV_Target
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

                // The SAME move as the lit pass. A lamp whose shadow stays nailed to the
                // wall while the lamp swings is worse than one that never moved.
                float3 moved = input.positionOS.xyz + SwayOffset(input.positionOS.xyz);

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
                float3 moved = input.positionOS.xyz + SwayOffset(input.positionOS.xyz);
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
                float3 moved = input.positionOS.xyz + SwayOffset(input.positionOS.xyz);
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
