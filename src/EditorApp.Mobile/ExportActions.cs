using System.IO.Compression;
using Android.App;
using Android.Content;
using Android.Widget;
using EditorApp.Core.Export;
using EditorApp.Core.Export.Mimicraft;
using EditorApp.Core.Meshing;
using EditorApp.Core.Scene;
using EditorApp.Mobile.Rendering;

namespace EditorApp.Mobile;

/// <summary>What leaves the phone, and in what shape.</summary>
public enum ExportKind
{
    /// <summary>One .glb, texture and all. The only mesh format that is genuinely a single file.</summary>
    Glb,

    /// <summary>An .obj, its .mtl and its .png, zipped — because the picker hands out one document.</summary>
    ObjZip,

    /// <summary>A Mimicraft .character.</summary>
    Character,

    /// <summary>A Mimicraft .weapons.</summary>
    Weapon,
}

/// <summary>
/// Exporting from the phone: the mesh the desktop writes, and the two Mimicraft formats.
///
/// The meshing, unwrapping and encoding are all the same Core code the desktop calls — nothing about
/// what comes out differs. What differs is where it goes. Android hands out one document at a time
/// and never a directory, which decides the two formats offered: a .glb carries its texture inside
/// itself, and an .obj needs three files so it travels as a zip.
///
/// Everything is written into the cache directory first and then copied across. The exporters take a
/// path, and a path is the one thing the picker does not give.
/// </summary>
public sealed class ExportActions(Activity activity, EditorSurfaceView surface, string cacheDirectory)
{
    public const int Request = 3;

    private ExportKind _pending;

    /// <summary>How much texture a voxel gets. The desktop's default, and its reasons.</summary>
    private const int TexelsPerVoxel = UvUnwrap.DefaultTexelsPerVoxel;

    public void Start(ExportKind kind)
    {
        _pending = kind;

        var intent = new Intent(Intent.ActionCreateDocument);
        intent.AddCategory(Intent.CategoryOpenable);
        intent.SetType("*/*");
        intent.PutExtra(Intent.ExtraTitle, surface.ProjectName + ExtensionFor(kind));

        activity.StartActivityForResult(intent, Request);
    }

    private static string ExtensionFor(ExportKind kind) => kind switch
    {
        ExportKind.Glb => ".glb",
        ExportKind.ObjZip => ".zip",
        ExportKind.Character => MimicraftFiles.CharacterExtension,
        _ => MimicraftFiles.WeaponExtension,
    };

    /// <summary>Handles the picker coming back. Returns false when the result was not ours.</summary>
    public bool OnPicked(int requestCode, Result resultCode, Intent? data)
    {
        if (requestCode != Request)
        {
            return false;
        }

        if (resultCode != Result.Ok || data?.Data is not { } uri)
        {
            return true;
        }

        try
        {
            // Checked before a byte is written. The desktop refuses an export that would not load in
            // Unity, and a file that is silently wrong is worse on a phone, where there is no build
            // log to notice it in later.
            if (Problems(_pending) is { Count: > 0 } problems)
            {
                Say($"Not written — {problems[0].Subject}: {problems[0].Message}");
                return true;
            }

            byte[] bytes = Build(_pending);

            using Stream? stream = activity.ContentResolver?.OpenOutputStream(uri)
                ?? throw new IOException("The file could not be created.");

            stream.Write(bytes);
            Say($"Exported {bytes.Length / 1024.0:0.0} KB.");
        }
        catch (Exception error)
        {
            Say($"Could not export: {error.Message}");
        }

        return true;
    }

    private IReadOnlyList<MimicraftProblem>? Problems(ExportKind kind)
    {
        if (kind is not (ExportKind.Character or ExportKind.Weapon))
        {
            return null;
        }

        MimicraftTarget target = kind == ExportKind.Character
            ? MimicraftTarget.Character
            : MimicraftTarget.Weapon;

        IReadOnlyList<MimicraftProblem>? found = null;
        surface.UseScene(scene => found = MimicraftValidation.Check(scene, target));
        return found;
    }

    private byte[] Build(ExportKind kind)
    {
        // A scene has no name of its own; what a level is called is the project it was saved as.
        string name = Files.LevelStore.MakeFileSafe(surface.ProjectName);
        byte[]? result = null;

        surface.UseScene(scene =>
        {
            result = kind switch
            {
                ExportKind.Glb => BuildMesh(scene, name, new GltfExporter(binary: true), zip: false),
                ExportKind.ObjZip => BuildMesh(scene, name, new ObjExporter(), zip: true),
                ExportKind.Character => MimicraftFiles.EncodeCharacter(
                    name,
                    rigId: string.Empty,
                    MimicraftScene.BuildCharacterParts(scene),
                    scene.Palette,
                    new MimicraftOrientation()),
                _ => MimicraftFiles.EncodeWeapon(
                    name,
                    MimicraftScene.BuildWeaponPiece(scene, name),
                    scene.Palette,
                    new MimicraftOrientation()),
            };
        });

        return result ?? throw new InvalidOperationException("Nothing was produced.");
    }

    /// <summary>
    /// Runs the export the desktop runs — greedy mesh, unwrap, atlas — into a scratch directory, then
    /// picks the result back up as bytes.
    /// </summary>
    private byte[] BuildMesh(VoxelScene scene, string name, IMeshExporter exporter, bool zip)
    {
        string workspace = Path.Combine(cacheDirectory, "export");

        if (Directory.Exists(workspace))
        {
            Directory.Delete(workspace, recursive: true);
        }

        Directory.CreateDirectory(workspace);

        try
        {
            // mergeAcrossColors: colour rides in the texture, so a merged quad spanning two colours
            // is still one quad — which is the whole reason the unwrap exists.
            ExportMesh mesh = GreedyMesher.BuildScene(scene, uvSelector: null, mergeAcrossColors: true);
            UvAtlas atlas = UvUnwrap.Apply(mesh, scene.VoxelSize, TexelsPerVoxel);

            string path = Path.Combine(workspace, name + exporter.Extension);
            exporter.Export(mesh, scene.Palette, path, new ExportOptions { Atlas = atlas });

            if (!zip)
            {
                return File.ReadAllBytes(path);
            }

            string archive = Path.Combine(cacheDirectory, "export.zip");
            File.Delete(archive);
            ZipFile.CreateFromDirectory(workspace, archive, CompressionLevel.Optimal, includeBaseDirectory: false);

            try
            {
                return File.ReadAllBytes(archive);
            }
            finally
            {
                File.Delete(archive);
            }
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }

    private void Say(string message) => Toast.MakeText(activity, message, ToastLength.Long)?.Show();
}
