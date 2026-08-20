using EditorApp.Core.Voxels;

namespace EditorApp.Core.Export.Mimicraft;

/// <summary>
/// The two file formats Mimicraft reads (EditorCodec.md §4 and §5). Both are thin shells around the
/// same CharacterCodec payload, which is itself a shell around <see cref="MimicraftBody"/> — one
/// encoder, wrapped twice, exactly as on the Unity side.
/// </summary>
public static class MimicraftFiles
{
    public const string CharacterExtension = ".character";
    public const string WeaponExtension = ".weapons";

    /// <summary>Most slots either format will accept.</summary>
    public const int MaxParts = 64;

    /// <summary>Longest a part or weapon id may be, in UTF-8 bytes.</summary>
    public const int MaxIdBytes = 64;

    private const byte Version = 1;

    /// <summary>
    /// The CharacterCodec payload: which grid fills which slot, then the body.
    ///
    /// The id list and the pieces must be the same length and in the same order — the reader checks
    /// and refuses the file otherwise. They are built from one list here so they cannot drift.
    /// </summary>
    public static byte[] EncodeCharacterPayload(
        IReadOnlyList<MimicraftPiece> pieces,
        Palette palette,
        MimicraftOrientation? orientation = null)
    {
        if (pieces.Count == 0)
        {
            // An empty payload is a real answer, and both shells accept a length of zero.
            return [];
        }

        var output = new List<byte>();
        MimicraftBinary.WriteAscii(output, "MCC");
        output.Add(Version);

        MimicraftBinary.WriteVarint(output, (uint)pieces.Count);
        foreach (MimicraftPiece piece in pieces)
        {
            byte[] id = System.Text.Encoding.UTF8.GetBytes(piece.Id);
            MimicraftBinary.WriteVarint(output, (uint)id.Length);
            output.AddRange(id);
        }

        byte[] body = MimicraftBody.Encode(pieces, palette, orientation);
        MimicraftBinary.WriteVarint(output, (uint)body.Length);
        output.AddRange(body);

        return [.. output];
    }

    /// <summary>A <c>.character</c> file: the payload plus the two things a file needs and a message does not.</summary>
    public static byte[] EncodeCharacter(
        string characterName,
        string rigId,
        IReadOnlyList<MimicraftPiece> parts,
        Palette palette,
        MimicraftOrientation? orientation = null)
    {
        byte[] payload = EncodeCharacterPayload(parts, palette, orientation);

        var output = new List<byte>();
        MimicraftBinary.WriteAscii(output, "MCF");
        output.Add(Version);
        MimicraftBinary.WriteString(output, characterName ?? string.Empty);
        MimicraftBinary.WriteString(output, rigId ?? string.Empty);

        // int32, and the payload's own length prefix is a varint. Two different encodings a dozen
        // bytes apart, which is worth saying out loud.
        MimicraftBinary.WriteInt32(output, payload.Length);
        output.AddRange(payload);

        return [.. output];
    }

    /// <summary>
    /// A <c>.weapons</c> file holding one weapon.
    ///
    /// The header and the voxel section are matched by id rather than by position, so the weapon's
    /// id has to be the piece's id as well. The three grip points are in the weapon's own space, in
    /// metres; with <paramref name="hasPoints"/> false they are ignored and the prefab's own grips
    /// are used, which is why the flag exists at all — zero is a real position.
    /// </summary>
    public static byte[] EncodeWeapon(
        string weaponId,
        MimicraftPiece? piece,
        Palette palette,
        MimicraftOrientation? orientation = null,
        bool hasPoints = false,
        (float X, float Y, float Z) leftGrip = default,
        (float X, float Y, float Z) rightGrip = default,
        (float X, float Y, float Z) muzzle = default)
    {
        byte[] payload = piece is null
            ? []
            : EncodeCharacterPayload([piece with { Id = weaponId }], palette, orientation);

        var output = new List<byte>();
        MimicraftBinary.WriteAscii(output, "MWS");
        output.Add(Version);

        // A plain int32, not a varint — the one place the two formats part company, and the Unity
        // source's own doc comment gets it wrong.
        MimicraftBinary.WriteInt32(output, 1);

        MimicraftBinary.WriteString(output, weaponId);
        WriteVector(output, leftGrip);
        WriteVector(output, rightGrip);
        WriteVector(output, muzzle);
        output.Add(hasPoints ? (byte)1 : (byte)0);

        MimicraftBinary.WriteInt32(output, payload.Length);
        output.AddRange(payload);

        return [.. output];
    }

    private static void WriteVector(List<byte> output, (float X, float Y, float Z) value)
    {
        MimicraftBinary.WriteSingle(output, value.X);
        MimicraftBinary.WriteSingle(output, value.Y);
        MimicraftBinary.WriteSingle(output, value.Z);
    }
}
