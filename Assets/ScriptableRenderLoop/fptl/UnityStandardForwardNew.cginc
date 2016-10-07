#ifndef UNITY_STANDARD_FORWARDNEW_INCLUDED
#define UNITY_STANDARD_FORWARDNEW_INCLUDED


// NOTE: had to split shadow functions into separate file,
// otherwise compiler gives trouble with LIGHTING_COORDS macro (in UnityStandardCore.cginc)

#include "UnityStandardConfig.cginc"
#include "UnityStandardCore.cginc"

struct VertexOutputForwardNew
{
	float4 pos							: SV_POSITION;
	float4 tex							: TEXCOORD0;
	half4 ambientOrLightmapUV			: TEXCOORD1;	// SH or Lightmap UV
	half4 tangentToWorldAndParallax[3]	: TEXCOORD2;	// [3x3:tangentToWorld | 1x3:empty]

	LIGHTING_COORDS(5,6)
	UNITY_FOG_COORDS(7)

	UNITY_VERTEX_OUTPUT_STEREO
};



VertexOutputForwardNew vertForward(VertexInput v) 
{ 
	UNITY_SETUP_INSTANCE_ID(v);
	VertexOutputForwardNew o;
	UNITY_INITIALIZE_OUTPUT(VertexOutputForwardNew, o);
	UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

	float4 posWorld = mul(unity_ObjectToWorld, v.vertex);
	o.pos = UnityObjectToClipPos(v.vertex);

	o.tex = TexCoords(v);
	
	float3 normalWorld = UnityObjectToWorldNormal(v.normal);
	#ifdef _TANGENT_TO_WORLD
		float4 tangentWorld = float4(UnityObjectToWorldDir(v.tangent.xyz), v.tangent.w);

		float3x3 tangentToWorld = CreateTangentToWorldPerVertex(normalWorld, tangentWorld.xyz, tangentWorld.w);
		o.tangentToWorldAndParallax[0].xyz = tangentToWorld[0];
		o.tangentToWorldAndParallax[1].xyz = tangentToWorld[1];
		o.tangentToWorldAndParallax[2].xyz = tangentToWorld[2];
	#else
		o.tangentToWorldAndParallax[0].xyz = 0;
		o.tangentToWorldAndParallax[1].xyz = 0;
		o.tangentToWorldAndParallax[2].xyz = normalWorld;
	#endif
	
	o.ambientOrLightmapUV = VertexGIForward(v, posWorld, normalWorld);
	
	UNITY_TRANSFER_FOG(o,o.pos);

	return o;
}


static FragmentCommonData gdata;


float3 EvalMaterial(UnityLight light, UnityIndirect ind)
{
	return UNITY_BRDF_PBS(gdata.diffColor, gdata.specColor, gdata.oneMinusReflectivity, gdata.smoothness, gdata.normalWorld, -gdata.eyeVec, light, ind);
}

#define ENABLE_DEPTH_TEXTURE_BACKPLANE

#include "TiledLightingTemplate.hlsl"



half4 fragForward(VertexOutputForwardNew i) : SV_Target 
{ 
#ifdef LEFT_HAND_COORDINATES
	float linZ = i.pos.w;
#else
	float linZ = -i.pos.w;
#endif

	float3 vP = GetViewPosFromLinDepth(i.pos.xy, linZ);
	float3 vPw = mul(g_mViewToWorld, float4(vP,1.0)).xyz;
	float3 Vworld = normalize(mul((float3x3) g_mViewToWorld, -vP).xyz);		//unity_CameraToWorld

#ifdef _PARALLAXMAP
	half3 tangent = i.tangentToWorldAndParallax[0].xyz;
	half3 bitangent = i.tangentToWorldAndParallax[1].xyz;
	half3 normal = i.tangentToWorldAndParallax[2].xyz;
	float3 vDirForParallax = float3( dot(tangent, Vworld), dot(bitangent, Vworld), dot(normal, Vworld));
#else
	float3 vDirForParallax = Vworld;
#endif
	gdata = FragmentSetup(i.tex, -Vworld, vDirForParallax, i.tangentToWorldAndParallax, vPw);		// eyeVec = -Vworld


	uint2 pixCoord = ((uint2) i.pos.xy);
	
	//float atten = 0.0;
	//half occlusion = Occlusion(i.tex.xy);
	//UnityGI gi = FragmentGI (gdata, occlusion, i.ambientOrLightmapUV, atten, mainLight);

	float3 res = 0;
	res += ExecuteLightListTiled(pixCoord, vP, vPw, Vworld);

	// don't really have a handle on this yet
	//UnityLight mainLight = MainLight ();
	//res += UNITY_BRDF_GI (gdata.diffColor, gdata.specColor, gdata.oneMinusReflectivity, gdata.smoothness, gdata.normalWorld, -gdata.eyeVec, occlusion, gi);
	res += Emission(i.tex.xy);

	//UNITY_APPLY_FOG(i.fogCoord, res);
	return OutputForward (float4(res,1.0), gdata.alpha);
}

#endif // UNITY_STANDARD_SHADOW_INCLUDED
