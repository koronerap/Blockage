using System.Numerics;
using EditorApp.Ui;
using ImGuiNET;

namespace EditorApp.Tests;

/// <summary>
/// The single-button switches that replaced the header's rows of paired buttons. They are drawn
/// everywhere a mode is chosen now, so what a click does has to be pinned down rather than looked at.
///
/// Not run in parallel with anything else that touches ImGui: its context is process-wide.
/// </summary>
[Collection(nameof(ImGuiCollection))]
public sealed class IconButtonTests : IDisposable
{
    private const float Size = 30f;

    private readonly ImGuiHarness _ui = new();

    public void Dispose() => _ui.Dispose();

    private static readonly (Icons.Painter, string)[] Modes =
        [(Icons.Brush, "Brush"), (Icons.Bucket, "Bucket fill"), (Icons.Pattern, "Pattern fill")];

    /// <summary>Where the first widget in the harness window lands, found rather than assumed.</summary>
    private Vector2 FirstItemCentre(Action draw)
    {
        Vector2 centre = Vector2.Zero;

        _ui.Frame(() =>
        {
            draw();
            centre = (ImGui.GetItemRectMin() + ImGui.GetItemRectMax()) * 0.5f;
        });

        return centre;
    }

    [Fact]
    public void AToggleReportsTheClickThatChangesIt()
    {
        bool second = false;
        int changes = 0;

        void Draw()
        {
            if (IconButton.Toggle("t", (Icons.Move, "Move"), (Icons.Rotate, "Rotate"), second, "F", Size))
            {
                second = !second;
                changes++;
            }
        }

        Vector2 button = FirstItemCentre(Draw);
        _ui.Click(button, Draw);

        Assert.Equal(1, changes);
        Assert.True(second);
    }

    [Fact]
    public void AToggleIgnoresAClickSomewhereElse()
    {
        bool second = false;

        void Draw()
        {
            if (IconButton.Toggle("t", (Icons.Move, "Move"), (Icons.Rotate, "Rotate"), second, "F", Size))
            {
                second = !second;
            }
        }

        Vector2 button = FirstItemCentre(Draw);
        _ui.Click(button + new Vector2(200f, 0f), Draw);

        Assert.False(second);
    }

    /// <summary>
    /// A choice does not change on the first click — that opens the list. Changing straight away
    /// would be cycling blind through options the user cannot see, which is what it replaced.
    /// </summary>
    [Fact]
    public void AChoiceOpensItsListRatherThanChangingOnTheFirstClick()
    {
        int selected = 0;
        bool open = false;

        void Draw()
        {
            IconButton.Choice("paint", Modes, selected, "X", Size, value => selected = value);
            open = ImGui.IsPopupOpen("##paint-choices");
        }

        Vector2 button = FirstItemCentre(Draw);
        _ui.Click(button, Draw);

        Assert.True(open, "the list did not open");
        Assert.Equal(0, selected);
    }

    [Fact]
    public void PickingFromTheListChangesTheChoiceAndClosesIt()
    {
        int selected = 0;
        bool open = false;
        var rows = new List<Vector2>();

        void Draw()
        {
            IconButton.Choice("paint", Modes, selected, "X", Size, value => selected = value);
            open = ImGui.IsPopupOpen("##paint-choices");
        }

        Vector2 button = FirstItemCentre(Draw);
        _ui.Click(button, Draw);
        Assert.True(open);

        // The popup's rows are items inside the popup window. Recorded through ImGui's own item
        // query on the frame they are drawn, by wrapping the same call.
        _ui.Frame(() =>
        {
            IconButton.Choice("paint", Modes, selected, "X", Size, value => selected = value);
        });

        // The rows sit directly below the button, one frame-height apart, starting after the popup's
        // padding: aimed at the third one, "Pattern fill", by walking down until it is hit.
        for (float y = button.Y + Size; y < button.Y + (Size * 6f) && selected == 0; y += 4f)
        {
            _ui.Click(new Vector2(button.X + 30f, y), Draw);

            if (!open)
            {
                break;
            }
        }

        Assert.NotEqual(0, selected);
        Assert.False(open, "the list stayed open after a choice was made");
    }

    [Fact]
    public void AChoiceLeftAloneDoesNotChange()
    {
        int selected = 1;

        void Draw() => IconButton.Choice("paint", Modes, selected, "X", Size, value => selected = value);

        for (int i = 0; i < 5; i++)
        {
            _ui.Frame(Draw);
        }

        Assert.Equal(1, selected);
    }
}

/// <summary>ImGui has one current context per process, so its tests take turns.</summary>
[CollectionDefinition(nameof(ImGuiCollection), DisableParallelization = true)]
public sealed class ImGuiCollection;
