using EditorApp.Core.Scene;
using EditorApp.Rendering;

namespace EditorApp.Tests;

/// <summary>
/// The grid measures world units, so its spacing is the one thing in the viewport that has to move
/// when a voxel stops being one unit.
/// </summary>
public class GroundGridTests
{
    [Fact]
    public void AtOneUnitPerVoxelACellIsOneVoxel()
    {
        // Where the grid started, and what it has to keep looking like for every level built so far.
        Assert.Equal(1f, GroundGrid.Spacing(1f), 5);
        Assert.Equal(1f, GroundGrid.WorldUnitsPerCell(1f), 5);
    }

    [Theory]
    [InlineData(0.5f, 2f)]
    [InlineData(0.25f, 4f)]
    [InlineData(0.2f, 5f)]
    public void ASmallerVoxelPutsMoreOfThemInACell(float voxelSize, float expected)
    {
        Assert.Equal(expected, GroundGrid.Spacing(voxelSize), 4);
        Assert.Equal(1f, GroundGrid.WorldUnitsPerCell(voxelSize), 4);
    }

    [Fact]
    public void TheModelToGridRatioActuallyChanges()
    {
        // The complaint this exists to answer: an 8-voxel cube covered 8 cells at any voxel size.
        const float cube = 8f;

        Assert.Equal(8f, cube / GroundGrid.Spacing(1f), 4);
        Assert.Equal(2f, cube / GroundGrid.Spacing(0.25f), 4);
        Assert.Equal(16f, cube / GroundGrid.Spacing(2f), 4);
    }

    [Theory]
    [InlineData(VoxelScene.MinVoxelSize)]
    [InlineData(0.03f)]
    [InlineData(1f)]
    [InlineData(7f)]
    [InlineData(120f)]
    [InlineData(VoxelScene.MaxVoxelSize)]
    public void ACellStaysLegibleAtEveryAllowedVoxelSize(float voxelSize)
    {
        float spacing = GroundGrid.Spacing(voxelSize);

        // Narrow enough to smear together, or wide enough that the grid gives the eye nothing.
        Assert.InRange(spacing, 0.5f, 5f);
    }

    [Fact]
    public void TheGridKeepsTheSameNumberOfCellsAtEverySize()
    {
        // What stops the answer to "make the grid respond" from being "make the grid empty": the
        // cell count is fixed, so the density on screen holds and only the model's share of it moves.
        Assert.True(GroundGrid.HalfExtentCells <= GroundGrid.MaxLinesPerAxis);

        foreach (float voxelSize in new[] { 0.001f, 0.25f, 1f, 4f, 1000f })
        {
            float extent = GroundGrid.HalfExtentCells * GroundGrid.Spacing(voxelSize);

            // The world it covers moves with the cell size — that is the whole mechanism.
            Assert.InRange(extent, GroundGrid.HalfExtentCells * 0.5f, GroundGrid.HalfExtentCells * 5f);
        }
    }

    [Fact]
    public void ACellIsAlwaysAWholePowerOfTenOfWorldUnits()
    {
        // A cell worth 2.5 units would be unreadable as a measurement, however tidy it looks.
        foreach (float voxelSize in new[] { 0.001f, 0.04f, 0.25f, 1f, 3f, 60f, 1000f })
        {
            double units = GroundGrid.WorldUnitsPerCell(voxelSize);
            double log = Math.Log10(units);

            Assert.Equal(Math.Round(log), log, 4);
        }
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(-1f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void ASizeThatCouldNotBeSetAnywayFallsBackToOne(float bad)
    {
        // VoxelScene refuses these, but a spacing routine that hangs or divides by zero on one is a
        // worse failure than the wrong number.
        Assert.Equal(1f, GroundGrid.Spacing(bad), 5);
    }
}
