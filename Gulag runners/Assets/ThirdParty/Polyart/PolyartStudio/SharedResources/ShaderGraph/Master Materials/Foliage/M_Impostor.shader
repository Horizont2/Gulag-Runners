Shader "Polyart/Utilities/Impostor"
{
    Properties
    {
        // Textures
        _MainTex("Albedo", 2D) = "white" {} 
        _NormalMap("Normals", 2D) = "bump" {}

        // Impostor Grid Params
        _GridSize("Grid Size", float) = 64
        _HorizontalSegments("Horizontal Segments", range(1, 64)) = 16
        _VerticalSegments("VerticalSegments", range(1, 15)) = 3
        _VerticalOffset("Vertical Offset", range(-15, 15)) = 0
        _VerticalStep("Horizontal Step", range(0.0, 90.0)) = 15.0

        // Color Customization
        _Hue("Hue Shift", Range(-1, 1)) = 0
        _Saturation("Saturation", Range(0, 2)) = 1
        _Value("Value (Brightness)", Range(0, 2)) = 1
        _Contrast("Contrast", Range(0, 2)) = 1
        _Smoothness("Smoothness", range(0, 1)) = 0.5
        _Metallic("Metallic", range(0, 1)) = 1.0
        _NormalStrength("Normal Strength", range(0, 10)) = 1.0

        // Opacity
        _AlphaClipThresshold("Alpha Clip Thresshold", range(0, 1)) = 0.355
    }
    SubShader
    {
        CGINCLUDE
        #pragma require integers
        #pragma target 3.0  
        #pragma multi_compile_instancing

        ENDCG
        Tags{ "RenderType" = "Opaque" }
        ZWrite On

        Cull Off // Renders both sides for two-sided shadows

        CGPROGRAM
        #pragma surface surf Standard vertex:vert alphatest:_AlphaClipThresshold addshadow fullforwardshadows dithercrossfade 
        #define HALF_PI 1.570795
        #define PI 3.14159
        #define TWO_PI 6.28318
        #define DEG2RAD 0.01745328

        struct Input {
            float2 uv_MainTex;
            float3 originViewDir;
        };

        sampler2D _MainTex;
        sampler2D _NormalMap;
        float _GridSize;
        uint _HorizontalSegments;
        float _VerticalSegments;
        float _VerticalOffset;
        float _VerticalStep;
        float _Smoothness;
        float _Metallic;
        float _NormalStrength;
        float _Hue;
        float _Saturation;
        float _Value;
        float _Contrast;

        void vert(inout appdata_full v, out Input o)
        {
            float3 viewVec = -_WorldSpaceCameraPos + mul(UNITY_MATRIX_M, float4(0, 0, 0, 1)).xyz;
            float3 viewDir = normalize(viewVec);

            float3 M2 = mul((float3x3)unity_WorldToObject, viewDir);
            float3 M0 = cross(mul((float3x3)unity_WorldToObject, float3(0, 1, 0)), M2);
            float3 M1 = cross(M2, M0);
            float3x3 mat = float3x3(normalize(M0), normalize(M1), M2);

            v.vertex.xyz = mul(v.vertex.xyz, mat);
            v.normal.xyz = mul(v.normal.xyz, mat);
            v.tangent.xyz = mul(v.tangent.xyz, mat);

            o.uv_MainTex = v.texcoord.xy;
            o.originViewDir = normalize(_WorldSpaceCameraPos - mul(unity_ObjectToWorld, float4(0, 0, 0, 1)).xyz);
        }

        void CalculateUVs(float2 uv_MainTex, float3 viewDir, out float2 UvLB, out float2 UvRB, out float2 UvLT, out float2 UvRT, out float alpha, out float beta)
        {
            float gridSide = round(sqrt(_GridSize));
            float gridStep = 1.0 / gridSide;

            float3x3 rotationScaleMatrix = float3x3(UNITY_MATRIX_M[0].xyz, UNITY_MATRIX_M[1].xyz, UNITY_MATRIX_M[2].xyz);
            float3x3 rotationOnlyMatrix;
            rotationOnlyMatrix[0] = normalize(rotationScaleMatrix[0]);
            rotationOnlyMatrix[1] = normalize(rotationScaleMatrix[1]);
            rotationOnlyMatrix[2] = normalize(rotationScaleMatrix[2]);
            float trYawOffset = -atan2(rotationOnlyMatrix._31, rotationOnlyMatrix._11);

            float yaw = atan2(viewDir.z, viewDir.x) + TWO_PI + trYawOffset;
            float yawId = (round((yaw / TWO_PI) * _HorizontalSegments) % _HorizontalSegments);
            float yawFrac = frac((yaw / TWO_PI) * _HorizontalSegments);

            float elevation = asin(viewDir.y);
            float elevationId = max(min(round(elevation / (_VerticalStep * DEG2RAD)) - _VerticalOffset, _VerticalSegments), -_VerticalSegments);
            float elevationFrac = frac(elevation / (_VerticalStep * DEG2RAD));

            alpha = 1.0 - yawFrac;
            beta = 1.0 - elevationFrac;

            float subdLB;
            float subdRB;
            float subdLT;
            float subdRT;

            if (elevationFrac < 0.5) {
                if (yawFrac < 0.5) {
                    subdLB = yawId + (elevationId + _VerticalSegments) * _HorizontalSegments;
                    subdRB = (yawId + 1) % _HorizontalSegments + (elevationId + _VerticalSegments) * _HorizontalSegments;
                    subdLT = yawId + (elevationId + (elevationId == _VerticalSegments ? 0 : 1) + _VerticalSegments) * _HorizontalSegments; /// 
                    subdRT = ((yawId + 1) % _HorizontalSegments + (elevationId + (elevationId == _VerticalSegments ? 0 : 1) + _VerticalSegments) * _HorizontalSegments);
                }
                else {
                    subdLB = (yawId - 1) % _HorizontalSegments + (elevationId + _VerticalSegments) * _HorizontalSegments;
                    subdRB = yawId + (elevationId + _VerticalSegments) * _HorizontalSegments;
                    subdLT = ((yawId - 1) % _HorizontalSegments + (elevationId + (elevationId == _VerticalSegments ? 0 : 1) + _VerticalSegments) * _HorizontalSegments);
                    subdRT = (yawId + (elevationId + (elevationId == _VerticalSegments ? 0 : 1) + _VerticalSegments) * _HorizontalSegments); /// 
                }
            }
            else {
                if (yawFrac < 0.5) {
                    subdLB = yawId + (elevationId - (elevationId == -_VerticalSegments ? 0 : 1) + _VerticalSegments) * _HorizontalSegments; /// 
                    subdRB = (yawId + 1) % _HorizontalSegments + (elevationId + _VerticalSegments - (elevationId == -_VerticalSegments ? 0 : 1)) * _HorizontalSegments;
                    subdLT = yawId + (elevationId + _VerticalSegments) * _HorizontalSegments;
                    subdRT = ((yawId + 1) % _HorizontalSegments + (elevationId + _VerticalSegments) * _HorizontalSegments);
                }
                else {
                    subdLB = (yawId - 1) % _HorizontalSegments + (elevationId + _VerticalSegments - (elevationId == -_VerticalSegments ? 0 : 1)) * _HorizontalSegments;
                    subdRB = yawId + (elevationId + _VerticalSegments - (elevationId == -_VerticalSegments ? 0 : 1)) * _HorizontalSegments; /// 
                    subdLT = ((yawId - 1) % _HorizontalSegments) + (elevationId + _VerticalSegments) * _HorizontalSegments;
                    subdRT = yawId + (elevationId + _VerticalSegments) * _HorizontalSegments;
                }
            }

            UvLB = uv_MainTex / gridSide + float2((subdLB % gridSide), floor(subdLB / gridSide)) * gridStep;
            UvRB = uv_MainTex / gridSide + float2((subdRB % gridSide), floor(subdRB / gridSide)) * gridStep;
            UvRT = uv_MainTex / gridSide + float2((subdRT % gridSide), floor(subdRT / gridSide)) * gridStep;
            UvLT = uv_MainTex / gridSide + float2((subdLT % gridSide), floor(subdLT / gridSide)) * gridStep;
        }

        // Convert RGB to HSV
        float3 RGBtoHSV(float3 color)
        {
            float maxVal = max(color.r, max(color.g, color.b));
            float minVal = min(color.r, min(color.g, color.b));
            float delta = maxVal - minVal;

            // Hue
            float hue = 0.0;
            if (delta > 0.0)
            {
                if (maxVal == color.r)
                    hue = (color.g - color.b) / delta + (color.g < color.b ? 6.0 : 0.0);
                else if (maxVal == color.g)
                    hue = (color.b - color.r) / delta + 2.0;
                else
                    hue = (color.r - color.g) / delta + 4.0;

                hue /= 6.0;
            }

            // Saturation
            float saturation = (maxVal == 0.0) ? 0.0 : (delta / maxVal);

            // Value
            float value = maxVal;

            return float3(hue, saturation, value);
        }

        // Convert HSV to RGB
        float3 HSVtoRGB(float3 hsv)
        {
            float h = hsv.x * 6.0;
            float s = hsv.y;
            float v = hsv.z;

            float c = v * s;
            float x = c * (1.0 - abs(fmod(h, 2.0) - 1.0));
            float m = v - c;

            float3 rgb;
            if (h < 1.0)
                rgb = float3(c, x, 0.0);
            else if (h < 2.0)
                rgb = float3(x, c, 0.0);
            else if (h < 3.0)
                rgb = float3(0.0, c, x);
            else if (h < 4.0)
                rgb = float3(0.0, x, c);
            else if (h < 5.0)
                rgb = float3(x, 0.0, c);
            else
                rgb = float3(c, 0.0, x);

            return rgb + m;
        }

        void surf(Input i, inout SurfaceOutputStandard  o)
        {
            float4 color, normal;
            float metallic;
            float alpha, beta;
            float2 UvLB, UvLT, UvRB, UvRT;

            float2 uv_MainTex = i.uv_MainTex;
            float3 viewDir = i.originViewDir;

            CalculateUVs(uv_MainTex, viewDir, UvLB, UvLT, UvRB, UvRT, alpha, beta);

            float4 cLB = tex2D(_MainTex, UvLB);
            float4 cRB = tex2D(_MainTex, UvRB);
            float4 cLT = tex2D(_MainTex, UvLT);
            float4 cRT = tex2D(_MainTex, UvRT);
            float4 cB = lerp(cRB, cLB, beta);
            float4 cT = lerp(cRT, cLT, beta);
            color = lerp(cT, cB, alpha);

            cLB = tex2D(_NormalMap, UvLB);
            cRB = tex2D(_NormalMap, UvRB);
            cLT = tex2D(_NormalMap, UvLT);
            cRT = tex2D(_NormalMap, UvRT);
            cB = lerp(cRB, cLB, beta);
            cT = lerp(cRT, cLT, beta);
            normal = lerp(cT, cB, alpha);

            // Convert RGB to HSV
            float3 hsv = RGBtoHSV(color.rgb);

            // Apply Hue adjustment (wrapping around)
            hsv.x = frac(hsv.x + _Hue);
            if (hsv.x < 0.0) hsv.x += 1.0;

            // Apply Saturation and Value adjustments
            hsv.y = saturate(hsv.y * _Saturation);
            hsv.z = saturate(hsv.z * _Value);

            // Convert back to RGB
            float3 rgb = HSVtoRGB(hsv);

            // Apply Contrast adjustment
            rgb = saturate((rgb - 0.5) * _Contrast + 0.5);


            

            normal = (normal * 2.0 - 1.0);
            normal.y = -normal.y;
            normal.xy *= _NormalStrength;
            normalize(normal);
            o.Normal = normal;
            
            o.Albedo = rgb;

            o.Smoothness = _Smoothness;
            o.Metallic = _Metallic;
            o.Alpha = color.a;
        }
        ENDCG 
    }
}