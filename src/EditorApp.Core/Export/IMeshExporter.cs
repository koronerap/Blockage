using EditorApp.Core.Meshing;
using EditorApp.Core.Voxels;

namespace EditorApp.Core.Export;

/// <summary>Settings shared by every exporter.</summary>
public sealed record ExportOptions
{
    /// <summary>
    /// Name of the generated palette texture, written next to the mesh and referenced by relative
    /// path. An absolute path would open textureless on any other machine (EditorApp.md §6).
    /// </summary>
    public string TextureFileName { get; init; } = PaletteTexture.DefaultFileName;

    /// <summary>Writes the recommended import settings beside the mesh as a short text file.</summary>
    public bool WriteImportNotes { get; init; } = true;
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
