using System.Numerics;
using EditorApp.Core.Editing;
using EditorApp.Core.Raycast;
using EditorApp.Core.Scene;
using EditorApp.Core.Voxels;
using EditorApp.Rendering;

namespace EditorApp;

/// <summary>Which faces a stencil's box on the view falls on (Fullreleaseplan 4.5).</summary>
public static class StencilProjection
{
    /// <summary>
    /// The object's open faces whose middles are inside a box on the view and seen there — the first
    /// face along the line of sight — each with where in the box it is, 0 to 1 across and down.
    /// </summary>
    public static IEnumerable<(Int3 Cell, Face Face, float U, float V)> FacesSeenIn(
        VoxelObject target,
        FlyCamera camera,
        Vector2 viewport,
        Vector2 min,
        Vector2 max)
    {
        Vector2 size = max - min;
        foreach (Int3 cell in ClipboardOperations.Everything(target.Grid).ToList())
        {
            for (int f = 0; f < FaceInfo.Count; f++)
            {
                var face = (Face)f;
                if (target.Grid.IsSolid(cell + FaceInfo.Offset(face)))
                {
                    continue;
                }

                Vector3 middle = target.Transform.TransformPoint(cell.ToVector3() + new Vector3(0.5f) + (FaceInfo.Normal(face) * 0.5f));
                if (!camera.TryProjectToScreen(middle, viewport, out Vector2 at)
                    || at.X < min.X || at.Y < min.Y || at.X > max.X || at.Y > max.Y)
                {
                    continue;
                }

                Ray local = target.Transform.InverseTransformRay(camera.ScreenPointToRay(at, viewport));
                if (VoxelRaycaster.TryCast(target.Grid, local, out RaycastHit hit) && hit.Voxel == cell && hit.Face == face)
                {
                    yield return (cell, face, (at.X - min.X) / size.X, (at.Y - min.Y) / size.Y);
                }
            }
        }
    }
}
