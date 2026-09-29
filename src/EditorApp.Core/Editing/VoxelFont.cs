using EditorApp.Core.Voxels;

namespace EditorApp.Core.Editing;

/// <summary>
/// A 5 × 7 pixel font for voxel lettering (Fullreleaseplan 3.9): capitals, digits and the common
/// marks, and Turkish's own letters with their marks above or below the line. Small letters are set
/// as capitals — at seven voxels high a lower case would be a smudge. Built in, so a sign reads the
/// same on every machine and in every file.
/// </summary>
public static class VoxelFont
{
    public const int Width = 5;

    /// <summary>The body of a letter; a mark above or a cedilla below goes one row outside it.</summary>
    public const int Height = 7;

    private static readonly Dictionary<char, string[]> Glyphs = new()
    {
        [' '] = [".....", ".....", ".....", ".....", ".....", ".....", "....."],
        ['0'] = [".###.", "#...#", "#..##", "#.#.#", "##..#", "#...#", ".###."],
        ['1'] = ["..#..", ".##..", "..#..", "..#..", "..#..", "..#..", ".###."],
        ['2'] = [".###.", "#...#", "....#", "...#.", "..#..", ".#...", "#####"],
        ['3'] = ["#####", "...#.", "..#..", "...#.", "....#", "#...#", ".###."],
        ['4'] = ["...#.", "..##.", ".#.#.", "#..#.", "#####", "...#.", "...#."],
        ['5'] = ["#####", "#....", "####.", "....#", "....#", "#...#", ".###."],
        ['6'] = ["..##.", ".#...", "#....", "####.", "#...#", "#...#", ".###."],
        ['7'] = ["#####", "....#", "...#.", "..#..", ".#...", ".#...", ".#..."],
        ['8'] = [".###.", "#...#", "#...#", ".###.", "#...#", "#...#", ".###."],
        ['9'] = [".###.", "#...#", "#...#", ".####", "....#", "...#.", ".##.."],
        ['A'] = [".###.", "#...#", "#...#", "#####", "#...#", "#...#", "#...#"],
        ['B'] = ["####.", "#...#", "#...#", "####.", "#...#", "#...#", "####."],
        ['C'] = [".###.", "#...#", "#....", "#....", "#....", "#...#", ".###."],
        ['D'] = ["###..", "#..#.", "#...#", "#...#", "#...#", "#..#.", "###.."],
        ['E'] = ["#####", "#....", "#....", "####.", "#....", "#....", "#####"],
        ['F'] = ["#####", "#....", "#....", "####.", "#....", "#....", "#...."],
        ['G'] = [".###.", "#...#", "#....", "#.###", "#...#", "#...#", ".####"],
        ['H'] = ["#...#", "#...#", "#...#", "#####", "#...#", "#...#", "#...#"],
        ['I'] = [".###.", "..#..", "..#..", "..#..", "..#..", "..#..", ".###."],
        ['J'] = ["..###", "...#.", "...#.", "...#.", "...#.", "#..#.", ".##.."],
        ['K'] = ["#...#", "#..#.", "#.#..", "##...", "#.#..", "#..#.", "#...#"],
        ['L'] = ["#....", "#....", "#....", "#....", "#....", "#....", "#####"],
        ['M'] = ["#...#", "##.##", "#.#.#", "#.#.#", "#...#", "#...#", "#...#"],
        ['N'] = ["#...#", "#...#", "##..#", "#.#.#", "#..##", "#...#", "#...#"],
        ['O'] = [".###.", "#...#", "#...#", "#...#", "#...#", "#...#", ".###."],
        ['P'] = ["####.", "#...#", "#...#", "####.", "#....", "#....", "#...."],
        ['Q'] = [".###.", "#...#", "#...#", "#...#", "#.#.#", "#..#.", ".##.#"],
        ['R'] = ["####.", "#...#", "#...#", "####.", "#.#..", "#..#.", "#...#"],
        ['S'] = [".####", "#....", "#....", ".###.", "....#", "....#", "####."],
        ['T'] = ["#####", "..#..", "..#..", "..#..", "..#..", "..#..", "..#.."],
        ['U'] = ["#...#", "#...#", "#...#", "#...#", "#...#", "#...#", ".###."],
        ['V'] = ["#...#", "#...#", "#...#", "#...#", "#...#", ".#.#.", "..#.."],
        ['W'] = ["#...#", "#...#", "#...#", "#.#.#", "#.#.#", "#.#.#", ".#.#."],
        ['X'] = ["#...#", "#...#", ".#.#.", "..#..", ".#.#.", "#...#", "#...#"],
        ['Y'] = ["#...#", "#...#", ".#.#.", "..#..", "..#..", "..#..", "..#.."],
        ['Z'] = ["#####", "....#", "...#.", "..#..", ".#...", "#....", "#####"],
        ['.'] = [".....", ".....", ".....", ".....", ".....", ".##..", ".##.."],
        [','] = [".....", ".....", ".....", ".....", ".##..", "..#..", ".#..."],
        ['!'] = ["..#..", "..#..", "..#..", "..#..", "..#..", ".....", "..#.."],
        ['?'] = [".###.", "#...#", "....#", "...#.", "..#..", ".....", "..#.."],
        ['-'] = [".....", ".....", ".....", "#####", ".....", ".....", "....."],
        ['+'] = [".....", "..#..", "..#..", "#####", "..#..", "..#..", "....."],
        [':'] = [".....", ".##..", ".##..", ".....", ".##..", ".##..", "....."],
        [';'] = [".....", ".##..", ".##..", ".....", ".##..", "..#..", ".#..."],
        ['\''] = [".##..", "..#..", ".#...", ".....", ".....", ".....", "....."],
        ['"'] = [".#.#.", ".#.#.", ".#.#.", ".....", ".....", ".....", "....."],
        ['/'] = [".....", "....#", "...#.", "..#..", ".#...", "#....", "....."],
        ['('] = ["...#.", "..#..", ".#...", ".#...", ".#...", "..#..", "...#."],
        [')'] = [".#...", "..#..", "...#.", "...#.", "...#.", "..#..", ".#..."],
        ['&'] = [".##..", "#..#.", "#.#..", ".#...", "#.#.#", "#..#.", ".##.#"],
        ['#'] = [".#.#.", ".#.#.", "#####", ".#.#.", "#####", ".#.#.", ".#.#."],
        ['='] = [".....", ".....", "#####", ".....", "#####", ".....", "....."],
        ['*'] = [".....", "..#..", "#.#.#", ".###.", "#.#.#", "..#..", "....."],
        ['_'] = [".....", ".....", ".....", ".....", ".....", ".....", "#####"],
        ['<'] = ["...#.", "..#..", ".#...", "#....", ".#...", "..#..", "...#."],
        ['>'] = [".#...", "..#..", "...#.", "....#", "...#.", "..#..", ".#..."],
        ['%'] = ["##...", "##..#", "...#.", "..#..", ".#...", "#..##", "...##"],
        ['$'] = ["..#..", ".####", "#.#..", ".###.", "..#.#", "####.", "..#.."],
        ['@'] = [".###.", "#...#", "....#", ".##.#", "#.#.#", "#.#.#", ".###."],
    };

    /// <summary>Turkish's own capitals: a letter it is built on, and the mark above or below it.</summary>
    private static readonly Dictionary<char, (char Base, string? Above, string? Below)> Marked = new()
    {
        ['Ç'] = ('C', null, "..#.."),
        ['Ş'] = ('S', null, "..#.."),
        ['Ğ'] = ('G', ".###.", null),
        ['İ'] = ('I', "..#..", null),
        ['Ö'] = ('O', ".#.#.", null),
        ['Ü'] = ('U', ".#.#.", null),
    };

    /// <summary>
    /// A letter's rows, top first, and how many of them are above the line: one for a mark on top,
    /// else none. A letter the font has not got is a box, so a missing one is seen rather than lost.
    /// </summary>
    public static (IReadOnlyList<string> Rows, int Above) Glyph(char letter)
    {
        // Capitals as the invariant culture makes them, so an i is an I and not the Turkish İ.
        char upper = letter switch
        {
            'ç' => 'Ç',
            'ş' => 'Ş',
            'ğ' => 'Ğ',
            'ö' => 'Ö',
            'ü' => 'Ü',
            'ı' => 'I',
            _ => char.ToUpperInvariant(letter),
        };

        if (Marked.TryGetValue(upper, out (char Base, string? Above, string? Below) marked))
        {
            List<string> rows = [.. Glyphs[marked.Base]];
            if (marked.Above is { } above)
            {
                rows.Insert(0, above);
            }

            if (marked.Below is { } below)
            {
                rows.Add(below);
            }

            return (rows, marked.Above is null ? 0 : 1);
        }

        return Glyphs.TryGetValue(upper, out string[]? glyph)
            ? (glyph, 0)
            : (["#####", "#...#", "#...#", "#...#", "#...#", "#...#", "#####"], 0);
    }

    /// <summary>
    /// The voxels of <paramref name="text"/> set in the font: reading along +X, facing +Z, each of the
    /// font's pixels <paramref name="size"/> voxels square and <paramref name="depth"/> deep, letters
    /// <paramref name="spacing"/> pixels apart. Lines break at a new line and stack downwards; the
    /// last line stands on y = 0, and the whole is centred across, as every shape is.
    /// </summary>
    public static HashSet<Int3> Cells(string text, int size, int depth, int spacing)
    {
        var pixels = new List<(int X, int Y)>();
        string[] lines = text.Replace("\r", string.Empty).Split('\n');
        int lineHeight = Height + 3;

        for (int line = 0; line < lines.Length; line++)
        {
            // From the bottom line up, the baseline of each two rows under its body for a cedilla.
            int baseline = ((lines.Length - 1 - line) * lineHeight) + 1;
            int x = 0;

            foreach (char letter in lines[line])
            {
                (IReadOnlyList<string> rows, int above) = Glyph(letter);
                for (int r = 0; r < rows.Count; r++)
                {
                    // Row `above` is the body's top; the body's bottom row sits on the baseline.
                    int y = baseline + (Height - 1) - (r - above);
                    for (int c = 0; c < Width; c++)
                    {
                        if (rows[r][c] == '#')
                        {
                            pixels.Add((x + c, y));
                        }
                    }
                }

                x += Width + spacing;
            }
        }

        var cells = new HashSet<Int3>();
        if (pixels.Count == 0)
        {
            return cells;
        }

        int minX = pixels.Min(p => p.X), maxX = pixels.Max(p => p.X), minY = pixels.Min(p => p.Y);
        int widthVoxels = (maxX - minX + 1) * size;
        int left = -(widthVoxels / 2);
        int front = -(depth / 2);

        foreach ((int px, int py) in pixels)
        {
            for (int dx = 0; dx < size; dx++)
            {
                for (int dy = 0; dy < size; dy++)
                {
                    for (int dz = 0; dz < depth; dz++)
                    {
                        cells.Add(new Int3(left + ((px - minX) * size) + dx, ((py - minY) * size) + dy, front + dz));
                    }
                }
            }
        }

        return cells;
    }
}
