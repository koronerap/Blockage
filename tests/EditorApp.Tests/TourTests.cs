using EditorApp.Core.Editing;
using EditorApp.Ui;

namespace EditorApp.Tests;

public class TourTests
{
    private static readonly TourState Fresh = new(
        Yaw: 0.8f, Pitch: -0.5f, Voxels: 512, Objects: 1, FocusId: 1, SelectedCount: 1,
        Tool: EditorTool.Select, Done: 0, Undone: 0, Saved: false);

    /// <summary>Each step waits for its own thing, and passes on it alone, from a fresh cube to a saved level.</summary>
    [Fact]
    public void EveryStepPassesWhenItsThingIsDone()
    {
        Tour.Start(Fresh, cubeId: 1);
        try
        {
            TourState now = Fresh;
            void Expect(int step, TourState state)
            {
                Tour.Update(state);
                Assert.Equal(step, Tour.CurrentStep);
                now = state;
            }

            // Nothing done yet: the first step stays, however long.
            Expect(0, now with { Yaw = now.Yaw + 0.1f });
            Expect(1, now with { Yaw = now.Yaw + 0.5f });

            // Paint before extruding does not pass Extrude; more voxels do.
            Expect(1, now with { Tool = EditorTool.Paint, Done = 1 });
            Expect(2, now with { Voxels = 576, Done = 2, Tool = EditorTool.Extrude });

            // Undoing is not painting, nor is another tool's step.
            Expect(2, now with { Done = 3, Tool = EditorTool.Extrude });
            Expect(3, now with { Tool = EditorTool.Paint, Done = 4 });

            Expect(4, now with { Objects = 2, FocusId = 2, Voxels = 700, Done = 5 });

            // Back on the cube, and it alone.
            Expect(4, now with { FocusId = 1, SelectedCount = 2 });
            Expect(5, now with { FocusId = 1, SelectedCount = 1 });

            Expect(6, now with { Undone = 1, Done = 4 });
            Expect(7, now with { Saved = true });
            Assert.Equal(Tour.StepCount, Tour.CurrentStep);
            Assert.True(Tour.IsActive);
        }
        finally
        {
            Tour.End();
        }

        Assert.False(Tour.IsActive);
    }
}
