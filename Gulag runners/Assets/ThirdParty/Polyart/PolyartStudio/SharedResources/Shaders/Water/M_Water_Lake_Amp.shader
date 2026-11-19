// Made with Amplify Shader Editor v1.9.8.1
// Available at the Unity Asset Store - http://u3d.as/y3X 
Shader "M_Water_Lake_Amp"
{
	Properties
	{
		[Header(Color)]_ColorShallow("Color Shallow", Color) = (0.5990566,0.9091429,1,0)
		_ColorDeep("Color Deep", Color) = (0.1213065,0.347919,0.5471698,0)
		[HEADER(Color)]_ShallowWaterDistance("Shallow Water Distance", Float) = 0
		[Header(Waves)]_WavesNormal("Waves Normal", 2D) = "bump" {}
		[HEADER(Normals)]_NormalStrength("Normal Strength", Range( 0 , 1)) = 0.4593325
		[HEADER(Normals)]_Waves01Speed("Waves 01 Speed", Float) = 0.54
		[HEADER(Normals)]_Waves01Scale("Waves 01 Scale", Float) = 10
		[HEADER(Normals)]_Waves02Speed("Waves 02 Speed", Float) = 0.54
		[HEADER(Normals)]_Waves02Scale("Waves 02 Scale", Float) = 12
		[Header(Foam)][HEADER(Foam)]_FoamTiling("Foam Tiling", Float) = 50
		[HEADER(Foam)]_FoamOpacity1("FoamOpacity", Range( 0 , 1)) = 0
		[HEADER(Foam)]_FoamDistance1("Foam Distance", Range( 0.01 , 1)) = 0.07
		[Header(Caustics)][HEADER(Caustics)]_CausticsDistance("Caustics Distance", Float) = 0.1
		_CausticsTexture("Caustics Texture", 2D) = "white" {}
		[HEADER(Caustics)]_CausticsScale("Caustics Scale", Float) = 50
		[HEADER(Caustics)]_CausticsIntensity("Caustics Intensity", Float) = 0.1
		[HEADER(Caustics)]_CausticsSpeed("Caustics Speed", Float) = 0.025
		[HEADER(Caustics)]_CausticsDistortion("Caustics Distortion", Float) = 0.1
		[HEADER(Caustics)]_CausticsOpacity("Caustics Opacity", Float) = 0.2
		[HEADER(Caustics)]_CausticsHeightOffset("Caustics Height Offset", Float) = 0
		[Header(Opacity)][HEADER(Opacity)]_WaterMaxOpacity("Water Max Opacity", Range( 0 , 1)) = 0.91
		[HEADER(Opacity)]_WaterShallowOpacityDistance("Water Shallow Opacity Distance", Float) = 0
		[HEADER(Opacity)]_WaterEdgeDistance("Water Edge Distance", Float) = 0
		[Header(Basic Attributes)][HEADER(Basic Attributes)]_Smoothness("Smoothness", Range( 0 , 1)) = 1
		[HEADER(Basic Attributes)]_Refraction1("Refraction", Range( 0 , 1)) = 0.85
		_Waves02Speed("Waves 02 Speed", Float) = 0
		[Header(Emissive)]_EmissiveIntensity("Emissive Intensity", Float) = 0
		[HEADER(Emissive)][Toggle(_EMISSIVE_ON)] _Emissive("Emissive?", Float) = 0
		_EmissiveScale("Emissive Scale", Float) = 1
		_EmissiveTexture("Emissive Texture", 2D) = "black" {}
		_EmissiveRefraction("Emissive Refraction", Float) = 0.15
		[HideInInspector] __dirty( "", Int ) = 1
	}

	SubShader
	{
		Tags{ "RenderType" = "Transparent"  "Queue" = "Transparent+0" "IgnoreProjector" = "True" "IsEmissive" = "true"  }
		Cull Back
		GrabPass{ }
		CGPROGRAM
		#include "UnityStandardUtils.cginc"
		#include "UnityShaderVariables.cginc"
		#include "UnityCG.cginc"
		#include "UnityStandardBRDF.cginc"
		#pragma target 3.0
		#pragma shader_feature_local _EMISSIVE_ON
		#define ASE_VERSION 19801
		#if defined(UNITY_STEREO_INSTANCING_ENABLED) || defined(UNITY_STEREO_MULTIVIEW_ENABLED)
		#define ASE_DECLARE_SCREENSPACE_TEXTURE(tex) UNITY_DECLARE_SCREENSPACE_TEXTURE(tex);
		#else
		#define ASE_DECLARE_SCREENSPACE_TEXTURE(tex) UNITY_DECLARE_SCREENSPACE_TEXTURE(tex)
		#endif
		#pragma surface surf Standard alpha:fade keepalpha exclude_path:deferred 
		struct Input
		{
			float3 worldPos;
			float4 screenPos;
			float3 viewDir;
			INTERNAL_DATA
		};

		uniform sampler2D _WavesNormal;
		uniform float _Waves01Scale;
		uniform float _Waves01Speed;
		uniform float _Waves02Speed;
		uniform float _Waves02Scale;
		uniform float _NormalStrength;
		ASE_DECLARE_SCREENSPACE_TEXTURE( _GrabTexture )
		uniform float _Refraction1;
		uniform float4 _ColorShallow;
		uniform float4 _ColorDeep;
		UNITY_DECLARE_DEPTH_TEXTURE( _CameraDepthTexture );
		uniform float4 _CameraDepthTexture_TexelSize;
		uniform float _ShallowWaterDistance;
		uniform float _FoamTiling;
		uniform float _FoamDistance1;
		uniform float _FoamOpacity1;
		uniform sampler2D _CausticsTexture;
		uniform float _CausticsScale;
		uniform float _CausticsHeightOffset;
		uniform float _CausticsSpeed;
		uniform float _CausticsDistortion;
		uniform float _CausticsIntensity;
		uniform float _CausticsDistance;
		uniform float _WaterShallowOpacityDistance;
		uniform float _WaterMaxOpacity;
		uniform float _CausticsOpacity;
		uniform float _WaterEdgeDistance;
		uniform sampler2D _EmissiveTexture;
		uniform float _EmissiveScale;
		uniform float _EmissiveRefraction;
		uniform float _EmissiveIntensity;
		uniform float _Smoothness;


		float2 voronoihash200( float2 p )
		{
			
			p = float2( dot( p, float2( 127.1, 311.7 ) ), dot( p, float2( 269.5, 183.3 ) ) );
			return frac( sin( p ) *43758.5453);
		}


		float voronoi200( float2 v, float time, inout float2 id, inout float2 mr, float smoothness, inout float2 smoothId )
		{
			float2 n = floor( v );
			float2 f = frac( v );
			float F1 = 8.0;
			float F2 = 8.0; float2 mg = 0;
			for ( int j = -1; j <= 1; j++ )
			{
				for ( int i = -1; i <= 1; i++ )
			 	{
			 		float2 g = float2( i, j );
			 		float2 o = voronoihash200( n + g );
					o = ( sin( time + o * 6.2831 ) * 0.5 + 0.5 ); float2 r = f - g - o;
					float d = 0.5 * ( abs(r.x) + abs(r.y) );
			 		if( d<F1 ) {
			 			F2 = F1;
			 			F1 = d; mg = g; mr = r; id = o;
			 		} else if( d<F2 ) {
			 			F2 = d;
			
			 		}
			 	}
			}
			return F1;
		}


		void surf( Input i , inout SurfaceOutputStandard o )
		{
			float3 ase_positionWS = i.worldPos;
			float4 appendResult36 = (float4(ase_positionWS.x , ase_positionWS.z , 0.0 , 0.0));
			float3 tex2DNode9 = UnpackNormal( tex2D( _WavesNormal, ( ( appendResult36 / _Waves01Scale ) + float4( ( ( _Waves01Speed * _Time.y ) * float2( 0.8,1 ) ), 0.0 , 0.0 ) ).xy ) );
			float4 appendResult37 = (float4(ase_positionWS.x , ase_positionWS.z , 0.0 , 0.0));
			float3 lerpResult39 = lerp( float3(0,0,1) , BlendNormals( tex2DNode9 , UnpackNormal( tex2D( _WavesNormal, ( ( _Time.y * _Waves02Speed ) + ( appendResult37 / _Waves02Scale ) ).xy ) ) ) , _NormalStrength);
			float3 Normal222 = lerpResult39;
			o.Normal = Normal222;
			float4 ase_positionSS = float4( i.screenPos.xyz , i.screenPos.w + 1e-7 );
			float4 ase_positionSSNorm = ase_positionSS / ase_positionSS.w;
			ase_positionSSNorm.z = ( UNITY_NEAR_CLIP_VALUE >= 0 ) ? ase_positionSSNorm.z : ase_positionSSNorm.z * 0.5 + 0.5;
			float3 Refraction_Normal151 = tex2DNode9;
			float4 screenColor190 = UNITY_SAMPLE_SCREENSPACE_TEXTURE(_GrabTexture,( ase_positionSSNorm + float4( ( _Refraction1 * Refraction_Normal151 ) , 0.0 ) ).xy);
			float depthLinearEye43 = LinearEyeDepth( SAMPLE_DEPTH_TEXTURE( _CameraDepthTexture, ase_positionSSNorm.xy ) );
			float4 lerpResult6 = lerp( _ColorShallow , _ColorDeep , saturate( ( ( depthLinearEye43 - ase_positionSS.w ) * _ShallowWaterDistance ) ));
			float4 vDepthColor7 = lerpResult6;
			float4 temp_cast_5 = (1.0).xxxx;
			float mulTime213 = _Time.y * _Waves02Speed;
			float time200 = ( mulTime213 * 50 );
			float2 voronoiSmoothId200 = 0;
			float2 appendResult217 = (float2(ase_positionWS.x , ase_positionWS.z));
			float2 FoamWorldSpaceTile219 = ( appendResult217 * 0.15 );
			float2 coords200 = FoamWorldSpaceTile219 * _FoamTiling;
			float2 id200 = 0;
			float2 uv200 = 0;
			float fade200 = 0.5;
			float voroi200 = 0;
			float rest200 = 0;
			for( int it200 = 0; it200 <3; it200++ ){
			voroi200 += fade200 * voronoi200( coords200, time200, id200, uv200, 0,voronoiSmoothId200 );
			rest200 += fade200;
			coords200 *= 2;
			fade200 *= 0.5;
			}//Voronoi200
			voroi200 /= rest200;
			float saferPower203 = abs( ( 1.0 - voroi200 ) );
			float screenDepth211 = LinearEyeDepth(SAMPLE_DEPTH_TEXTURE( _CameraDepthTexture, ase_positionSSNorm.xy ));
			float distanceDepth211 = abs( ( screenDepth211 - LinearEyeDepth( ase_positionSSNorm.z ) ) / ( ( _FoamDistance1 * 0.3 ) ) );
			float Foam210 = ( saturate( ( pow( saferPower203 , -1.5 ) + ( 1.0 - distanceDepth211 ) ) ) * _FoamOpacity1 );
			float4 lerpResult128 = lerp( vDepthColor7 , temp_cast_5 , Foam210);
			float4 appendResult147 = (float4(ase_positionWS.x , ase_positionWS.z , 0.0 , 0.0));
			float2 paralaxOffset144 = ParallaxOffset( 0 , _CausticsHeightOffset , Unity_SafeNormalize( i.viewDir ) );
			float mulTime155 = _Time.y * _CausticsSpeed;
			float depthLinearEye172 = LinearEyeDepth( SAMPLE_DEPTH_TEXTURE( _CameraDepthTexture, ase_positionSSNorm.xy ) );
			float4 lerpResult177 = lerp( saturate( ( tex2D( _CausticsTexture, ( ( ( ( appendResult147 / _CausticsScale ) + float4( paralaxOffset144, 0.0 , 0.0 ) ) + float4( ( mulTime155 * float2( 1,0 ) ), 0.0 , 0.0 ) ) + ( Refraction_Normal151.x * _CausticsDistortion ) ).xy ) * _CausticsIntensity ) ) , float4( 0,0,0,0 ) , saturate( ( ( depthLinearEye172 - ase_positionSS.w ) * _CausticsDistance ) ));
			float4 Caustics166 = lerpResult177;
			float depthLinearEye59 = LinearEyeDepth( SAMPLE_DEPTH_TEXTURE( _CameraDepthTexture, ase_positionSSNorm.xy ) );
			float depthLinearEye54 = LinearEyeDepth( SAMPLE_DEPTH_TEXTURE( _CameraDepthTexture, ase_positionSSNorm.xy ) );
			float temp_output_58_0 = saturate( ( ( depthLinearEye54 - ase_positionSS.w ) * _WaterEdgeDistance ) );
			float Opacity130 = ( ( ( min( saturate( ( ( depthLinearEye59 - ase_positionSS.w ) * _WaterShallowOpacityDistance ) ) , _WaterMaxOpacity ) + Foam210 ) + ( length( Caustics166 ) * _CausticsOpacity ) ) * temp_output_58_0 );
			float4 lerpResult183 = lerp( screenColor190 , ( lerpResult128 + Caustics166 ) , Opacity130);
			float4 FinalColor229 = lerpResult183;
			o.Albedo = FinalColor229.rgb;
			float3 temp_cast_10 = (0.0).xxx;
			float4 appendResult237 = (float4(ase_positionWS.x , ase_positionWS.z , 0.0 , 0.0));
			float4 temp_output_238_0 = ( appendResult237 / _EmissiveScale );
			float4 lerpResult242 = lerp( temp_output_238_0 , ( temp_output_238_0 * float4( Normal222 , 0.0 ) ) , ( _EmissiveRefraction / 10.0 ));
			#ifdef _EMISSIVE_ON
				float3 staticSwitch246 = ( tex2D( _EmissiveTexture, lerpResult242.xy ).rgb * _EmissiveIntensity );
			#else
				float3 staticSwitch246 = temp_cast_10;
			#endif
			o.Emission = staticSwitch246;
			o.Smoothness = _Smoothness;
			float OpacityOutput226 = temp_output_58_0;
			o.Alpha = OpacityOutput226;
		}

		ENDCG
	}
	Fallback "Diffuse"
	CustomEditor "AmplifyShaderEditor.MaterialInspector"
}
/*ASEBEGIN
Version=19801
Node;AmplifyShaderEditor.CommentaryNode;221;-3966.575,61.80243;Inherit;False;2478.808;762.0764;Normal;26;222;39;41;40;33;25;30;28;15;37;27;16;26;151;9;21;10;19;69;75;20;36;12;18;14;11;;1,1,1,1;0;0
Node;AmplifyShaderEditor.SimpleTimeNode;11;-3915.317,388.6026;Inherit;False;1;0;FLOAT;1;False;1;FLOAT;0
Node;AmplifyShaderEditor.WorldPosInputsNode;18;-3916.317,112.6024;Inherit;False;0;4;FLOAT3;0;FLOAT;1;FLOAT;2;FLOAT;3
Node;AmplifyShaderEditor.RangedFloatNode;14;-3960.12,275.8022;Inherit;False;Property;_Waves01Speed;Waves 01 Speed;5;0;Create;True;0;0;0;False;1;HEADER(Normals);False;0.54;0.2;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;12;-3707.317,276.6023;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.DynamicAppendNode;36;-3724.317,112.2023;Inherit;False;FLOAT4;4;0;FLOAT;0;False;1;FLOAT;0;False;2;FLOAT;0;False;3;FLOAT;0;False;1;FLOAT4;0
Node;AmplifyShaderEditor.Vector2Node;75;-3567.676,372.2531;Inherit;False;Constant;_Vector1;Vector 1;15;0;Create;True;0;0;0;False;0;False;0.8,1;0,0;0;3;FLOAT2;0;FLOAT;1;FLOAT;2
Node;AmplifyShaderEditor.RangedFloatNode;20;-3568.317,180.5023;Inherit;False;Property;_Waves01Scale;Waves 01 Scale;6;0;Create;True;0;0;0;False;1;HEADER(Normals);False;10;0.2;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleDivideOpNode;19;-3353.117,111.8024;Inherit;False;2;0;FLOAT4;0,0,0,0;False;1;FLOAT;0;False;1;FLOAT4;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;69;-3393.945,274.5955;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT2;0,0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.SimpleAddOpNode;21;-3184.012,248.7584;Inherit;False;2;2;0;FLOAT4;0,0,0,0;False;1;FLOAT2;0,0;False;1;FLOAT4;0
Node;AmplifyShaderEditor.TexturePropertyNode;10;-3172.814,555.9269;Inherit;True;Property;_WavesNormal;Waves Normal;3;1;[Header];Create;True;1;Waves;0;0;False;0;False;None;4ea058df3811fdf439d5bff6fbdb2316;True;bump;Auto;Texture2D;-1;0;2;SAMPLER2D;0;SAMPLERSTATE;1
Node;AmplifyShaderEditor.CommentaryNode;231;-4799.208,1293.469;Inherit;False;3308.675;622.0664;Caustics;30;146;147;148;149;170;152;150;144;145;153;154;155;156;160;161;162;159;158;163;164;165;172;173;174;175;176;171;169;177;166;Caustics;1,1,1,1;0;0
Node;AmplifyShaderEditor.CommentaryNode;214;-4293.641,898.601;Inherit;False;914.8306;366.1572;;5;219;218;217;215;216;World Space Tile;1,1,1,1;0;0
Node;AmplifyShaderEditor.SamplerNode;9;-2876.828,226.3194;Inherit;True;Property;_T_WaterNormal_01;T_WaterNormal_01;3;0;Create;True;0;0;0;False;0;False;-1;4ea058df3811fdf439d5bff6fbdb2316;4ea058df3811fdf439d5bff6fbdb2316;True;0;True;bump;Auto;True;Object;-1;Auto;Texture2D;8;0;SAMPLER2D;;False;1;FLOAT2;0,0;False;2;FLOAT;0;False;3;FLOAT2;0,0;False;4;FLOAT2;0,0;False;5;FLOAT;1;False;6;FLOAT;0;False;7;SAMPLERSTATE;;False;6;FLOAT3;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4;FLOAT3;5
Node;AmplifyShaderEditor.WorldPosInputsNode;215;-4245.675,998.7369;Inherit;True;0;4;FLOAT3;0;FLOAT;1;FLOAT;2;FLOAT;3
Node;AmplifyShaderEditor.WorldPosInputsNode;146;-4749.208,1351.499;Inherit;False;0;4;FLOAT3;0;FLOAT;1;FLOAT;2;FLOAT;3
Node;AmplifyShaderEditor.RegisterLocalVarNode;151;-2508.323,451.0839;Inherit;False;Refraction Normal;-1;True;1;0;FLOAT3;0,0,0;False;1;FLOAT3;0
Node;AmplifyShaderEditor.DynamicAppendNode;217;-3985.062,1033.809;Inherit;False;FLOAT2;4;0;FLOAT;0;False;1;FLOAT;0;False;2;FLOAT;0;False;3;FLOAT;0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.DynamicAppendNode;147;-4536.543,1349.281;Inherit;False;FLOAT4;4;0;FLOAT;0;False;1;FLOAT;0;False;2;FLOAT;0;False;3;FLOAT;0;False;1;FLOAT4;0
Node;AmplifyShaderEditor.ViewDirInputsCoordNode;145;-4296.187,1573.432;Inherit;False;Tangent;True;0;4;FLOAT3;0;FLOAT;1;FLOAT;2;FLOAT;3
Node;AmplifyShaderEditor.RangedFloatNode;216;-3969.305,1174.828;Inherit;False;Constant;_Tiling;Foam Tiling;17;0;Create;False;0;0;0;False;1;HEADER(Foam);False;0.15;0.15;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;148;-4361.938,1419.415;Inherit;False;Property;_CausticsScale;Caustics Scale;13;0;Create;True;0;0;0;False;1;HEADER(Caustics);False;50;0.2;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;156;-4051.381,1648.1;Inherit;False;Property;_CausticsSpeed;Caustics Speed;15;0;Create;True;0;0;0;False;1;HEADER(Caustics);False;0.025;0.2;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;150;-4359.726,1499.782;Inherit;False;Property;_CausticsHeightOffset;Caustics Height Offset;18;0;Create;True;0;0;0;False;1;HEADER(Caustics);False;0;0.2;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.CommentaryNode;193;-3355.523,849.819;Inherit;False;1868.142;417.1328;;17;225;210;207;208;201;203;213;224;196;197;200;206;205;204;211;199;198;Foam;1,1,1,1;0;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;218;-3768.865,1088.647;Inherit;False;2;2;0;FLOAT2;0,0;False;1;FLOAT;0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.RangedFloatNode;224;-3316.364,987.0937;Inherit;False;Property;_Waves02Speed;Waves 02 Speed;24;0;Fetch;True;0;0;0;False;0;False;0;0;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleDivideOpNode;149;-4143.931,1347.437;Inherit;False;2;0;FLOAT4;0,0,0,0;False;1;FLOAT;0;False;1;FLOAT4;0
Node;AmplifyShaderEditor.ParallaxOffsetHlpNode;144;-4016.059,1426.967;Inherit;False;3;0;FLOAT;0;False;1;FLOAT;0;False;2;FLOAT3;0,0,0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.Vector2Node;153;-3867.157,1629.057;Inherit;False;Constant;_Vector3;Vector 1;15;0;Create;True;0;0;0;False;0;False;1,0;0,0;0;3;FLOAT2;0;FLOAT;1;FLOAT;2
Node;AmplifyShaderEditor.SimpleTimeNode;155;-4051.864,1578.183;Inherit;False;1;0;FLOAT;1;False;1;FLOAT;0
Node;AmplifyShaderEditor.GetLocalVarNode;159;-3542.332,1586.806;Inherit;False;151;Refraction Normal;1;0;OBJECT;;False;1;FLOAT3;0
Node;AmplifyShaderEditor.RegisterLocalVarNode;219;-3629.563,1088.542;Inherit;False;FoamWorldSpaceTile;-1;True;1;0;FLOAT2;0,0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.SimpleTimeNode;213;-3111.332,985.2119;Inherit;False;1;0;FLOAT;0.01;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleAddOpNode;170;-3787.677,1343.469;Inherit;False;2;2;0;FLOAT4;0,0,0,0;False;1;FLOAT2;0,0;False;1;FLOAT4;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;154;-3702.443,1579.507;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT2;0,0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.BreakToComponentsNode;160;-3328.917,1585.774;Inherit;False;FLOAT3;1;0;FLOAT3;0,0,0;False;16;FLOAT;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4;FLOAT;5;FLOAT;6;FLOAT;7;FLOAT;8;FLOAT;9;FLOAT;10;FLOAT;11;FLOAT;12;FLOAT;13;FLOAT;14;FLOAT;15
Node;AmplifyShaderEditor.RangedFloatNode;162;-3544.494,1672.521;Inherit;False;Property;_CausticsDistortion;Caustics Distortion;16;0;Create;True;0;0;0;False;1;HEADER(Caustics);False;0.1;0.2;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.ScaleNode;196;-2933.623,985.5922;Inherit;False;50;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.GetLocalVarNode;225;-3027.834,910.433;Inherit;False;219;FoamWorldSpaceTile;1;0;OBJECT;;False;1;FLOAT2;0
Node;AmplifyShaderEditor.SimpleAddOpNode;152;-3605.855,1452.279;Inherit;False;2;2;0;FLOAT4;0,0,0,0;False;1;FLOAT2;0,0;False;1;FLOAT4;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;161;-3184.534,1585.49;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;198;-3200.263,1163.692;Inherit;False;Property;_FoamDistance1;Foam Distance;10;0;Create;False;0;0;0;False;1;HEADER(Foam);False;0.07;0.207;0.01;1;0;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;197;-2938.081,1064.207;Inherit;False;Property;_FoamTiling;Foam Tiling;8;1;[Header];Create;False;1;Foam;0;0;False;1;HEADER(Foam);False;50;4;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.ScaleNode;199;-2876.332,1164.167;Inherit;False;0.3;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.VoronoiNode;200;-2738.981,917.1051;Inherit;False;0;2;1;0;3;False;1;False;False;False;4;0;FLOAT2;0,0;False;1;FLOAT;0;False;2;FLOAT;1;False;3;FLOAT;0;False;3;FLOAT;0;FLOAT;1;FLOAT2;2
Node;AmplifyShaderEditor.SimpleAddOpNode;158;-3038.152,1450.22;Inherit;False;2;2;0;FLOAT4;0,0,0,0;False;1;FLOAT;0;False;1;FLOAT4;0
Node;AmplifyShaderEditor.ScreenDepthNode;172;-2979.28,1633.381;Inherit;False;0;1;0;FLOAT4;0,0,0,0;False;1;FLOAT;0
Node;AmplifyShaderEditor.ScreenPosInputsNode;173;-2964.557,1706.537;Float;False;1;False;0;5;FLOAT4;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.DepthFade;211;-2675.646,1140.175;Inherit;False;True;False;True;2;1;FLOAT3;0,0,0;False;0;FLOAT;1;False;1;FLOAT;0
Node;AmplifyShaderEditor.OneMinusNode;201;-2557.613,918.1769;Inherit;False;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleSubtractOpNode;174;-2701.696,1694.352;Inherit;False;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;165;-2597.597,1498.683;Inherit;False;Property;_CausticsIntensity;Caustics Intensity;14;0;Create;True;0;0;0;False;1;HEADER(Caustics);False;0.1;0.2;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;175;-2505.704,1741.817;Inherit;False;Property;_CausticsDistance;Caustics Distance;11;1;[Header];Create;True;1;Caustics;0;0;False;1;HEADER(Caustics);False;0.1;-13.06;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.SamplerNode;163;-2897.438,1427.161;Inherit;True;Property;_CausticsTexture;Caustics Texture;12;0;Create;True;0;0;0;False;0;False;-1;None;None;True;0;False;white;Auto;False;Object;-1;Auto;Texture2D;8;0;SAMPLER2D;;False;1;FLOAT2;0,0;False;2;FLOAT;0;False;3;FLOAT2;0,0;False;4;FLOAT2;0,0;False;5;FLOAT;1;False;6;FLOAT;0;False;7;SAMPLERSTATE;;False;6;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4;FLOAT3;5
Node;AmplifyShaderEditor.OneMinusNode;204;-2395.044,1139.915;Inherit;False;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;164;-2385.854,1431.026;Inherit;False;2;2;0;COLOR;0,0,0,0;False;1;FLOAT;0;False;1;COLOR;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;176;-2236.085,1698.151;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.PowerNode;203;-2407.287,917.8177;Inherit;False;True;2;0;FLOAT;0;False;1;FLOAT;-1.5;False;1;FLOAT;0
Node;AmplifyShaderEditor.CommentaryNode;228;-4308.832,-1236.782;Inherit;False;1851.613;686.6728;Opacity;24;63;65;67;66;178;64;130;62;59;60;61;136;127;180;182;181;179;54;55;56;57;53;58;226;Opacity;1,1,1,1;0;0
Node;AmplifyShaderEditor.SimpleAddOpNode;205;-2179.338,1024.891;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SaturateNode;171;-2087.263,1698.235;Inherit;False;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SaturateNode;169;-2188.107,1430.268;Inherit;False;1;0;COLOR;0,0,0,0;False;1;COLOR;0
Node;AmplifyShaderEditor.ScreenDepthNode;59;-4258.832,-1186.782;Inherit;False;0;1;0;FLOAT4;0,0,0,0;False;1;FLOAT;0
Node;AmplifyShaderEditor.ScreenPosInputsNode;60;-4244.107,-1113.627;Float;False;1;False;0;5;FLOAT4;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.WorldPosInputsNode;26;-3920,560;Inherit;False;0;4;FLOAT3;0;FLOAT;1;FLOAT;2;FLOAT;3
Node;AmplifyShaderEditor.SaturateNode;206;-2028.057,1024.167;Inherit;False;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.LerpOp;177;-1888.198,1430.114;Inherit;False;3;0;COLOR;0,0,0,0;False;1;COLOR;0,0,0,0;False;2;FLOAT;0;False;1;COLOR;0
Node;AmplifyShaderEditor.RangedFloatNode;207;-2178.871,1130.75;Inherit;False;Property;_FoamOpacity1;FoamOpacity;9;0;Create;False;0;0;0;False;1;HEADER(Foam);False;0;0.2;0;1;0;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleSubtractOpNode;61;-3981.249,-1125.812;Inherit;False;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;62;-4055.212,-972.2425;Inherit;False;Property;_WaterShallowOpacityDistance;Water Shallow Opacity Distance;20;0;Create;True;0;0;0;False;1;HEADER(Opacity);False;0;-13.06;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.CommentaryNode;8;-3255.219,-524.7608;Inherit;False;1764.648;559.485;Color;15;128;137;129;1;4;5;7;6;3;46;45;44;43;167;168;Color;1,1,1,1;0;0
Node;AmplifyShaderEditor.RangedFloatNode;16;-3942.151,465.0211;Inherit;False;Property;_Waves02Speed;Waves 02 Speed;7;0;Create;True;0;0;0;False;1;HEADER(Normals);False;0.54;0.2;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;27;-3504.31,636.9872;Inherit;False;Property;_Waves02Scale;Waves 02 Scale;7;0;Create;True;0;0;0;False;1;HEADER(Normals);False;12;0.2;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.DynamicAppendNode;37;-3712,560;Inherit;False;FLOAT4;4;0;FLOAT;0;False;1;FLOAT;0;False;2;FLOAT;0;False;3;FLOAT;0;False;1;FLOAT4;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;208;-1857.076,1023.232;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.RegisterLocalVarNode;166;-1732.527,1430.786;Inherit;False;Caustics;-1;True;1;0;COLOR;0,0,0,0;False;1;COLOR;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;63;-3759.541,-1127.484;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.ScreenDepthNode;43;-3204.082,-219.6112;Inherit;False;0;1;0;FLOAT4;0,0,0,0;False;1;FLOAT;0
Node;AmplifyShaderEditor.ScreenPosInputsNode;44;-3208.559,-148.0554;Float;False;1;False;0;5;FLOAT4;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;15;-3703.625,443.7023;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleDivideOpNode;28;-3311.307,563.0116;Inherit;False;2;0;FLOAT4;0,0,0,0;False;1;FLOAT;0;False;1;FLOAT4;0
Node;AmplifyShaderEditor.RegisterLocalVarNode;210;-1704.931,1023.502;Inherit;False;Foam;-1;True;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SaturateNode;65;-3582.638,-1121.448;Inherit;False;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.GetLocalVarNode;179;-3645.703,-883.3157;Inherit;False;166;Caustics;1;0;OBJECT;;False;1;COLOR;0
Node;AmplifyShaderEditor.ScreenDepthNode;54;-3930.488,-832.2648;Inherit;False;0;1;0;FLOAT4;0,0,0,0;False;1;FLOAT;0
Node;AmplifyShaderEditor.ScreenPosInputsNode;55;-3915.765,-759.1097;Float;False;1;False;0;5;FLOAT4;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.SimpleSubtractOpNode;45;-2947.299,-219.4411;Inherit;False;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;1;-2979.898,-81.53427;Inherit;False;Property;_ShallowWaterDistance;Shallow Water Distance;2;0;Create;True;0;0;0;False;1;HEADER(Color);False;0;-13.06;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;67;-3737.866,-970.3265;Inherit;False;Property;_WaterMaxOpacity;Water Max Opacity;19;1;[Header];Create;True;1;Opacity;0;0;False;1;HEADER(Opacity);False;0.91;-13.06;0;1;0;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleAddOpNode;30;-3180.971,446.4008;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT4;0,0,0,0;False;1;FLOAT4;0
Node;AmplifyShaderEditor.SimpleMinOpNode;66;-3405.676,-1069.518;Inherit;False;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.GetLocalVarNode;136;-3403.2,-974.3006;Inherit;False;210;Foam;1;0;OBJECT;;False;1;FLOAT;0
Node;AmplifyShaderEditor.LengthOpNode;182;-3462.859,-882.441;Inherit;False;1;0;COLOR;0,0,0,0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleSubtractOpNode;56;-3652.905,-771.2948;Inherit;False;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;46;-2710.477,-113.9996;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;57;-3456.915,-723.8301;Inherit;False;Property;_WaterEdgeDistance;Water Edge Distance;21;0;Create;True;0;0;0;False;1;HEADER(Opacity);False;0;-13.06;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;180;-3453.87,-807.0458;Inherit;False;Property;_CausticsOpacity;Caustics Opacity;17;0;Create;True;0;0;0;False;1;HEADER(Caustics);False;0.2;-13.06;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.SamplerNode;25;-2878.034,425.3209;Inherit;True;Property;_TextureSample1;Texture Sample 1;7;0;Create;True;0;0;0;False;0;False;-1;4ea058df3811fdf439d5bff6fbdb2316;4ea058df3811fdf439d5bff6fbdb2316;True;0;True;bump;Auto;True;Object;-1;Auto;Texture2D;8;0;SAMPLER2D;;False;1;FLOAT2;0,0;False;2;FLOAT;0;False;3;FLOAT2;0,0;False;4;FLOAT2;0,0;False;5;FLOAT;1;False;6;FLOAT;0;False;7;SAMPLERSTATE;;False;6;FLOAT3;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4;FLOAT3;5
Node;AmplifyShaderEditor.SimpleAddOpNode;127;-3165.346,-1070.5;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;181;-3212.877,-877.7269;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;53;-3187.297,-767.4957;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SaturateNode;4;-2560.996,-114.534;Inherit;False;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.ColorNode;5;-2575.781,-462.4581;Inherit;False;Property;_ColorShallow;Color Shallow;0;0;Create;True;0;0;0;False;1;Header(Color);False;0.5990566,0.9091429,1,0;0,0.6274511,0.7176471,0;True;True;0;6;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4;FLOAT3;5
Node;AmplifyShaderEditor.ColorNode;3;-2572.283,-295.8587;Inherit;False;Property;_ColorDeep;Color Deep;1;0;Create;True;0;0;0;False;0;False;0.1213065,0.347919,0.5471698,0;0,0.3372549,0.3882353,0;True;True;0;6;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4;FLOAT3;5
Node;AmplifyShaderEditor.CommentaryNode;220;-2430.941,-1013.666;Inherit;False;946.3938;464.6796;Refraction;6;189;187;188;192;186;190;Refraction;1,1,1,1;0;0
Node;AmplifyShaderEditor.BlendNormalsNode;33;-2515.7,305.4534;Inherit;False;0;3;0;FLOAT3;0,0,0;False;1;FLOAT3;0,0,0;False;2;FLOAT3;0,0,0;False;1;FLOAT3;0
Node;AmplifyShaderEditor.Vector3Node;40;-2162.702,284.8303;Inherit;False;Constant;_Vector0;Vector 0;9;0;Create;True;0;0;0;False;0;False;0,0,1;0,0,0;0;4;FLOAT3;0;FLOAT;1;FLOAT;2;FLOAT;3
Node;AmplifyShaderEditor.RangedFloatNode;41;-2255.703,437.8309;Inherit;False;Property;_NormalStrength;Normal Strength;4;0;Create;True;0;0;0;False;1;HEADER(Normals);False;0.4593325;0;0;1;0;1;FLOAT;0
Node;AmplifyShaderEditor.WorldPosInputsNode;236;-1264,1040;Inherit;False;0;4;FLOAT3;0;FLOAT;1;FLOAT;2;FLOAT;3
Node;AmplifyShaderEditor.SimpleAddOpNode;178;-3002.438,-1004.003;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SaturateNode;58;-3038.476,-767.4117;Inherit;False;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.LerpOp;6;-2270.8,-392.4593;Inherit;False;3;0;COLOR;0,0,0,0;False;1;COLOR;0,0,0,0;False;2;FLOAT;0;False;1;COLOR;0
Node;AmplifyShaderEditor.RangedFloatNode;187;-2380.941,-749.0635;Inherit;False;Property;_Refraction1;Refraction;23;0;Create;True;0;0;0;False;1;HEADER(Basic Attributes);False;0.85;1;0;1;0;1;FLOAT;0
Node;AmplifyShaderEditor.GetLocalVarNode;189;-2278.011,-661.985;Inherit;False;151;Refraction Normal;1;0;OBJECT;;False;1;FLOAT3;0
Node;AmplifyShaderEditor.LerpOp;39;-1961.699,284.8303;Inherit;False;3;0;FLOAT3;0,0,0;False;1;FLOAT3;0,0,0;False;2;FLOAT;0;False;1;FLOAT3;0
Node;AmplifyShaderEditor.DynamicAppendNode;237;-1056,1040;Inherit;False;FLOAT4;4;0;FLOAT;0;False;1;FLOAT;0;False;2;FLOAT;0;False;3;FLOAT;0;False;1;FLOAT4;0
Node;AmplifyShaderEditor.RangedFloatNode;233;-1104,1216;Inherit;False;Property;_EmissiveScale;Emissive Scale;27;0;Create;True;0;0;0;False;0;False;1;0.2;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;188;-2071.551,-712.1256;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT3;0,0,0;False;1;FLOAT3;0
Node;AmplifyShaderEditor.ScreenPosInputsNode;192;-2106.167,-963.6659;Float;False;0;False;0;5;FLOAT4;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;64;-2854.337,-986.7687;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.RegisterLocalVarNode;7;-2104.69,-390.2727;Inherit;False;vDepthColor;-1;True;1;0;COLOR;0,0,0,0;False;1;COLOR;0
Node;AmplifyShaderEditor.RegisterLocalVarNode;222;-1712,368;Inherit;False;Normal;-1;True;1;0;FLOAT3;0,0,0;False;1;FLOAT3;0
Node;AmplifyShaderEditor.RangedFloatNode;129;-2096,-320;Inherit;False;Constant;_Float2;Float 2;25;0;Create;True;0;0;0;False;0;False;1;0;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.GetLocalVarNode;137;-2112,-224;Inherit;False;210;Foam;1;0;OBJECT;;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleDivideOpNode;238;-944,912;Inherit;False;2;0;FLOAT4;0,0,0,0;False;1;FLOAT;0;False;1;FLOAT4;0
Node;AmplifyShaderEditor.RangedFloatNode;243;-1280,320;Inherit;False;Property;_EmissiveRefraction;Emissive Refraction;29;0;Create;True;0;0;0;False;0;False;0.15;0;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.GetLocalVarNode;168;-1856.698,-259.4942;Inherit;False;166;Caustics;1;0;OBJECT;;False;1;COLOR;0
Node;AmplifyShaderEditor.SimpleAddOpNode;186;-1827.926,-797.468;Inherit;False;2;2;0;FLOAT4;0,0,0,0;False;1;FLOAT3;0,0,0;False;1;FLOAT4;0
Node;AmplifyShaderEditor.RegisterLocalVarNode;130;-2699.224,-987.495;Inherit;False;Opacity;-1;True;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.LerpOp;128;-1858.132,-389.8068;Inherit;False;3;0;COLOR;0,0,0,0;False;1;COLOR;0,0,0,0;False;2;FLOAT;0;False;1;COLOR;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;239;-1216,448;Inherit;False;2;2;0;FLOAT4;0,0,0,0;False;1;FLOAT3;0,0,0;False;1;FLOAT4;0
Node;AmplifyShaderEditor.SimpleDivideOpNode;244;-1024,608;Inherit;False;2;0;FLOAT;0;False;1;FLOAT;10;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleAddOpNode;167;-1650.542,-392.0468;Inherit;False;2;2;0;COLOR;0,0,0,0;False;1;COLOR;0,0,0,0;False;1;COLOR;0
Node;AmplifyShaderEditor.ScreenColorNode;190;-1694.547,-797.6596;Inherit;False;Global;_GrabScreen0;Grab Screen 0;34;0;Create;True;0;0;0;False;0;False;Object;-1;False;False;False;False;2;0;FLOAT2;0,0;False;1;FLOAT;0;False;5;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.GetLocalVarNode;131;-1456,-304;Inherit;False;130;Opacity;1;0;OBJECT;;False;1;FLOAT;0
Node;AmplifyShaderEditor.LerpOp;242;-784,496;Inherit;False;3;0;FLOAT4;0,0,0,0;False;1;FLOAT4;0,0,0,0;False;2;FLOAT;0;False;1;FLOAT4;0
Node;AmplifyShaderEditor.LerpOp;183;-1402.769,-613.0276;Inherit;False;3;0;COLOR;0,0,0,0;False;1;COLOR;0,0,0,0;False;2;FLOAT;0;False;1;COLOR;0
Node;AmplifyShaderEditor.SamplerNode;232;-544,720;Inherit;True;Property;_EmissiveTexture;Emissive Texture;28;0;Create;True;0;0;0;False;0;False;-1;None;None;True;0;False;black;Auto;False;Object;-1;Auto;Texture2D;8;0;SAMPLER2D;;False;1;FLOAT2;0,0;False;2;FLOAT;0;False;3;FLOAT2;0,0;False;4;FLOAT2;0,0;False;5;FLOAT;1;False;6;FLOAT;0;False;7;SAMPLERSTATE;;False;6;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4;FLOAT3;5
Node;AmplifyShaderEditor.RangedFloatNode;235;-512,1104;Inherit;False;Property;_EmissiveIntensity;Emissive Intensity;25;1;[Header];Create;True;1;Emissive;0;0;False;0;False;0;0.2;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.RegisterLocalVarNode;226;-2879.168,-762.3644;Inherit;False;OpacityOutput;-1;True;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.RegisterLocalVarNode;229;-1168,-608;Inherit;False;FinalColor;-1;True;1;0;COLOR;0,0,0,0;False;1;COLOR;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;234;-192,896;Inherit;False;2;2;0;FLOAT3;0,0,0;False;1;FLOAT;0;False;1;FLOAT3;0
Node;AmplifyShaderEditor.RangedFloatNode;247;-304,576;Inherit;False;Constant;_Float0;Float 0;31;0;Create;True;0;0;0;False;0;False;0;0;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.GetLocalVarNode;227;-688,480;Inherit;False;226;OpacityOutput;1;0;OBJECT;;False;1;FLOAT;0
Node;AmplifyShaderEditor.GetLocalVarNode;223;-928,96;Inherit;False;222;Normal;1;0;OBJECT;;False;1;FLOAT3;0
Node;AmplifyShaderEditor.GetLocalVarNode;230;-880,-16;Inherit;False;229;FinalColor;1;0;OBJECT;;False;1;COLOR;0
Node;AmplifyShaderEditor.RangedFloatNode;35;-1040,304;Inherit;False;Property;_Smoothness;Smoothness;22;1;[Header];Create;True;1;Basic Attributes;0;0;False;1;HEADER(Basic Attributes);False;1;1;0;1;0;1;FLOAT;0
Node;AmplifyShaderEditor.StaticSwitch;246;48,880;Inherit;False;Property;_Emissive;Emissive?;26;0;Create;True;0;0;0;False;1;HEADER(Emissive);False;0;0;0;True;;Toggle;2;Key0;Key1;Create;True;True;All;9;1;FLOAT3;0,0,0;False;0;FLOAT3;0,0,0;False;2;FLOAT3;0,0,0;False;3;FLOAT3;0,0,0;False;4;FLOAT3;0,0,0;False;5;FLOAT3;0,0,0;False;6;FLOAT3;0,0,0;False;7;FLOAT3;0,0,0;False;8;FLOAT3;0,0,0;False;1;FLOAT3;0
Node;AmplifyShaderEditor.StandardSurfaceOutputNode;0;176,208;Float;False;True;-1;2;AmplifyShaderEditor.MaterialInspector;0;0;Standard;M_Water_Lake_Amp;False;False;False;False;False;False;False;False;False;False;False;False;False;False;True;False;False;False;False;False;False;Back;0;False;;0;False;;False;0;False;;0;False;;False;0;Transparent;0.5;True;False;0;False;Transparent;;Transparent;ForwardOnly;12;all;True;True;True;True;0;False;;False;0;False;;255;False;;255;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;False;2;15;10;25;False;0.5;True;2;5;False;;10;False;;0;0;False;;0;False;;0;False;;0;False;;0;False;0;0,0,0,0;VertexOffset;True;False;Cylindrical;False;True;Relative;0;;-1;-1;-1;-1;0;False;0;0;False;;-1;0;False;;0;0;0;False;0.1;False;;0;False;;False;17;0;FLOAT3;0,0,0;False;1;FLOAT3;0,0,0;False;2;FLOAT3;0,0,0;False;3;FLOAT;0;False;4;FLOAT;0;False;5;FLOAT;0;False;6;FLOAT3;0,0,0;False;7;FLOAT3;0,0,0;False;8;FLOAT;0;False;9;FLOAT;0;False;10;FLOAT;0;False;13;FLOAT3;0,0,0;False;11;FLOAT3;0,0,0;False;12;FLOAT3;0,0,0;False;16;FLOAT4;0,0,0,0;False;14;FLOAT4;0,0,0,0;False;15;FLOAT3;0,0,0;False;0
WireConnection;12;0;14;0
WireConnection;12;1;11;0
WireConnection;36;0;18;1
WireConnection;36;1;18;3
WireConnection;19;0;36;0
WireConnection;19;1;20;0
WireConnection;69;0;12;0
WireConnection;69;1;75;0
WireConnection;21;0;19;0
WireConnection;21;1;69;0
WireConnection;9;0;10;0
WireConnection;9;1;21;0
WireConnection;9;7;10;1
WireConnection;151;0;9;0
WireConnection;217;0;215;1
WireConnection;217;1;215;3
WireConnection;147;0;146;1
WireConnection;147;1;146;3
WireConnection;218;0;217;0
WireConnection;218;1;216;0
WireConnection;149;0;147;0
WireConnection;149;1;148;0
WireConnection;144;1;150;0
WireConnection;144;2;145;0
WireConnection;155;0;156;0
WireConnection;219;0;218;0
WireConnection;213;0;224;0
WireConnection;170;0;149;0
WireConnection;170;1;144;0
WireConnection;154;0;155;0
WireConnection;154;1;153;0
WireConnection;160;0;159;0
WireConnection;196;0;213;0
WireConnection;152;0;170;0
WireConnection;152;1;154;0
WireConnection;161;0;160;0
WireConnection;161;1;162;0
WireConnection;199;0;198;0
WireConnection;200;0;225;0
WireConnection;200;1;196;0
WireConnection;200;2;197;0
WireConnection;158;0;152;0
WireConnection;158;1;161;0
WireConnection;211;0;199;0
WireConnection;201;0;200;0
WireConnection;174;0;172;0
WireConnection;174;1;173;4
WireConnection;163;1;158;0
WireConnection;204;0;211;0
WireConnection;164;0;163;0
WireConnection;164;1;165;0
WireConnection;176;0;174;0
WireConnection;176;1;175;0
WireConnection;203;0;201;0
WireConnection;205;0;203;0
WireConnection;205;1;204;0
WireConnection;171;0;176;0
WireConnection;169;0;164;0
WireConnection;206;0;205;0
WireConnection;177;0;169;0
WireConnection;177;2;171;0
WireConnection;61;0;59;0
WireConnection;61;1;60;4
WireConnection;37;0;26;1
WireConnection;37;1;26;3
WireConnection;208;0;206;0
WireConnection;208;1;207;0
WireConnection;166;0;177;0
WireConnection;63;0;61;0
WireConnection;63;1;62;0
WireConnection;15;0;11;0
WireConnection;15;1;16;0
WireConnection;28;0;37;0
WireConnection;28;1;27;0
WireConnection;210;0;208;0
WireConnection;65;0;63;0
WireConnection;45;0;43;0
WireConnection;45;1;44;4
WireConnection;30;0;15;0
WireConnection;30;1;28;0
WireConnection;66;0;65;0
WireConnection;66;1;67;0
WireConnection;182;0;179;0
WireConnection;56;0;54;0
WireConnection;56;1;55;4
WireConnection;46;0;45;0
WireConnection;46;1;1;0
WireConnection;25;0;10;0
WireConnection;25;1;30;0
WireConnection;25;7;10;1
WireConnection;127;0;66;0
WireConnection;127;1;136;0
WireConnection;181;0;182;0
WireConnection;181;1;180;0
WireConnection;53;0;56;0
WireConnection;53;1;57;0
WireConnection;4;0;46;0
WireConnection;33;0;9;0
WireConnection;33;1;25;0
WireConnection;178;0;127;0
WireConnection;178;1;181;0
WireConnection;58;0;53;0
WireConnection;6;0;5;0
WireConnection;6;1;3;0
WireConnection;6;2;4;0
WireConnection;39;0;40;0
WireConnection;39;1;33;0
WireConnection;39;2;41;0
WireConnection;237;0;236;1
WireConnection;237;1;236;3
WireConnection;188;0;187;0
WireConnection;188;1;189;0
WireConnection;64;0;178;0
WireConnection;64;1;58;0
WireConnection;7;0;6;0
WireConnection;222;0;39;0
WireConnection;238;0;237;0
WireConnection;238;1;233;0
WireConnection;186;0;192;0
WireConnection;186;1;188;0
WireConnection;130;0;64;0
WireConnection;128;0;7;0
WireConnection;128;1;129;0
WireConnection;128;2;137;0
WireConnection;239;0;238;0
WireConnection;239;1;222;0
WireConnection;244;0;243;0
WireConnection;167;0;128;0
WireConnection;167;1;168;0
WireConnection;190;0;186;0
WireConnection;242;0;238;0
WireConnection;242;1;239;0
WireConnection;242;2;244;0
WireConnection;183;0;190;0
WireConnection;183;1;167;0
WireConnection;183;2;131;0
WireConnection;232;1;242;0
WireConnection;226;0;58;0
WireConnection;229;0;183;0
WireConnection;234;0;232;5
WireConnection;234;1;235;0
WireConnection;246;1;247;0
WireConnection;246;0;234;0
WireConnection;0;0;230;0
WireConnection;0;1;223;0
WireConnection;0;2;246;0
WireConnection;0;4;35;0
WireConnection;0;9;227;0
ASEEND*/
//CHKSM=C649BF28DFFCA8C20323E43141963D77055EC104