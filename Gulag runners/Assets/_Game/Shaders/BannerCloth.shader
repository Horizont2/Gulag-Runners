// Cloth that hangs from a rail and is moved by a draught.
//
// The tell of a fake cloth shader is a single sine: every point of the banner reaches
// its extreme on the same frame, the hem never lags the rail, and the lighting stays
// flat while the surface visibly ripples. All three are fixed here and none of them is
// expensive on a 224-vertex mesh:
//
//   * the wave TRAVELS down the cloth, so the hem lags the rail by a real phase;
//   * two waves at unrelated frequencies, so the loop is long enough not to be heard;
//   * a gust envelope that swells and dies, so between gusts the cloth settles instead
//     of metronoming;
//   * a per-instance phase taken from the object's world position, so two banners on
//     one wall never move together;
//   * edges flutter more than the middle, which is where a real banner looks alive;
//   * the normal is rebuilt from the displaced surface, so the torch catches the
//     ripples instead of lighting a flat sheet that happens to be moving;
//   * the shadow caster displaces identically, so the shadow sways with the cloth;
//   * and the sheet is pulled towards its anchor as it bends, so bending does not
//     stretch it.
//
// Nothing here is authored in the shader: the hang axis, the anchor and the length are
// measured off the mesh by Tools > Gulag Runners > Set up banner cloth, because this
// mesh came out of Blender and whether the axis conversion was baked into the vertices
// or left on the transform is not something the shader can know.
Shader "Gulag Runners/Banner Cloth"
{
    Properties
    {
        [MainTexture] _BaseMap("Base map", 2D) = "white" {}
        [MainColor]   _BaseColor("Base colour", Color) = (1,1,1,1)
        _SpecColor("Specular", Color) = (0.1,0.1,0.1,1)
        _Smoothness("Smoothness", Range(0,1)) = 0.12

        [Header(Rig    measured by the setup tool)][Space(4)]
        _HangAxis("Hang axis (object space, anchor to hem)", Vector) = (0,-1,0,0)
        _WidthAxis("Width axis (object space)", Vector) = (1,0,0,0)
        _AnchorPoint("Anchor point (object space)", Vector) = (0,0,0,0)
        _HangLength("Hang length", Float) = 1
        _HalfWidth("Half width", Float) = 0.5

        [Header(Wind)][Space(4)]
        _WindDir("Wind direction (world)", Vector) = (1,0,0.25,0)
        _Sway("Sway, as a share of the hang length", Range(0,0.5)) = 0.055
        _Billow("Billow out of the sheet, same share", Range(0,0.5)) = 0.035
        _Swing("Slow swing of the whole sheet", Range(0,0.5)) = 0.03

        [Header(Shape of the motion)][Space(4)]
        _Stiffness("Stiffness near the rail", Range(0.5,4)) = 1.8
        _Waves("Wavelengths down the cloth", Range(0.2,4)) = 1.15
        _Skew("Skew across the width", Range(0,6)) = 1.6
        _Speed("Ripple speed", Range(0,6)) = 1.25
        _SwingSpeed("Swing speed", Range(0,3)) = 0.33
        _EdgeFlutter("Extra flutter at the free edges", Range(0,2)) = 0.65
        _KeepLength("Keep the cloth from stretching", Range(0,1)) = 1

        [Header(Gusts)][Space(4)]
        _GustSpeed("Gust speed", Range(0,2)) = 0.42
        _Lull("How still it goes between gusts", Range(0,1)) = 0.3

        [Header(Surface)][Space(4)]
        _NormalStrength("How much the ripples bend the light", Range(0,2)) = 1
        [Toggle(_DEBUG_MASK)] _DebugMask("Show the hang mask instead", Float) = 0
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
            half4  _SpecColor;
            half   _Smoothness;
            float4 _HangAxis;
            float4 _WidthAxis;
            float4 _AnchorPoint;
            float  _HangLength;
            float  _HalfWidth;
            float4 _WindDir;
            float  _Sway;
            float  _Billow;
            float  _Swing;
            float  _Stiffness;
            float  _Waves;
            float  _Skew;
            float  _Speed;
            float  _SwingSpeed;
            float  _EdgeFlutter;
            float  _KeepLength;
            float  _GustSpeed;
            float  _Lull;
            float  _NormalStrength;
            float  _DebugMask;
        CBUFFER_END

        #define GR_TAU 6.2831853

        // Where this banner is in its own cycle. Taken from the object's world origin so
        // two banners hung on the same wall are never in step, and so the offset survives
        // a scene reload -- a random() per instance would re-roll and look different every
        // time the menu is opened.
        float ClothSeed()
        {
            float3 pivotWS = float3(GetObjectToWorldMatrix()._m03,
                                    GetObjectToWorldMatrix()._m13,
                                    GetObjectToWorldMatrix()._m23);
            return frac(dot(pivotWS, float3(0.3183, 0.5771, 0.7549)) + 0.1) * GR_TAU;
        }

        // Wind that swells and dies. Two slow waves at an irrational ratio, so the
        // envelope does not repeat on any interval a player sits through, and a floor so
        // the cloth never freezes solid between gusts.
        float ClothGust(float seed)
        {
            float t = _Time.y * _GustSpeed;
            float g = 0.62 * sin(t * 0.73 + seed) + 0.38 * sin(t * 1.193 + seed * 2.17);
            return lerp(_Lull, 1.0, saturate(g * 0.5 + 0.5));
        }

        // One sample of the cloth's offset. Called twice per vertex, a hair apart along
        // the hang, so the normal can be rebuilt from the surface that actually ends up
        // on screen rather than from the one the mesh was authored with.
        float3 ClothOffset(float along, float across, float edge, float seed, float gust,
                           float3 windOS, float3 sheetN)
        {
            float t = _Time.y * _Speed + seed;
            float phase = along * GR_TAU * _Waves + across * _Skew;

            float w1 = sin(t - phase);
            float w2 = sin(t * 1.618 - phase * 2.13 + 1.7) * 0.42;
            float ripple = (w1 + w2) * 0.704;

            // Nothing at the rail, everything at the hem, and not in a straight line:
            // a banner is stiff where it is nailed and loose where it is not.
            float hang = pow(saturate(along), _Stiffness);
            float flutter = 1.0 + _EdgeFlutter * edge * edge;
            float amount = hang * gust * flutter * _HangLength;

            float swing = sin(_Time.y * _SwingSpeed + seed * 0.61);

            float3 offset = windOS * ((ripple * _Sway + swing * _Swing) * amount);
            offset += sheetN * (ripple * _Billow * amount);
            return offset;
        }

        struct ClothVertex
        {
            float3 positionOS;
            float3 normalOS;
        };

        // Displaces one vertex and hands back the normal of the surface it landed on.
        ClothVertex Cloth(float3 positionOS, float3 normalOS)
        {
            float3 hangAxis = normalize(_HangAxis.xyz + float3(0, -1e-6, 0));
            float3 widthAxis = normalize(_WidthAxis.xyz + float3(1e-6, 0, 0));
            float3 sheetN = normalize(cross(hangAxis, widthAxis));

            float len = max(_HangLength, 1e-4);
            float3 fromAnchor = positionOS - _AnchorPoint.xyz;

            float along = saturate(dot(fromAnchor, hangAxis) / len);
            float across = dot(fromAnchor, widthAxis);
            float edge = saturate(abs(across) / max(_HalfWidth, 1e-4));

            float seed = ClothSeed();
            float gust = ClothGust(seed);

            float3 windOS = normalize(TransformWorldToObjectDir(_WindDir.xyz, true)
                                      + float3(1e-6, 0, 0));
            // Only the part of the wind across the cloth moves it. A draught blowing
            // straight down a hanging banner does not swing it, it just presses it flat.
            windOS = normalize(windOS - hangAxis * dot(windOS, hangAxis) + float3(1e-6, 0, 0));

            float3 here = ClothOffset(along, across, edge, seed, gust, windOS, sheetN);

            // A second sample a little further down the cloth. The vector between the two
            // displaced points IS the new tangent, which is the only honest way to get a
            // normal out of a vertex displacement.
            float step = len * 0.04;
            float3 aheadOS = positionOS + hangAxis * step;
            float alongA = saturate(along + step / len);
            float3 there = ClothOffset(alongA, across, edge, seed, gust, windOS, sheetN);

            float3 tangent = normalize((aheadOS + there) - (positionOS + here)
                                       + float3(0, 0, 1e-6));

            ClothVertex o;
            o.positionOS = positionOS + here;

            // Bending a sheet that keeps its length pulls the hem up. Without this the
            // cloth visibly grows as it swings, which reads as rubber rather than linen.
            float slack = dot(here, here) / (2.0 * len);
            o.positionOS -= hangAxis * (slack * _KeepLength);

            float3 bent = normalize(cross(widthAxis, tangent) + float3(0, 0, 1e-6));
            bent *= (dot(bent, normalOS) < 0.0) ? -1.0 : 1.0;
            o.normalOS = normalize(lerp(normalOS, bent, saturate(_NormalStrength)));
            return o;
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            // A banner is a sheet: both faces are the banner, and the back of it has to
            // be lit as the back rather than as a hole.
            Cull Off

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex ClothVert
            #pragma fragment ClothFrag
            #pragma shader_feature_local _DEBUG_MASK
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
                float  mask       : TEXCOORD4;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings ClothVert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                ClothVertex cloth = Cloth(input.positionOS.xyz, input.normalOS);

                VertexPositionInputs p = GetVertexPositionInputs(cloth.positionOS);
                VertexNormalInputs n = GetVertexNormalInputs(cloth.normalOS);

                output.positionCS = p.positionCS;
                output.positionWS = p.positionWS;
                output.normalWS = n.normalWS;
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.fogCoord = ComputeFogFactor(p.positionCS.z);

                float3 fromAnchor = input.positionOS.xyz - _AnchorPoint.xyz;
                output.mask = saturate(dot(fromAnchor, normalize(_HangAxis.xyz +
                                           float3(0, -1e-6, 0))) / max(_HangLength, 1e-4));

                return output;
            }

            half4 ClothFrag(Varyings input,
                            FRONT_FACE_TYPE cullFace : FRONT_FACE_SEMANTIC) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

#ifdef _DEBUG_MASK
                // Black at the rail, white at the hem. If this is not a clean gradient
                // down the banner, the rig is wrong and no amount of tuning will help.
                return half4(input.mask.xxx, 1);
#endif

                half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
                half3 albedo = tex.rgb * _BaseColor.rgb;

                // A banner is a sheet: the back of it is the banner too, and has to be lit
                // as a back rather than as a hole.
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
                lighting.normalizedScreenSpaceUV =
                    GetNormalizedScreenSpaceUV(input.positionCS);
                lighting.shadowMask = half4(1, 1, 1, 1);

                SurfaceData surface = (SurfaceData)0;
                surface.albedo = albedo;
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
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Off

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

                // The SAME displacement as the lit pass. A banner whose shadow stands
                // still while the cloth moves is worse than one that does not move.
                ClothVertex cloth = Cloth(input.positionOS.xyz, input.normalOS);

                float3 positionWS = TransformObjectToWorld(cloth.positionOS);
                float3 normalWS = TransformObjectToWorldNormal(cloth.normalOS);

#if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDirectionWS = normalize(_LightPosition - positionWS);
#else
                float3 lightDirectionWS = _LightDirection;
#endif
                float4 positionCS =
                    TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
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
            Cull Off

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
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
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings DepthVert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                ClothVertex cloth = Cloth(input.positionOS.xyz, input.normalOS);
                output.positionCS = TransformObjectToHClip(cloth.positionOS);
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
            Cull Off

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
                ClothVertex cloth = Cloth(input.positionOS.xyz, input.normalOS);
                output.positionCS = TransformObjectToHClip(cloth.positionOS);
                output.normalWS = TransformObjectToWorldNormal(cloth.normalOS);
                return output;
            }

            half4 DepthNormalsFrag(Varyings input,
                                   FRONT_FACE_TYPE cullFace : FRONT_FACE_SEMANTIC) : SV_Target
            {
                float3 n = normalize(input.normalWS) * IS_FRONT_VFACE(cullFace, 1.0, -1.0);
                return half4(NormalizeNormalPerPixel(n), 0.0);
            }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Lit"
}
