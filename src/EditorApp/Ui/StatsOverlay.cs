using System.Numerics;
using EditorApp.Rendering;
using ImGuiNET;

namespace EditorApp.Ui;

/// <summary>Frame timing and mesh counters — the numbers that tell you face culling is working.</summary>
public static class StatsOverlay
{
    public static void Draw(GlRenderer renderer, FlyCamera camera, int solidVoxels, int chunkCount, float deltaSeconds)
    {
        ImGui.SetNextWindowPos(new Vector2(12, 12), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowBgAlpha(0.85f);

        if (!ImGui.Begin("Statistics", ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoFocusOnAppearing))
        {
            ImGui.End();
            return;
        }

        float fps = deltaSeconds > 0f ? 1f / deltaSeconds : 0f;
        ImGui.Text($"{fps,6:0.0} fps   ({deltaSeconds * 1000f:0.00} ms)");
        ImGui.Separator();

        ImGui.Text($"Voxels        {solidVoxels:N0}");
        ImGui.Text($"Chunks        {chunkCount:N0}");
        ImGui.Text($"Visible       {renderer.VisibleChunks:N0}");
        ImGui.Text($"Vertices      {renderer.TotalVertices:N0}");
        ImGui.Text($"Triangles     {renderer.DrawnTriangles:N0}");
        ImGui.Text($"Remeshed      {renderer.LastRemeshedChunks:N0}");
        ImGui.Separator();

        Vector3 position = camera.Position;
        ImGui.Text($"Camera        {position.X:0.0}, {position.Y:0.0}, {position.Z:0.0}");

        ImGui.End();
    }
}
