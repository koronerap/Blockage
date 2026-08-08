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

    public void Draw(GlRenderer renderer, FlyCamera camera, int solidVoxels, int chunkCount, float deltaSeconds)
    {
        _frameTimes[_cursor] = deltaSeconds * 1000f;
        _cursor = (_cursor + 1) % HistoryLength;

        ImGui.SetNextWindowPos(new Vector2(12, 32), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowBgAlpha(0.85f);

        if (!ImGui.Begin("Statistics", ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoFocusOnAppearing))
        {
            ImGui.End();
            return;
        }

        float fps = deltaSeconds > 0f ? 1f / deltaSeconds : 0f;
        ImGui.Text($"{fps,6:0.0} fps   ({deltaSeconds * 1000f:0.00} ms)");
        ImGui.PlotLines("##frametimes", ref _frameTimes[0], HistoryLength, _cursor, "frame ms", 0f, 33f, new Vector2(220f, 40f));
        ImGui.Separator();

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

        ImGui.End();
    }
}
