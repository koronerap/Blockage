using EditorApp.Core.Export.Mimicraft;
using EditorApp.Core.Voxels;

namespace EditorApp.Core.Tests.Mimicraft;

/// <summary>
/// The round-trip tests are only worth anything if the reader would notice a writer that had gone
/// wrong. These hand-build the exact mistakes Mimicraft's decoder rejects and check that this one
/// rejects them too — otherwise a passing round trip proves nothing but that both halves agree.
/// </summary>
public class MimicraftReaderTeethTests
{
    /// <summary>A one-piece body with a 4x1x2 box and whatever runs and faces are handed in.</summary>
    private static byte[] Body(Action<List<byte>> runs, int runCount, Action<List<byte>>? faces = null, int faceCount = 0)
    {
        var body = new List<byte> { MimicraftBody.FormatRaw };

        MimicraftBinary.WriteSingle(body, 1f);
        MimicraftBinary.WriteVarint(body, 1);          // one piece
        MimicraftBinary.WriteVarint(body, 1);          // one palette entry
        body.AddRange([200, 60, 60]);

        for (int i = 0; i < 7; i++)
        {
            MimicraftBinary.WriteSingle(body, i == 6 ? 1f : 0f);
        }

        MimicraftBinary.WriteInt16(body, 0);
        MimicraftBinary.WriteInt16(body, 0);
        MimicraftBinary.WriteInt16(body, 0);
        MimicraftBinary.WriteUInt16(body, 4);
        MimicraftBinary.WriteUInt16(body, 1);
        MimicraftBinary.WriteUInt16(body, 2);

        MimicraftBinary.WriteVarint(body, (uint)runCount);
        runs(body);

        MimicraftBinary.WriteVarint(body, (uint)faceCount);
        faces?.Invoke(body);

        return [.. body];
    }

    private static void Run(List<byte> body, uint gap, uint length)
    {
        MimicraftBinary.WriteVarint(body, gap);
        MimicraftBinary.WriteVarint(body, length);
        body.Add(0);
    }

    [Fact]
    public void AWellFormedBodyIsAccepted()
    {
        // The control. Without it, every test below could be passing because the reader rejects
        // everything.
        DecodedBody body = MimicraftReader.ReadBody(Body(b => Run(b, 0, 4), 1));

        Assert.Equal(4, body.Pieces[0].Voxels.Count);
    }

    [Fact]
    public void ARunThatCrossesARowIsRejected()
    {
        // The mistake the format warns about twice: x wraps at 4, the index keeps counting, and a
        // run of 6 from zero looks continuous while covering parts of two rows.
        MimicraftReader.RejectedException failure = Assert.Throws<MimicraftReader.RejectedException>(
            () => MimicraftReader.ReadBody(Body(b => Run(b, 0, 6), 1)));

        Assert.Contains("crosses a row", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AZeroLengthRunIsRejected()
    {
        Assert.Throws<MimicraftReader.RejectedException>(
            () => MimicraftReader.ReadBody(Body(b => Run(b, 0, 0), 1)));
    }

    [Fact]
    public void ARunPastTheEndOfTheBoxIsRejected()
    {
        Assert.Throws<MimicraftReader.RejectedException>(
            () => MimicraftReader.ReadBody(Body(b =>
            {
                Run(b, 0, 4);
                Run(b, 8, 4);
            }, 2)));
    }

    [Fact]
    public void RunGapsMeasuredFromTheWrongPlaceAreCaught()
    {
        // If a writer measured the gap from the previous run's START instead of its END, the second
        // run would land on top of the first. The reader sees the overlap.
        Assert.Throws<MimicraftReader.RejectedException>(
            () => MimicraftReader.ReadBody(Body(b =>
            {
                Run(b, 0, 4);
                Run(b, 0, 4);   // from the end this is the next row; from the start it is the same one
                Run(b, 0, 4);
            }, 3)));
    }

    [Fact]
    public void TheSameFaceTwiceIsRejected()
    {
        MimicraftReader.RejectedException failure = Assert.Throws<MimicraftReader.RejectedException>(
            () => MimicraftReader.ReadBody(Body(
                b => Run(b, 0, 4),
                1,
                b =>
                {
                    MimicraftBinary.WriteVarint(b, 0);
                    b.Add(2);
                    b.Add(0);

                    MimicraftBinary.WriteVarint(b, 0);
                    b.Add(2);
                    b.Add(0);
                },
                2)));

        Assert.Contains("out of order", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FacesGoingBackwardsAreRejected()
    {
        Assert.Throws<MimicraftReader.RejectedException>(
            () => MimicraftReader.ReadBody(Body(
                b => Run(b, 0, 4),
                1,
                b =>
                {
                    MimicraftBinary.WriteVarint(b, 0);
                    b.Add(4);
                    b.Add(0);

                    MimicraftBinary.WriteVarint(b, 0);
                    b.Add(1);   // lower than the last face at the same index
                    b.Add(0);
                },
                2)));
    }

    [Fact]
    public void AFaceOnAVoxelThatIsNotThereIsRejected()
    {
        Assert.Throws<MimicraftReader.RejectedException>(
            () => MimicraftReader.ReadBody(Body(
                b => Run(b, 0, 2),
                1,
                b =>
                {
                    MimicraftBinary.WriteVarint(b, 6);   // in the second row, where nothing was written
                    b.Add(2);
                    b.Add(0);
                },
                1)));
    }

    [Fact]
    public void AFaceNumberOutOfRangeIsRejected()
    {
        Assert.Throws<MimicraftReader.RejectedException>(
            () => MimicraftReader.ReadBody(Body(
                b => Run(b, 0, 4),
                1,
                b =>
                {
                    MimicraftBinary.WriteVarint(b, 0);
                    b.Add(6);
                    b.Add(0);
                },
                1)));
    }

    [Fact]
    public void AShortRunCountLeavesBytesOver()
    {
        // Claiming fewer runs than were written: the piece parses, and the leftovers give it away.
        Assert.Throws<MimicraftReader.RejectedException>(
            () => MimicraftReader.ReadBody(Body(
                b =>
                {
                    Run(b, 0, 4);
                    Run(b, 0, 4);
                },
                1)));
    }
}
