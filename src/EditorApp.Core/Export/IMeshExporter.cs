using EditorApp.Core.Meshing;
using EditorApp.Core.Voxels;


namespace EditorApp.Core.Export;

/// <summary>Settings shared by every exporter.</summary>
public sealed record ExportOptions
{
    /// <summary>
    /// Name of the generated texture, written next to the mesh and referenced by relative path. An
    /// absolute path would open textureless on any other machine (EditorApp.md §6). Left empty it
    /// follows the layout, since the two textures are not the same kind of thing.
    /// </summary>
    public string TextureFileName { get; init; } = string.Empty;

    /// <summary>
    /// The unwrapped layout, or null for the palette blocks of §6.
    ///
    /// Set by whoever built the mesh, because unwrapping rewrites the mesh's UVs — the exporter is
    /// handed a mesh that is already laid out, and only needs to know which texture to draw for it.
    /// </summary>
    public UvAtlas? Atlas { get; init; }

    /// <summary>
    /// Off when the model is going somewhere that will supply its own texture. The UVs are written
    /// either way; this only decides whether a starting image is written with them.
    /// </summary>
    public bool WriteTexture { get; init; } = true;

    /// <summary>Writes the recommended import settings beside the mesh as a short text file.</summary>
    public bool WriteImportNotes { get; init; } = true;

    public string ResolveTextureFileName() => TextureFileName.Length > 0
        ? TextureFileName
        : Atlas is null ? PaletteTexture.DefaultFileName : AtlasTexture.DefaultFileName;

    public byte[] EncodeTexture(ExportMesh mesh, Palette palette) => Atlas is { } atlas
        ? AtlasTexture.EncodePng(atlas, mesh, palette)
        : PaletteTexture.EncodePng(palette);

    /// <summary>A texture laid out as the colour one is, each entry's texel what <paramref name="texel"/> says: a material channel.</summary>
    public byte[] EncodeChannel(ExportMesh mesh, Palette palette, Func<int, Color32> texel) => Atlas is { } atlas
        ? AtlasTexture.EncodePng(atlas, mesh, texel)
        : PaletteTexture.EncodePng(palette, texel);

    public string ImportNotes() => Atlas is { } atlas
        ? AtlasTexture.ImportNotes(atlas)
        : PaletteTexture.ImportNotes;
}

/// <summary>What an export produced.</summary>
public sealed record ExportResult(IReadOnlyList<string> FilesWritten, int VertexCount, int TriangleCount, int QuadCount);

/// <summary>
/// One output format. All of them consume the same greedy <see cref="ExportMesh"/>, so adding FBX
/// later is a new implementation rather than a change to anything that already works.
/// </summary>
public interface IMeshExporter
{
    /// <summary>Shown in the export dialog.</summary>
    string DisplayName { get; }

    /// <summary>Primary file extension, including the dot.</summary>
    string Extension { get; }

    ExportResult Export(ExportMesh mesh, Palette palette, string path, ExportOptions options);
}
