using EditorApp.Core.Voxels;

namespace EditorApp.Core.Export.Mimicraft;

/// <summary>Which of the editor's axes points up in the model as it was built.</summary>
public enum MimicraftUpAxis
{
    Y,
    Z,
    X,
}

/// <summary>
/// How a model's own axes are turned into Unity's on the way out.
///
/// One part of this is fixed and one is a choice. The fixed part is handedness: this editor is
/// right-handed, its camera looking down -Z as the graphics API under it does, and Unity is
/// left-handed. Copying coordinates across unchanged hands over the model's mirror image, and a
/// reflection of a familiar shape mostly reads as the shape put together wrong.
///
/// The choice is everything else — which way the model was actually built. A model lying on its side
/// or facing along the wrong horizontal axis is not a bug in the conversion, and there is nothing in
/// the file that could tell them apart, so it has to be said rather than guessed.
///
/// Cells and face directions both go through the same signed permutation, which is the point of
/// keeping it as a matrix rather than as a handful of cases: they cannot drift apart. Every
/// combination offered here is a rotation, so nothing can be set to a state that mirrors the model
/// by accident.
/// </summary>
public sealed record MimicraftOrientation(MimicraftUpAxis Up = MimicraftUpAxis.Y, int TurnDegrees = 0)
{
    /// <summary>What the editor has always meant: Y up, nothing turned.</summary>
    public static readonly MimicraftOrientation Default = new();

    public static readonly int[] Turns = [0, 90, 180, 270];

    // Worked out once and kept. Every cell of every piece asks for the mapping, and composing three
    // matrices per voxel turned a two-second test run into a twenty-three-second one.
    private (int Axis, int Sign)[]? _mapping;
    private byte[]? _faces;

    /// <summary>
    /// The full mapping as <c>unity = M * editor</c>, built by composing three steps rather than by
    /// enumerating the twelve results.
    /// </summary>
    private int[,] Matrix
    {
        get
        {
            // Whichever editor axis the modeller treated as up becomes Y first.
            int[,] up = Up switch
            {
                // +Z is up: turn it onto +Y, which is a quarter turn about X.
                MimicraftUpAxis.Z => new[,] { { 1, 0, 0 }, { 0, 0, 1 }, { 0, -1, 0 } },

                // +X is up: a quarter turn about Z.
                MimicraftUpAxis.X => new[,] { { 0, 1, 0 }, { -1, 0, 0 }, { 0, 0, 1 } },

                _ => Identity,
            };

            // Then handedness. This is the step that is not a choice.
            int[,] handed = { { 1, 0, 0 }, { 0, 1, 0 }, { 0, 0, -1 } };

            // And finally the turn the modeller asks for, about the axis that is now up.
            int degrees = (((TurnDegrees % 360) + 360) % 360);
            (int sin, int cos) = degrees switch
            {
                90 => (1, 0),
                180 => (0, -1),
                270 => (-1, 0),
                _ => (0, 1),
            };

            int[,] turn = { { cos, 0, sin }, { 0, 1, 0 }, { -sin, 0, cos } };

            return Multiply(turn, Multiply(handed, up));
        }
    }

    private static readonly int[,] Identity = { { 1, 0, 0 }, { 0, 1, 0 }, { 0, 0, 1 } };

    private static int[,] Multiply(int[,] a, int[,] b)
    {
        var result = new int[3, 3];
        for (int row = 0; row < 3; row++)
        {
            for (int column = 0; column < 3; column++)
            {
                int sum = 0;
                for (int k = 0; k < 3; k++)
                {
                    sum += a[row, k] * b[k, column];
                }

                result[row, column] = sum;
            }
        }

        return result;
    }

    /// <summary>
    /// For each output axis, which source axis feeds it and whether it counts forwards or backwards.
    ///
    /// This is the inverse of the matrix, which for a signed permutation is just its transpose — and
    /// it is the direction actually needed, because the box is filled in output order and each cell
    /// has to ask where it came from.
    /// </summary>
    public (int Axis, int Sign)[] SourceOfEachAxis()
    {
        if (_mapping is not null)
        {
            return _mapping;
        }

        int[,] m = Matrix;
        var mapping = new (int, int)[3];

        for (int output = 0; output < 3; output++)
        {
            for (int source = 0; source < 3; source++)
            {
                if (m[output, source] != 0)
                {
                    mapping[output] = (source, m[output, source]);
                    break;
                }
            }
        }

        return _mapping = mapping;
    }

    /// <summary>The size of the written box, which is the source box with its axes shuffled.</summary>
    public Int3 Size(Int3 source)
    {
        (int Axis, int Sign)[] mapping = SourceOfEachAxis();
        int[] sizes = [source.X, source.Y, source.Z];

        return new Int3(
            sizes[mapping[0].Axis],
            sizes[mapping[1].Axis],
            sizes[mapping[2].Axis]);
    }

    /// <summary>Where an output cell reads from, in the source box's own coordinates.</summary>
    public Int3 Source(Int3 output, Int3 sourceSize)
    {
        (int Axis, int Sign)[] mapping = SourceOfEachAxis();
        int[] outputs = [output.X, output.Y, output.Z];
        int[] sizes = [sourceSize.X, sourceSize.Y, sourceSize.Z];
        var source = new int[3];

        for (int i = 0; i < 3; i++)
        {
            (int axis, int sign) = mapping[i];

            // A negative sign means this axis counts the other way, so the far end of the source
            // feeds the near end of the output.
            source[axis] = sign > 0 ? outputs[i] : sizes[axis] - 1 - outputs[i];
        }

        return new Int3(source[0], source[1], source[2]);
    }

    /// <summary>
    /// The Mimicraft face number a given editor face ends up as.
    ///
    /// Taken by turning the face's own normal through the same matrix and then asking which of
    /// Mimicraft's six directions that is — rather than by keeping a second table that would have to
    /// be re-derived by hand for every orientation, and would be wrong in a way flat-coloured models
    /// hide completely.
    /// </summary>
    public byte FaceNumber(Face face) => (_faces ??= BuildFaceTable())[(int)face];

    private byte[] BuildFaceTable()
    {
        int[,] m = Matrix;
        var table = new byte[FaceInfo.Count];

        for (int f = 0; f < FaceInfo.Count; f++)
        {
            System.Numerics.Vector3 n = FaceInfo.Normal((Face)f);
            int[] source = [(int)n.X, (int)n.Y, (int)n.Z];
            var turned = new int[3];

            for (int row = 0; row < 3; row++)
            {
                turned[row] = (m[row, 0] * source[0]) + (m[row, 1] * source[1]) + (m[row, 2] * source[2]);
            }

            table[f] = (turned[0], turned[1], turned[2]) switch
            {
                (0, 0, -1) => 0,
                (0, 0, 1) => 1,
                (0, 1, 0) => 2,
                (0, -1, 0) => 3,
                (-1, 0, 0) => 4,
                (1, 0, 0) => 5,
                _ => throw new InvalidOperationException(
                    $"{(Face)f} turned into ({turned[0]}, {turned[1]}, {turned[2]}), which is not a face direction."),
            };
        }

        return table;
    }

    public override string ToString() =>
        Up == MimicraftUpAxis.Y && TurnDegrees == 0 ? "Y up" : $"{Up} up, turned {TurnDegrees}";
}
