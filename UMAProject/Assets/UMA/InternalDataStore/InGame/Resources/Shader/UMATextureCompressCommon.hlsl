#ifndef UMA_TEXTURE_COMPRESS_COMMON_INCLUDED
#define UMA_TEXTURE_COMPRESS_COMMON_INCLUDED

// Shared input/output for the UMA block compression kernels.
//
// Each thread encodes one 4x4 block of one mip level of _Source. The encoded
// block is written either to an integer texture that has one texel per block
// (copied afterwards into the compressed texture with Graphics.CopyTexture),
// or, with UMA_BLOCKS_TO_BUFFER, to a buffer laid out exactly like the mip's
// compressed data (read back and uploaded on devices without that copy).

// OpenGL ES cannot bind a two-channel 32-bit integer image, and UMA does not rely on its
// texture copies between integer and compressed formats, so its blocks always go to the buffer.
#if defined(SHADER_API_GLES3) && !defined(UMA_BLOCKS_TO_BUFFER)
#define UMA_BLOCKS_TO_BUFFER 1
#endif

Texture2D<float4> _Source;
int _SourceMip;
int2 _SourceSize;   // size of the mip level in texels
int2 _BlockCount;   // size of the mip level in blocks

#if defined(UMA_BLOCKS_TO_BUFFER)
RWStructuredBuffer<uint> _BlockBuffer;
int _BlockBufferOffset;   // first uint of this mip level
#else
RWTexture2D<uint4> _Blocks16;   // 16-byte blocks (BC3, ETC2 RGBA8)
RWTexture2D<uint2> _Blocks8;    // 8-byte blocks (BC1, ETC2 RGB8)
#endif

// Texels in row order: index = y * 4 + x. Mip levels narrower than a block
// repeat their edge texels.
void LoadBlock(uint2 block, out float4 texels[16])
{
    int2 origin = int2(block) * 4;
    int2 last = _SourceSize - 1;
    for (int i = 0; i < 16; i++)
    {
        int2 p = min(origin + int2(i & 3, i >> 2), last);
        texels[i] = saturate(_Source.Load(int3(p, _SourceMip)));
    }
}

void StoreBlock16(uint2 block, uint4 data)
{
#if defined(UMA_BLOCKS_TO_BUFFER)
    uint index = (uint)_BlockBufferOffset + (block.y * (uint)_BlockCount.x + block.x) * 4u;
    _BlockBuffer[index] = data.x;
    _BlockBuffer[index + 1u] = data.y;
    _BlockBuffer[index + 2u] = data.z;
    _BlockBuffer[index + 3u] = data.w;
#else
    _Blocks16[block] = data;
#endif
}

void StoreBlock8(uint2 block, uint2 data)
{
#if defined(UMA_BLOCKS_TO_BUFFER)
    uint index = (uint)_BlockBufferOffset + (block.y * (uint)_BlockCount.x + block.x) * 2u;
    _BlockBuffer[index] = data.x;
    _BlockBuffer[index + 1u] = data.y;
#else
    _Blocks8[block] = data;
#endif
}

#endif
