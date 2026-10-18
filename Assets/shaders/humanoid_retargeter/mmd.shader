HEADER
{
	Description = "MMD toon ramp and sphere material";
}

FEATURES
{
	#include "common/features.hlsl"
}

MODES
{
	Forward();
	Depth( S_MODE_DEPTH );
}

COMMON
{
	#define CUSTOM_MATERIAL_INPUTS
	#include "common/shared.hlsl"
}

struct VertexInput
{
	#include "common/vertexinput.hlsl"
};

struct PixelInput
{
	#include "common/pixelinput.hlsl"
	float2 mmdSphereUv : TEXCOORD8;
};

VS
{
	#include "common/vertex.hlsl"

	PixelInput MainVs( VertexInput input )
	{
		PixelInput output = FinalizeVertex( ProcessVertex( input ) );
		output.mmdSphereUv = input.vTexCoord2;
		return output;
	}
}

PS
{
	#include "common/pixel.hlsl"
	SamplerState g_sWrap < Filter( ANISO ); AddressU( WRAP ); AddressV( WRAP ); >;
	SamplerState g_sClamp < Filter( BILINEAR ); AddressU( CLAMP ); AddressV( CLAMP ); >;
	CreateInputTexture2D( Diffuse, Srgb, 8, "None", "_color", "MMD,0/0", Default4( 1, 1, 1, 1 ) );
	CreateInputTexture2D( Sphere, Srgb, 8, "None", "_sphere", "MMD,0/1", Default4( 1, 1, 1, 1 ) );
	CreateInputTexture2D( Toon, Srgb, 8, "None", "_toon", "MMD,0/2", Default4( 1, 1, 1, 1 ) );
	Texture2D g_tDiffuse < Channel( RGBA, Box( Diffuse ), Srgb ); OutputFormat( BC7 ); SrgbRead( true ); >;
	Texture2D g_tSphere < Channel( RGBA, Box( Sphere ), Srgb ); OutputFormat( BC7 ); SrgbRead( true ); >;
	Texture2D g_tToon < Channel( RGBA, Box( Toon ), Srgb ); OutputFormat( BC7 ); SrgbRead( true ); >;
	float4 g_vDiffuse < Attribute( "MmdDiffuse" ); UiType( Color ); Default4( 1, 1, 1, 1 ); >;
	float4 g_vTextureTint < Attribute( "MmdTextureTint" ); Default4( 1, 1, 1, 1 ); >;
	float4 g_vSphereTint < Attribute( "MmdSphereTint" ); Default4( 1, 1, 1, 1 ); >;
	float4 g_vToonTint < Attribute( "MmdToonTint" ); Default4( 1, 1, 1, 1 ); >;
	float3 g_vSpecular < Attribute( "MmdSpecular" ); Default3( 0, 0, 0 ); >;
	float3 g_vAmbient < Attribute( "MmdAmbient" ); Default3( 0, 0, 0 ); >;
	float g_flSpecularPower < Attribute( "MmdSpecularPower" ); Default( 1 ); >;
	float g_flSphereMode < Default( 0 ); Range( 0, 3 ); >;
	float g_flHasToon < Default( 0 ); Range( 0, 1 ); >;
	float g_flUnlit < Default( 0 ); Range( 0, 1 ); >;
	float3 g_vLightDirection < Default3( -0.5, -0.5, 1 ); >;

	float4 MainPs( PixelInput input ) : SV_Target0
	{
		Material material = Material::Init( input );
		float3 normal = normalize( input.vNormalWs );
		float light = saturate( dot( normal, normalize( g_vLightDirection ) ) * 0.5 + 0.5 );
		float3 ramp = g_flHasToon > 0.5
			? g_tToon.Sample( g_sClamp, float2( 0.5, 1.0 - light ) ).rgb
			: lerp( 0.65.xxx, 1.0.xxx, smoothstep( 0.45, 0.5, light ) );
		ramp *= g_vToonTint.rgb;
		float4 diffuse = g_tDiffuse.Sample( g_sWrap, input.vTextureCoords.xy ) * g_vTextureTint * g_vDiffuse;
		float3 viewNormal = mul( (float3x3)g_matWorldToView, normal );
		float2 sphereUv = viewNormal.xy * float2( 0.5, -0.5 ) + 0.5;
		if ( g_flSphereMode >= 2.5 )
			sphereUv = input.mmdSphereUv;
		float3 sphere = g_tSphere.Sample( g_sWrap, sphereUv ).rgb * g_vSphereTint.rgb;
		float3 halfVector = normalize( normalize( g_vLightDirection ) + normalize( g_vCameraPositionWs - input.vPositionWithOffsetWs.xyz ) );
		float3 color = diffuse.rgb * ramp + g_vAmbient * diffuse.rgb
			+ g_vSpecular * pow( saturate( dot( normal, halfVector ) ), max( g_flSpecularPower, 0.001 ) );
		if ( g_flUnlit > 0.5 )
			color = diffuse.rgb;
		if ( (g_flSphereMode > 0.5 && g_flSphereMode < 1.5) || g_flSphereMode >= 2.5 )
			color *= sphere;
		else if ( g_flSphereMode >= 1.5 && g_flSphereMode < 2.5 )
			color += sphere;
		material.Albedo = 0;
		material.Emission = color;
		material.Opacity = diffuse.a;
		material.Roughness = 1;
		material.Metalness = 0;
		return ShadingModelStandard::Shade( material );
	}
}
