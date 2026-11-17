// Grass that moves, and a row of it that does not move together.
//
// The Egypt pack's grass is welded in place, and still grass in a night desert is not
// neutral: it is the thing that tells you the arena is a model. Everything else in that
// scene already moves -- the sand drifts, the lamps flicker -- so the one patch of growing
// thing standing perfectly rigid is what the eye lands on.
//
// WHAT MOVES. A blade is anchored at the root and free at the tip, so the bend has to be
// zero at the bottom and all of it at the top. The mask is the vertex's height in OBJECT
// space over _Height, raised to _Stiffness: a power curve rather than a straight ramp,
// because a blade is not a hinge. Straight-line bending makes the whole clump lean like a
// signpost; the curve keeps the bottom third nearly still and spends the motion where the
// blade is thin.
//
// The sway is applied ALONG the wind, not sideways to it, with a smaller cross component
// so the tip traces a flattened figure rather than sliding back and forth on a rail. Two
// frequencies, not one: a single sine is a metronome, and the eye reads a metronome as
// animation. The second is deliberately not a multiple of the first.
//
// WHY THEY ARE NOT IN STEP, which is most of the work:
//
//   PHASE. Each clump starts somewhere else in its own cycle, out of its position in the
//   WORLD -- so two clumps a metre apart are at unrelated points of the same motion, and a
//   clump dropped anywhere in the scene is already out of step with its neighbours without
//   being told about them.
//
//   PERIOD. Jittered a tenth either way from the same position. Clumps that happen to
//   start together drift apart within a few seconds and never re-synchronise, which a
//   shared period would, however the phases began.
//
//   THE GUST TRAVELS. This is the one that reads as weather rather than as a field of
//   independent wobbles: a gust is a thing that moves THROUGH a place. Its strength is
//   sampled at (time - x / speed), so it arrives at one end of the arena and walks to the
//   other. Grass near each other gusts nearly together; grass thirty metres apart does not.
//
// And no component, no script, nothing to wire per clump. It is a function of the clock and
// of where the grass stands, so a blade dropped into a scene moves the moment it is placed.
//
// Lit the way docs/06 asks for -- UniversalFragmentBlinnPhong, ambient out of the probes --
// and NOT lightmapped: a vertex that moves cannot carry a baked shadow that does not.
// VegetationSetup clears Contribute GI on anything wearing this.

Shader "Gulag Runners/Egypt Grass"
{
    Properties
    {
        [MainTexture] _BaseMap("Base map", 2D) = "white" {}
        [MainColor]   _BaseColor("Base colour", Color) = (1,1,1,1)
        _SpecColor("Specular", Color) = (0.05,0.05,0.05,1)
        _Smoothness("Smoothness", Range(0,1)) = 0.08
        _Cutoff("Alpha cutoff", Range(0,1)) = 0.5

        [Header(The plant)][Space(4)]
        _Height("Height of the tallest blade, metres", Float) = 0.55
        _Stiffness("Stiffness at the root", Range(1,5)) = 2.2

        [Header(Wind)][Space(4)]
        _WindDir("Wind direction (world, xz)", Vector) = (1,0,0.25,0)
        _Sway("Sway along the wind, metres at the tip", Range(0,0.5)) = 0.085
        _Cross("Sway across it", Range(0,0.5)) = 0.035
        _Speed("How fast a blade answers", Range(0,6)) = 1.9

        [Header(Gusts)][Space(4)]
        _GustSpeed("How fast a gust crosses the arena", Range(0,3)) = 0.45
        _GustLength("Metres between gust crests", Range(1,40)) = 11
        _Lull("How still it goes between gusts", Range(0,1)) = 0.35
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "TransparentCutout"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "AlphaTest"
        }
        LOD 200
        Cull Off                        // a blade is a sheet; its back is still the blade

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4  _BaseColor;
            half4  _SpecColor;
            half   _Smoothness;
            half   _Cutoff;
            float  _Height;
            float  _Stiffness;
            float4 _WindDir;
            float  _Sway;
            float  _Cross;
            float  _Speed;
            float  _GustSpeed;
            float  _GustLength;
            float  _Lull;
        CBUFFER_END

        // Where this plant stands. Everything that has to differ between one clump and the
        // next is derived from it, so nothing has to be authored per clump.
        float3 PlantRoot()
        {
            return float3(UNITY_MATRIX_M._m03, UNITY_MATRIX_M._m13, UNITY_MATRIX_M._m23);
        }

        // A gust walking across the arena along the wind. Sampled at (time - distance /
        // speed), which is what makes it a thing that travels rather than a brightness
        // everything shares. Never fully still and never fully flat: _Lull sets the floor.
        float Gust(float3 root, float2 wind, float time)
        {
            float along = dot(root.xz, wind);
            float phase = time * _GustSpeed - along / max(1.0, _GustLength);
            float wave = sin(phase * 6.2831853) * 0.5 + 0.5;
            return lerp(_Lull, 1.0, wave);
        }

        float3 Bend(float3 positionOS, out float mask)
        {
            float3 root = PlantRoot();
            float2 wind = normalize(_WindDir.xz + float2(1e-5, 0));
            float time = _Time.y;

            // Height up the blade, zero at the root. Powered, so the bottom barely moves.
            mask = saturate(positionOS.y / max(0.01, _Height));
            float lean = pow(mask, _Stiffness);

            // Its own phase and its own period, both out of where it stands. Two clumps a
            // metre apart share neither.
            float seed = root.x * 1.7 + root.z * 2.3;
            float rate = _Speed * (1.0 + 0.1 * sin(seed * 3.1));

            float t = time * rate + seed;
            float swing = sin(t) * 0.75 + sin(t * 1.37 + 1.1) * 0.25;   // not a metronome

            float strength = Gust(root, wind, time) * lean;

            float3 world = float3(wind.x, 0, wind.y) * (swing * _Sway * strength)
                         + float3(-wind.y, 0, wind.x) * (sin(t * 0.83 + 2.0) * _Cross * strength);

            // The push is in world space; the vertex is in object space.
            return mul((float3x3)UNITY_MATRIX_I_M, world);
        }
        ENDHLSL

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex GrassVert
            #pragma fragment GrassFrag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
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

            Varyings GrassVert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float mask;
                float3 moved = input.positionOS.xyz + Bend(input.positionOS.xyz, mask);

                VertexPositionInputs pos = GetVertexPositionInputs(moved);
                output.positionCS = pos.positionCS;
                output.positionWS = pos.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.fogCoord = ComputeFogFactor(pos.positionCS.z);
                return output;
            }

            half4 GrassFrag(Varyings input,
                            FRONT_FACE_TYPE cullFace : FRONT_FACE_SEMANTIC) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
                clip(tex.a - _Cutoff);

                // A blade is a sheet. Its back has to be lit as a back, not as a hole.
                float3 normalWS = normalize(input.normalWS) *
                                  IS_FRONT_VFACE(cullFace, 1.0, -1.0);

                InputData lighting = (InputData)0;
                lighting.positionWS = input.positionWS;
                lighting.positionCS = input.positionCS;
                lighting.normalWS = normalWS;
                lighting.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                lighting.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                lighting.fogCoord = input.fogCoord;
                lighting.bakedGI = SampleSH(normalWS);     // probes: this thing moves
                lighting.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                lighting.shadowMask = half4(1, 1, 1, 1);

                SurfaceData surface = (SurfaceData)0;
                surface.albedo = tex.rgb * _BaseColor.rgb;
                surface.specular = _SpecColor.rgb;
                surface.smoothness = _Smoothness;
                surface.normalTS = half3(0, 0, 1);
                surface.occlusion = 1.0;
                surface.alpha = 1.0;

                half4 colour = UniversalFragmentBlinnPhong(lighting, surface);
                colour.rgb = MixFog(colour.rgb, input.fogCoord);
                return colour;
            }
            ENDHLSL
        }

        // Depth only. docs/06 runs zero realtime shadows, so there is no ShadowCaster here
        // -- but a depth pass costs almost nothing and keeps the grass from confusing any
        // effect that reads depth.
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

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            struct DepthIn
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct DepthOut
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            DepthOut DepthVert(DepthIn input)
            {
                DepthOut output = (DepthOut)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                float mask;
                float3 moved = input.positionOS.xyz + Bend(input.positionOS.xyz, mask);
                output.positionCS = TransformObjectToHClip(moved);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return output;
            }

            half4 DepthFrag(DepthOut input) : SV_Target
            {
                clip(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).a - _Cutoff);
                return 0;
            }
            ENDHLSL
        }
    }

    Fallback "Universal Render Pipeline/Simple Lit"
}
