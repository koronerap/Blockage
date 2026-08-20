using System.Text;
using EditorApp.Core.Voxels;

namespace EditorApp.Core.Tests.Mimicraft;

/// <summary>A piece as it came back out of the bytes.</summary>
public sealed class DecodedPiece
{
    public string Id { get; set; } = string.Empty;

    public Int3 BoxMin { get; set; }

    public Int3 BoxSize { get; set; }

    /// <summary>Cell index to colour, in the box's own coordinates.</summary>
    public Dictionary<long, Color32> Voxels { get; } = [];

    /// <summary>(cell index, Mimicraft face number) to colour.</summary>
    public Dictionary<(long Index, byte Face), Color32> Faces { get; } = [];

    public int RunCount { get; set; }
}

public sealed class DecodedBody
{
    public float VoxelSize { get; set; }

    public List<Color32> Palette { get; } = [];

    public List<DecodedPiece> Pieces { get; } = [];
}

/// <summary>
/// A reader written from EditorCodec.md and Mimicraft's own decoder, kept deliberately separate from
/// the writer.
///
/// Round-tripping through the same code would only prove the writer agrees with itself. What has to
/// be true is that it agrees with a reader that was told the rules independently — so every
/// rejection Mimicraft makes is made here too, and a test that encodes something the real decoder
/// would refuse fails here rather than in Unity.
/// </summary>
public static class MimicraftReader
{
    public sealed class RejectedException(string message) : Exception(message);

    public static DecodedBody ReadBody(byte[] data)
    {
        int offset = 0;
        byte format = data[offset++];
        if (format != 0x01)
        {
            throw new RejectedException($"Only the raw frame is written; got 0x{format:X2}.");
        }

        var body = new DecodedBody { VoxelSize = ReadSingle(data, ref offset) };

        uint pieceCount = ReadVarint(data, ref offset);
        uint paletteCount = ReadVarint(data, ref offset);

        for (uint i = 0; i < paletteCount; i++)
        {
            body.Palette.Add(new Color32(data[offset], data[offset + 1], data[offset + 2]));
            offset += 3;
        }

        for (uint i = 0; i < pieceCount; i++)
        {
            body.Pieces.Add(ReadPiece(data, ref offset, body.Palette));
        }

        if (offset != data.Length)
        {
            throw new RejectedException($"{data.Length - offset} bytes left over after the last piece.");
        }

        return body;
    }

    private static DecodedPiece ReadPiece(byte[] data, ref int offset, List<Color32> palette)
    {
        var piece = new DecodedPiece();

        // Position and rotation are read and discarded, exactly as Mimicraft does.
        for (int i = 0; i < 7; i++)
        {
            ReadSingle(data, ref offset);
        }

        piece.BoxMin = new Int3(ReadInt16(data, ref offset), ReadInt16(data, ref offset), ReadInt16(data, ref offset));
        piece.BoxSize = new Int3(ReadUInt16(data, ref offset), ReadUInt16(data, ref offset), ReadUInt16(data, ref offset));

        int sizeX = piece.BoxSize.X;
        int sizeY = piece.BoxSize.Y;
        int sizeZ = piece.BoxSize.Z;

        // The stock game refuses a piece over 64 on an axis, but that number is in its own source
        // and this project's copy has been raised, so it is not a rule of the format. What is left
        // is the field itself: the reader gets whatever a uint16 could hold, and no more.
        long cells = (long)sizeX * sizeY * sizeZ;

        uint runCount = ReadVarint(data, ref offset);
        piece.RunCount = (int)runCount;

        long previousEnd = 0;
        for (uint i = 0; i < runCount; i++)
        {
            uint gap = ReadVarint(data, ref offset);
            uint length = ReadVarint(data, ref offset);

            long start = previousEnd + gap;
            if (length == 0)
            {
                throw new RejectedException("A run of zero length.");
            }

            if (start + length > cells)
            {
                throw new RejectedException("A run runs past the end of the box.");
            }

            // The rule that is easiest to break by accident: x wraps but the index does not, so a
            // run that carried on into the next row would look continuous and be wrong.
            int xStart = (int)(start % sizeX);
            if (xStart + length > sizeX)
            {
                throw new RejectedException($"A run crosses a row: starts at x={xStart}, length {length}, row is {sizeX}.");
            }

            Color32 color = ReadColor(data, ref offset, palette);
            for (long c = start; c < start + length; c++)
            {
                if (!piece.Voxels.TryAdd(c, color))
                {
                    throw new RejectedException($"Cell {c} written twice.");
                }
            }

            previousEnd = start + length;
        }

        uint faceCount = ReadVarint(data, ref offset);
        if (faceCount > (long)piece.Voxels.Count * 6)
        {
            throw new RejectedException($"{faceCount} face entries for {piece.Voxels.Count} voxels.");
        }

        long previousIndex = 0;
        int previousFace = -1;
        bool first = true;

        for (uint i = 0; i < faceCount; i++)
        {
            uint gap = ReadVarint(data, ref offset);
            byte face = data[offset++];

            if (face >= 6)
            {
                throw new RejectedException($"Face number {face}.");
            }

            long index = previousIndex + gap;
            if (index >= cells)
            {
                throw new RejectedException("A face entry past the end of the box.");
            }

            if (!first && index == previousIndex && face <= previousFace)
            {
                throw new RejectedException($"Face entries out of order at cell {index}.");
            }

            Color32 color = ReadColor(data, ref offset, palette);

            if (!piece.Voxels.ContainsKey(index))
            {
                throw new RejectedException($"A face painted on cell {index}, which holds no voxel.");
            }

            piece.Faces[(index, face)] = color;

            previousIndex = index;
            previousFace = face;
            first = false;
        }

        return piece;
    }

    public static (string Name, string RigId, List<DecodedPiece> Parts) ReadCharacterFile(byte[] data)
    {
        int offset = 0;
        Expect(data, ref offset, "MCF");

        if (data[offset++] != 1)
        {
            throw new RejectedException("Wrong version byte.");
        }

        string name = ReadString(data, ref offset);
        string rigId = ReadString(data, ref offset);

        int payloadLength = ReadInt32(data, ref offset);
        if (payloadLength < 0 || payloadLength > data.Length - offset)
        {
            throw new RejectedException("Payload length does not fit the file.");
        }

        var payload = new byte[payloadLength];
        Array.Copy(data, offset, payload, 0, payloadLength);
        offset += payloadLength;

        if (offset != data.Length)
        {
            throw new RejectedException("Bytes left over after the payload.");
        }

        return (name, rigId, payloadLength == 0 ? [] : ReadCharacterPayload(payload));
    }

    public static List<DecodedPiece> ReadCharacterPayload(byte[] data)
    {
        int offset = 0;
        Expect(data, ref offset, "MCC");

        if (data[offset++] != 1)
        {
            throw new RejectedException("Wrong version byte.");
        }

        uint partCount = ReadVarint(data, ref offset);
        if (partCount > 64)
        {
            throw new RejectedException($"{partCount} parts.");
        }

        var ids = new List<string>();
        for (uint i = 0; i < partCount; i++)
        {
            uint length = ReadVarint(data, ref offset);
            if (length > 64)
            {
                throw new RejectedException($"A part id of {length} bytes.");
            }

            ids.Add(Encoding.UTF8.GetString(data, offset, (int)length));
            offset += (int)length;
        }

        uint bodyLength = ReadVarint(data, ref offset);
        var body = new byte[bodyLength];
        Array.Copy(data, offset, body, 0, (int)bodyLength);
        offset += (int)bodyLength;

        if (offset != data.Length)
        {
            throw new RejectedException("Bytes left over after the body.");
        }

        DecodedBody decoded = ReadBody(body);

        // The check Mimicraft makes and the reason the two halves are built from one list.
        if (decoded.Pieces.Count != ids.Count)
        {
            throw new RejectedException($"{ids.Count} ids but {decoded.Pieces.Count} pieces.");
        }

        for (int i = 0; i < ids.Count; i++)
        {
            decoded.Pieces[i].Id = ids[i];
        }

        return decoded.Pieces;
    }

    public sealed record DecodedWeapon(
        string Id,
        (float X, float Y, float Z) LeftGrip,
        (float X, float Y, float Z) RightGrip,
        (float X, float Y, float Z) Muzzle,
        bool HasPoints);

    public static (List<DecodedWeapon> Weapons, List<DecodedPiece> Pieces) ReadWeaponFile(byte[] data)
    {
        int offset = 0;
        Expect(data, ref offset, "MWS");

        if (data[offset++] != 1)
        {
            throw new RejectedException("Wrong version byte.");
        }

        // int32 here, varint in the character format.
        int count = ReadInt32(data, ref offset);
        if (count < 0 || count > 64)
        {
            throw new RejectedException($"{count} weapons.");
        }

        var weapons = new List<DecodedWeapon>();
        for (int i = 0; i < count; i++)
        {
            weapons.Add(new DecodedWeapon(
                ReadString(data, ref offset),
                ReadVector(data, ref offset),
                ReadVector(data, ref offset),
                ReadVector(data, ref offset),
                data[offset++] != 0));
        }

        int payloadLength = ReadInt32(data, ref offset);
        if (payloadLength < 0 || payloadLength > data.Length - offset)
        {
            throw new RejectedException("Payload length does not fit the file.");
        }

        var payload = new byte[payloadLength];
        Array.Copy(data, offset, payload, 0, payloadLength);
        offset += payloadLength;

        if (offset != data.Length)
        {
            throw new RejectedException("Bytes left over after the payload.");
        }

        return (weapons, payloadLength == 0 ? [] : ReadCharacterPayload(payload));
    }

    private static void Expect(byte[] data, ref int offset, string magic)
    {
        string found = Encoding.ASCII.GetString(data, offset, magic.Length);
        if (found != magic)
        {
            throw new RejectedException($"Expected '{magic}', found '{found}'.");
        }

        offset += magic.Length;
    }

    private static uint ReadVarint(byte[] data, ref int offset)
    {
        uint value = 0;
        int shift = 0;

        while (true)
        {
            if (offset >= data.Length || shift > 28)
            {
                throw new RejectedException("A varint that never ended, or ran past five bytes.");
            }

            byte b = data[offset++];
            value |= (uint)(b & 0x7F) << shift;

            if ((b & 0x80) == 0)
            {
                return value;
            }

            shift += 7;
        }
    }

    private static string ReadString(byte[] data, ref int offset)
    {
        uint length = ReadVarint(data, ref offset);
        string value = Encoding.UTF8.GetString(data, offset, (int)length);
        offset += (int)length;
        return value;
    }

    private static Color32 ReadColor(byte[] data, ref int offset, List<Color32> palette)
    {
        if (palette.Count == 0)
        {
            var raw = new Color32(data[offset], data[offset + 1], data[offset + 2]);
            offset += 3;
            return raw;
        }

        int index = palette.Count <= 256
            ? data[offset++]
            : data[offset++] | (data[offset++] << 8);

        if (index >= palette.Count)
        {
            throw new RejectedException($"Palette index {index} of {palette.Count}.");
        }

        return palette[index];
    }

    private static float ReadSingle(byte[] data, ref int offset)
    {
        float value = BitConverter.ToSingle(data, offset);
        offset += 4;
        return value;
    }

    private static int ReadInt32(byte[] data, ref int offset)
    {
        int value = BitConverter.ToInt32(data, offset);
        offset += 4;
        return value;
    }

    private static short ReadInt16(byte[] data, ref int offset)
    {
        short value = BitConverter.ToInt16(data, offset);
        offset += 2;
        return value;
    }

    private static ushort ReadUInt16(byte[] data, ref int offset)
    {
        ushort value = BitConverter.ToUInt16(data, offset);
        offset += 2;
        return value;
    }

    private static (float, float, float) ReadVector(byte[] data, ref int offset) =>
        (ReadSingle(data, ref offset), ReadSingle(data, ref offset), ReadSingle(data, ref offset));
}
