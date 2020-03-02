#ifndef INCLUDE_HLSLI
#define INCLUDE_HLSLI

//#define DEMO_MODE

// Maximum number of fragments to be sorted per pixel
#ifndef MAX_FRAGMENTS
#define MAX_FRAGMENTS 32
#endif

#define CMP_NEVER        1
#define CMP_LESS         2
#define CMP_EQUAL        3
#define CMP_LESSEQUAL    4
#define CMP_GREATER      5
#define CMP_NOTEQUAL     6
#define CMP_GREATEREQUAL 7
#define CMP_ALWAYS       8

// D3DBLEND enum
#define BLEND_ZERO            1
#define BLEND_ONE             2
#define BLEND_SRCCOLOR        3
#define BLEND_INVSRCCOLOR     4
#define BLEND_SRCALPHA        5
#define BLEND_INVSRCALPHA     6
#define BLEND_DESTALPHA       7
#define BLEND_INVDESTALPHA    8
#define BLEND_DESTCOLOR       9
#define BLEND_INVDESTCOLOR    10
#define BLEND_SRCALPHASAT     11

// D3DBLENDOP
#define BLENDOP_ADD         1
#define BLENDOP_SUBTRACT    2
#define BLENDOP_REVSUBTRACT 3
#define BLENDOP_MIN         4
#define BLENDOP_MAX         5

#define TA_SELECTMASK        0x0000000f  // mask for arg selector
#define TA_DIFFUSE           0x00000000  // select diffuse color (read only)
#define TA_CURRENT           0x00000001  // select stage destination register (read/write)
#define TA_TEXTURE           0x00000002  // select texture color (read only)
#define TA_TFACTOR           0x00000003  // select D3DRS_TEXTUREFACTOR (read only)
#define TA_SPECULAR          0x00000004  // select specular color (read only)
#define TA_TEMP              0x00000005  // select temporary register color (read/write)
#define TA_COMPLEMENT        0x00000010  // take 1.0 - x (read modifier)
#define TA_ALPHAREPLICATE    0x00000020  // replicate alpha to color components (read modifier)

#define TOP_DISABLE                    1
#define TOP_SELECTARG1                 2
#define TOP_SELECTARG2                 3
#define TOP_MODULATE                   4
#define TOP_MODULATE2X                 5
#define TOP_MODULATE4X                 6
#define TOP_ADD                        7
#define TOP_ADDSIGNED                  8
#define TOP_ADDSIGNED2X                9
#define TOP_SUBTRACT                  10
#define TOP_ADDSMOOTH                 11
#define TOP_BLENDDIFFUSEALPHA         12
#define TOP_BLENDTEXTUREALPHA         13
#define TOP_BLENDFACTORALPHA          14
#define TOP_BLENDTEXTUREALPHAPM       15
#define TOP_BLENDCURRENTALPHA         16
#define TOP_PREMODULATE               17
#define TOP_MODULATEALPHA_ADDCOLOR    18
#define TOP_MODULATECOLOR_ADDALPHA    19
#define TOP_MODULATEINVALPHA_ADDCOLOR 20
#define TOP_MODULATEINVCOLOR_ADDALPHA 21
#define TOP_BUMPENVMAP                22
#define TOP_BUMPENVMAPLUMINANCE       23
#define TOP_DOTPRODUCT3               24
#define TOP_MULTIPLYADD               25
#define TOP_LERP                      26

// Magic number to consider a null-entry.
static const uint FRAGMENT_LIST_NULL = 0xFFFFFFFF;

// Fragment list node.
/*
 * TODO: replace "flags" with a "context" (index) -- see below
 *
 * rather than 32-bit flags, store a 32-bit index into an array
 * of structures that have all of the amenities described by the
 * current flags; this could potentially save on memory in the long-run
 */
struct OitNode
{
	float depth; // fragment depth
	uint  color; // 32-bit packed fragment color
	uint  flags; // 16 bit draw call number, 4 bit blend op, 4 bit source blend, 4 bit destination blend
	uint  next;  // index of the next entry, or FRAGMENT_LIST_NULL
};

// TODO: per-pixel link count
// TODO: test append buffer again, increase max fragments

#ifdef NODE_WRITE

// Read/write mode.

globallycoherent RWTexture2D<uint>           FragListHead  : register(u1);
globallycoherent RWTexture2D<uint>           FragListCount : register(u2);
globallycoherent RWStructuredBuffer<OitNode> FragListNodes : register(u3);

#else

// Read-only mode.

Texture2D<uint>           FragListHead  : register(t0);
Texture2D<uint>           FragListCount : register(t1);
StructuredBuffer<OitNode> FragListNodes : register(t2);
Texture2D                 BackBuffer    : register(t3);
Texture2D                 DepthBuffer   : register(t4);

#endif

// from D3DX_DXGIFormatConvert.inl

uint float_to_uint(float _V, float _Scale)
{
	return (uint)floor(_V * _Scale + 0.5f);
}

float4 unorm_to_float4(uint packedInput)
{
	precise float4 unpackedOutput;
	unpackedOutput.x = (float)(packedInput & 0x000000ff) / 255;
	unpackedOutput.y = (float)(((packedInput >> 8) & 0x000000ff)) / 255;
	unpackedOutput.z = (float)(((packedInput >> 16) & 0x000000ff)) / 255;
	unpackedOutput.w = (float)(packedInput >> 24) / 255;
	return unpackedOutput;
}

uint float4_to_unorm(precise float4 unpackedInput)
{
	uint packedOutput;
	packedOutput = ((float_to_uint(saturate(unpackedInput.x), 255)) |
		(float_to_uint(saturate(unpackedInput.y), 255) << 8) |
		(float_to_uint(saturate(unpackedInput.z), 255) << 16) |
		(float_to_uint(saturate(unpackedInput.w), 255) << 24));
	return packedOutput;
}

#endif
