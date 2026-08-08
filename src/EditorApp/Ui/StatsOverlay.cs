using System.Numerics;
using EditorApp.Rendering;
using ImGuiNET;

namespace EditorApp.Ui;

/// <summary>
/// Frame timing and mesh counters — the numbers that show face culling is working, and the ones to
/// look at first when a large level starts to feel heavy.
/// </summary>
public sealed class StatsOverlay
{
    private const int HistoryLength = 120;

    private readonly float[] _frameTimes = new float[HistoryLength];
    private int _cursor;

    /// <summary>Records the frame time even when the section is collapsed, so the graph stays continuous.</summary>
    public void Sample(float deltaSeconds)
    {
        _frameTimes[_cursor] = deltaSeconds * 1000f;
        _cursor = (_cursor + 1) % HistoryLength;
    }

    public void DrawContent(GlRenderer renderer, FlyCamera camera, EditorApp.Core.Editing.EditorSession session)
    {
        int solidVoxels = session.Scene.SolidCount;

        int chunkCount = 0;
        foreach (Core.Scene.VoxelObject o in session.Scene.Objects)
        {
            chunkCount += o.Grid.Chunks.Count;
        }

        float deltaSeconds = _frameTimes[(_cursor + HistoryLength - 1) % HistoryLength] / 1000f;
        float fps = deltaSeconds > 0f ? 1f / deltaSeconds : 0f;

        ImGui.Text($"{fps,6:0.0} fps   ({deltaSeconds * 1000f:0.00} ms)");
        ImGui.PlotLines("##frametimes", ref _frameTimes[0], HistoryLength, _cursor, "frame ms", 0f, 33f, new Vector2(-1f, 40f));
        ImGui.Spacing();

        ImGui.Text($"Objects       {session.Scene.Objects.Count:N0}");
        ImGui.Text($"Voxels        {solidVoxels:N0}");
        ImGui.Text($"Chunks        {chunkCount:N0}");
        ImGui.Text($"Drawn         {renderer.VisibleChunks:N0}   ({chunkCount - renderer.VisibleChunks:N0} culled)");
        ImGui.Text($"Vertices      {renderer.TotalVertices:N0}");
        ImGui.Text($"Triangles     {renderer.DrawnTriangles:N0}");
        ImGui.Separator();

        ImGui.Text($"Remeshed      {renderer.LastRemeshedChunks:N0} chunk(s)");
        ImGui.Text($"  mesh        {renderer.LastMeshMilliseconds:0.00} ms");
        ImGui.Text($"  upload      {renderer.LastUploadMilliseconds:0.00} ms");

        if (renderer.PendingChunks > 0)
        {
            // A backlog is normal right after a big paste; it should drain within a few frames.
            ImGui.TextColored(Theme.Highlight, $"  queued     {renderer.PendingChunks:N0}");
        }
        else
        {
            ImGui.TextDisabled("  queued     0");
        }

        ImGui.Separator();
        Vector3 position = camera.Position;
        ImGui.Text($"Camera        {position.X:0.0}, {position.Y:0.0}, {position.Z:0.0}");
    }
}
