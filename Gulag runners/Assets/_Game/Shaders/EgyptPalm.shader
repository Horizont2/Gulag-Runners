// A palm in the wind, which is two different things moving at two different rates.
//
// WHY NOT THE GRASS SHADER WITH BIGGER NUMBERS. A blade of grass is one thing: anchored at
// the root, free at the tip, one motion. A palm is a mast with a crown on it, and the mast
// and the crown answer the same wind on completely different clocks.
//
//   THE TRUNK is a pole several metres tall, and a pole pushed by slow gusts answers at ITS
//   frequency, not theirs. T = 2*pi*sqrt(L/g), and for the top half of a four-metre palm
//   that is about 2.8 s -- call it a third of a hertz. It bends a little and it bends
//   slowly. Driving a trunk at the speed of the wind is the single thing that makes a tree
//   look like seaweed.
//
//   THE FRONDS are long thin leaves on a flexible rib, and they are an order of magnitude
//   lighter. They answer in well under a second, they move MUCH further, and most of what
//   they do is up and down rather than side to side: a frond catches the wind along its
//   length and lifts, it does not slide sideways on a rail.
//
// So the shader needs to know which vertices are trunk and which are frond, and the mesh
// carries no vertex colour to say. It is told by shape instead, which is what the geometry
// already says plainly: a frond is HIGH and it is FAR OUT from the trunk's axis. The trunk
// is near the axis at every height. Both come free out of the object-space position --
// height over _Height, radius over _FrondReach -- and multiplying them gives a mask that
// is 0 everywhere on the mast and 1 at the frond tips.
//
// THE GUST IS SHARED WITH THE GRASS, on purpose and with the same defaults. A gust that
// walks across the arena along the wind, sampled at (time - distance / speed), is what
// makes a scene look like it is standing in weather rather than like several objects each
// animating. If the palms and the grass gust on different clocks, both of them look wound
// up. If they gust together, the whole place breathes.
//
// Lit as docs/06 asks -- BlinnPhong, ambient from the probes -- and never lightmapped: a
// vertex that moves cannot carry a baked shadow that does not. VegetationSetup clears
// Contribute GI on anything wearing this.

Shader "Gulag Runners/Egypt Palm"
{
    Properties
    {
        [MainTexture] _BaseMap("Base map", 2D) = "white" {}
        [MainColor]   _BaseColor("Base colour", Color) = (1,1,1,1)
        _SpecColor("Specular", Color) = (0.05,0.05,0.05,1)
        _Smoothness("Smoothness", Range(0,1)) = 0.1
        _Cutoff("Alpha cutoff", Range(0,1)) = 0.5

        [Header(The tree)][Space(4)]
        _Height("Height of the palm, metres", Float) = 4.0
        _FrondReach("How far a frond reaches from the trunk, metres", Float) = 1.6
        _TrunkStiffness("Stiffness at the base of the trunk", Range(1,5)) = 2.6

        [Header(Wind)][Space(4)]
        _WindDir("Wind direction (world, xz)", Vector) = (1,0,0.25,0)

        [Header(Trunk    a slow pendulum)][Space(4)]
        _TrunkSway("Lean at the crown, metres", Range(0,1)) = 0.13
        _TrunkSpeed("Trunk rate, Hz (2*pi*sqrt(L/g) says about 0.35)", Range(0,2)) = 0.35

        [Header(Fronds    light and quick)][Space(4)]
        _FrondSway("Frond sway across the wind, metres", Range(0,1)) = 0.12
        _FrondLift("Frond lift, metres -- the bigger half of what a frond does", Range(0,1)) = 0.2
        _FrondSpeed("Frond rate, Hz", Range(0,6)) = 1.5
        _FrondStagger("How much neighbouring fronds lag each other", Range(0,4)) = 1.8

        [Header(Gusts    keep these equal to the grass)][Space(4)]
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
            "VertexAnimation" = "True"   // see BakeSetup: never batched, never lightmapped
            "Queue" = "AlphaTest"
        }
        LOD 200
        Cull Off                        // a frond is a sheet, seen from both sides

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4  _BaseColor;
            half4  _SpecColor;
            half   _Smoothness;
            half   _Cutoff;
            float  _Height;
            float  _FrondReach;
            float  _TrunkStiffness;
            float4 _WindDir;
            float  _TrunkSway;
            float  _TrunkSpeed;
            float  _FrondSway;
            float  _FrondLift;
            float  _FrondSpeed;
            float  _FrondStagger;
            float  _GustSpeed;
            float  _GustLength;
            float  _Lull;
        CBUFFER_END

        float3 PalmRoot()
        {
            return float3(UNITY_MATRIX_M._m03, UNITY_MATRIX_M._m13, UNITY_MATRIX_M._m23);
        }

        // The same gust the grass uses, with the same defaults, so the whole scene is in
        // one weather rather than several.
        float Gust(float3 root, float2 wind, float time)
        {
            float along = dot(root.xz, wind);
            float phase = time * _GustSpeed - along / max(1.0, _GustLength);
            float wave = sin(phase * 6.2831853) * 0.5 + 0.5;
            return lerp(_Lull, 1.0, wave);
        }

        float3 Bend(float3 positionOS)
        {
            float3 root = PalmRoot();
            float2 wind = normalize(_WindDir.xz + float2(1e-5, 0));
            float3 side = float3(wind.x, 0, wind.y);
            float3 across = float3(-wind.y, 0, wind.x);
            float time = _Time.y;

            float up = saturate(positionOS.y / max(0.01, _Height));
            float radius = length(positionOS.xz);

            // Trunk near the axis at any height; frond high AND far out. The product is
            // what keeps the mast still while the crown works.
            float frond = saturate(radius / max(0.01, _FrondReach)) * smoothstep(0.45, 0.95, up);

            float seed = root.x * 1.7 + root.z * 2.3;
            float strength = Gust(root, wind, time);

            // --- the mast: slow, small, stiff at the foot
            float lean = pow(up, _TrunkStiffness);
            float tt = time * _TrunkSpeed * 6.2831853 + seed;
            float trunkSwing = sin(tt) * 0.8 + sin(tt * 1.31 + 0.7) * 0.2;
            float3 move = side * (trunkSwing * _TrunkSway * lean * strength);

            // --- the crown: quick, big, and mostly vertical. The stagger is taken from the
            // vertex's angle about the trunk, so the fronds on one side of a palm lag those
            // on the other instead of flapping as a dish.
            float around = atan2(positionOS.z, positionOS.x);
            float ft = time * _FrondSpeed * 6.2831853 + seed * 1.6 + around * _FrondStagger;
            float flap = sin(ft) * 0.7 + sin(ft * 1.73 + 2.1) * 0.3;

            move += across * (flap * _FrondSway * frond * strength);
            move.y += flap * _FrondLift * frond * strength;

            return mul((float3x3)UNITY_MATRIX_I_M, move);
        }
        ENDHLSL

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex PalmVert
            #pragma fragment PalmFrag
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

            Varyings PalmVert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float3 moved = input.positionOS.xyz + Bend(input.positionOS.xyz);

                VertexPositionInputs pos = GetVertexPositionInputs(moved);
                output.positionCS = pos.positionCS;
                output.positionWS = pos.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.fogCoord = ComputeFogFactor(pos.positionCS.z);
                return output;
            }

            half4 PalmFrag(Varyings input,
                           FRONT_FACE_TYPE cullFace : FRONT_FACE_SEMANTIC) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
                clip(tex.a - _Cutoff);

                float3 normalWS = normalize(input.normalWS) *
                                  IS_FRONT_VFACE(cullFace, 1.0, -1.0);

                InputData lighting = (InputData)0;
                lighting.positionWS = input.positionWS;
                lighting.positionCS = input.positionCS;
                lighting.normalWS = normalWS;
                lighting.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                lighting.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                lighting.fogCoord = input.fogCoord;
                lighting.bakedGI = SampleSH(normalWS);
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
                output.positionCS = TransformObjectToHClip(
                    input.positionOS.xyz + Bend(input.positionOS.xyz));
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
