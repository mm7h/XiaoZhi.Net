using System;
using System.Buffers.Binary;
using System.IO;
using System.Text.Json.Nodes;
using XiaoZhi.Net.Server.I18n;

namespace XiaoZhi.Net.Server.Resources.Fonts;

internal sealed class CbinFont
{
    private readonly byte[] _data;
    private readonly int _bitmap;
    private readonly int _descriptors;
    private readonly int _cmaps;
    private readonly int _cmapCount;

    public CbinFont(byte[] data)
    {
        this._data = data;
        int descriptor = checked((int)this.UInt32(24));
        this._bitmap = checked(descriptor + (int)this.UInt32(descriptor));
        this._descriptors = checked(descriptor + (int)this.UInt32(descriptor + 4));
        this._cmaps = checked(descriptor + (int)this.UInt32(descriptor + 8));
        ushort flags = this.UInt16(descriptor + 18);
        this._cmapCount = flags & 0x1ff;
        this.Bpp = (flags >> 9) & 0xf;
        if (this.Bpp is not (1 or 4) || ((flags >> 14) & 3) != 0 || this.Bytes(descriptor + 20, 1)[0] != 0)
        {
            throw new InvalidDataException(Lang.CbinFont_Ctor_UnsupportedBitmapFormat);
        }
        this.Bytes(this._cmaps, checked(this._cmapCount * 20));
    }

    public int Bpp { get; }

    public JsonObject? GetGlyph(int codepoint)
    {
        int glyphId = this.FindGlyph(codepoint);
        if (glyphId == 0)
        {
            return null;
        }
        int offset = checked(this._descriptors + glyphId * 16);
        int width = this.UInt16(offset + 8);
        int height = this.UInt16(offset + 10);
        if (width > 64 || height > 64)
        {
            throw new InvalidDataException(Lang.CbinFont_GetGlyph_GlyphSizeExceeded);
        }
        int bitmapStart = checked(this._bitmap + (int)this.UInt32(offset));
        int byteCount = (width * height * this.Bpp + 7) / 8;
        return new JsonObject
        {
            ["codepoint"] = codepoint,
            ["adv_w"] = this.UInt32(offset + 4),
            ["box_w"] = width,
            ["box_h"] = height,
            ["ofs_x"] = BinaryPrimitives.ReadInt16LittleEndian(this.Bytes(offset + 12, 2)),
            ["ofs_y"] = BinaryPrimitives.ReadInt16LittleEndian(this.Bytes(offset + 14, 2)),
            ["bitmap"] = Convert.ToBase64String(this.Bytes(bitmapStart, byteCount))
        };
    }

    private int FindGlyph(int codepoint)
    {
        for (int map = 0; map < this._cmapCount; map++)
        {
            int pointer = this._cmaps + map * 20;
            long relative = codepoint - (long)this.UInt32(pointer);
            if (relative < 0 || relative >= this.UInt16(pointer + 4))
            {
                continue;
            }
            int start = this.UInt16(pointer + 6);
            uint glyphOffsets = this.UInt32(pointer + 12);
            int kind = this.Bytes(pointer + 18, 1)[0];
            if (kind == 2)
            {
                return checked(start + (int)relative);
            }
            if (kind == 0)
            {
                return checked(start + (glyphOffsets == 0 ? (int)relative : this.Bytes(checked(this._cmaps + (int)glyphOffsets + (int)relative), 1)[0]));
            }
            if (kind is not (1 or 3))
            {
                throw new InvalidDataException(Lang.CbinFont_FindGlyph_UnsupportedCmapKind);
            }
            int unicode = checked(this._cmaps + (int)this.UInt32(pointer + 8));
            int low = 0;
            int high = this.UInt16(pointer + 16) - 1;
            while (low <= high)
            {
                int middle = low + (high - low) / 2;
                int value = this.UInt16(checked(unicode + middle * 2));
                if (value < relative)
                {
                    low = middle + 1;
                }
                else if (value > relative)
                {
                    high = middle - 1;
                }
                else
                {
                    int offset = kind == 3 || glyphOffsets == 0 ? middle : this.UInt16(checked(this._cmaps + (int)glyphOffsets + middle * 2));
                    return checked(start + offset);
                }
            }
        }
        return 0;
    }

    private uint UInt32(int offset) => BinaryPrimitives.ReadUInt32LittleEndian(this.Bytes(offset, 4));
    private ushort UInt16(int offset) => BinaryPrimitives.ReadUInt16LittleEndian(this.Bytes(offset, 2));
    private ReadOnlySpan<byte> Bytes(int offset, int count)
    {
        if (offset < 0 || count < 0 || offset > this._data.Length - count)
        {
            throw new InvalidDataException(Lang.CbinFont_Bytes_OutOfRange);
        }
        return this._data.AsSpan(offset, count);
    }
}
