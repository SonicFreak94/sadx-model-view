#include "include.hlsli"
#include "common.hlsli" // for PerSceneBuffer

/*
	This shader draws a full screen triangle onto
	which it composites the stored alpha fragments.
*/

struct VertexOutput
{
	float4 position : SV_POSITION;
};

VertexOutput vs_main(uint vertexId : SV_VertexID)
{
	VertexOutput output;
	float2 texcoord = float2((vertexId << 1) & 2, vertexId & 2);
	output.position = float4(texcoord * float2(2.0f, -2.0f) + float2(-1.0f, 1.0f), 0.0f, 1.0f);
	return output;
}

float4 get_blend_factor(uint mode, float4 source, float4 destination)
{
	switch (mode)
	{
		default: // error state
			return float4(1, 0, 0, 1);
		case BLEND_ZERO:
			return float4(0, 0, 0, 0);
		case BLEND_ONE:
			return float4(1, 1, 1, 1);
		case BLEND_SRCCOLOR:
			return source;
		case BLEND_INVSRCCOLOR:
			return 1.0f - source;
		case BLEND_SRCALPHA:
			return source.aaaa;
		case BLEND_INVSRCALPHA:
			return 1.0f - source.aaaa;
		case BLEND_DESTALPHA:
			return destination.aaaa;
		case BLEND_INVDESTALPHA:
			return 1.0f - destination.aaaa;
		case BLEND_DESTCOLOR:
			return destination;
		case BLEND_INVDESTCOLOR:
			return 1.0f - destination;
		case BLEND_SRCALPHASAT:
			float f = min(source.a, 1 - destination.a);
			return float4(f, f, f, 1);
	}
}

float4 blend_colors(uint blendOp, uint srcBlend, uint dstBlend, float4 sourceColor, float4 destinationColor)
{
	float4 src = get_blend_factor(srcBlend, sourceColor, destinationColor);
	float4 dst = get_blend_factor(dstBlend, sourceColor, destinationColor);

	float4 srcResult = sourceColor * src;
	float4 dstResult = destinationColor * dst;

	switch (blendOp)
	{
		default:
			return float4(1, 0, 0, 1);

		case BLENDOP_ADD:
			return srcResult + dstResult;

		case BLENDOP_REVSUBTRACT: // TODO: BLENDOP_REVSUBTRACT ???
		case BLENDOP_SUBTRACT:
			return srcResult - dstResult;

		case BLENDOP_MIN:
			return min(srcResult, dstResult);

		case BLENDOP_MAX:
			return max(srcResult, dstResult);
	}
}

float4 ps_main(VertexOutput input) : SV_TARGET
{
	int2 pos = int2(input.position.xy);

#ifdef DEMO_MODE
	const int center = screen_dimensions.x / 2;
	const bool should_sort = pos.x >= center;

	if (abs(center.x - pos.x) < 4)
	{
		return float4(1, 0, 0, 1);
	}
#endif

	float4 backBufferColor = BackBuffer[pos];
	uint index = FragListHead[pos];

	// TODO: LotR: RotK is bailing here!
	if (index == FRAGMENT_LIST_NULL)
	{
		return backBufferColor;
	}

	float opaque_depth = DepthBuffer[pos].r;

	uint indices[MAX_FRAGMENTS];
	uint count = 0;

	while (index != FRAGMENT_LIST_NULL && count < MAX_FRAGMENTS)
	{
		const OitNode node_i = FragListNodes[index];

		if (node_i.depth > opaque_depth)
		{
			index = node_i.next;
			continue;
		}

		if (!count)
		{
			indices[0] = index;
			++count;
			index = node_i.next;
			continue;
		}

		int j = count;

	//#define DISABLE_SORT
	#ifndef DISABLE_SORT
		OitNode node_j = FragListNodes[indices[j - 1]];

		uint draw_call_i = (node_i.flags >> 16) & 0xFFFF;
		uint draw_call_j = (node_j.flags >> 16) & 0xFFFF;

		while (j > 0 &&
		       #ifdef DEMO_MODE
		       ((should_sort && node_j.depth > node_i.depth) || ((!should_sort || node_j.depth == node_i.depth) && draw_call_j < draw_call_i))
		       #else
		       (node_j.depth > node_i.depth || (node_j.depth == node_i.depth && draw_call_j < draw_call_i))
		       #endif
		)
		{
			uint temp = indices[j];
			indices[j] = indices[--j];
			indices[j] = temp;
			node_j = FragListNodes[indices[j - 1]];
			draw_call_j = (node_j.flags >> 16) & 0xFFFF;
		}
	#endif

		indices[j] = index;
		index = node_i.next;
		++count;
	}

	if (count == 0)
	{
		return backBufferColor;
	}

	float4 final = backBufferColor;

	for (int i = count - 1; i >= 0; i--)
	{
		const OitNode fragment = FragListNodes[indices[i]];
		uint blend = fragment.flags;

		uint blendOp   = (blend >> 8) & 0xF;
		uint srcBlend  = (blend >> 4) & 0xF;
		uint destBlend = blend & 0xF;

		float4 color = unorm_to_float4(fragment.color);
		final = blend_colors(blendOp, srcBlend, destBlend, color, final);
	}

	return float4(final.rgb, 1);
}
