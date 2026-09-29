using System.Diagnostics;
using System.Numerics;
using EditorApp.Core.Scene;

namespace EditorApp.Core.Rendering;

/// <summary>
/// What a render can be made into beyond one picture (Fullreleaseplan 5.5): a turntable — the level
/// turning in front of the camera — a sheet of sprites from all round it, and a video of it.
/// </summary>
public static class RenderOutputs
{
    /// <summary>
    /// The middle of what a render shows: the centre of every shown object's box in the world, or
    /// the origin for a level with nothing in it. A turntable turns round it.
    /// </summary>
    public static Vector3 LevelCentre(VoxelScene scene)
    {
        Vector3 min = new(float.MaxValue);
        Vector3 max = new(float.MinValue);
        bool any = false;
        foreach (VoxelObject o in scene.Objects)
        {
            if (o.Visible && o.TryGetWorldBounds(out Vector3 objectMin, out Vector3 objectMax))
            {
                min = Vector3.Min(min, objectMin);
                max = Vector3.Max(max, objectMax);
                any = true;
            }
        }

        return any ? (min + max) * 0.5f : Vector3.Zero;
    }

    /// <summary>
    /// <paramref name="count"/> cameras round the upright axis through <paramref name="centre"/>,
    /// the first where <paramref name="start"/> is and each a step further round — the camera
    /// orbiting, which looks the same as the level turning on a table in front of it.
    /// </summary>
    public static RenderCamera[] Orbit(RenderCamera start, Vector3 centre, int count)
    {
        count = Math.Max(count, 1);
        var cameras = new RenderCamera[count];
        for (int i = 0; i < count; i++)
        {
            var turn = Quaternion.CreateFromAxisAngle(Vector3.UnitY, -MathF.Tau * i / count);
            cameras[i] = start with
            {
                Position = centre + Vector3.Transform(start.Position - centre, turn),
                Forward = Vector3.Transform(start.Forward, turn),
                Up = Vector3.Transform(start.Up, turn),
            };
        }

        return cameras;
    }

    /// <summary>
    /// Pictures of one size laid out in a grid, <paramref name="columns"/> across, left to right and
    /// top to bottom: a sprite sheet. The cells left over at the end are see-through.
    /// </summary>
    public static (byte[] Rgba, int Width, int Height) Sheet(IReadOnlyList<byte[]> pictures, int width, int height, int columns)
    {
        columns = Math.Clamp(columns, 1, Math.Max(pictures.Count, 1));
        int rows = (pictures.Count + columns - 1) / columns;
        int sheetWidth = width * columns;
        int sheetHeight = height * Math.Max(rows, 1);
        var sheet = new byte[sheetWidth * sheetHeight * 4];

        for (int i = 0; i < pictures.Count; i++)
        {
            int left = (i % columns) * width;
            int top = (i / columns) * height;
            for (int y = 0; y < height; y++)
            {
                Array.Copy(pictures[i], y * width * 4, sheet, (((top + y) * sheetWidth) + left) * 4, width * 4);
            }
        }

        return (sheet, sheetWidth, sheetHeight);
    }

    /// <summary>Where ffmpeg is on this computer's PATH; null when it is not. Blockage does not bring its own.</summary>
    public static string? FindFfmpeg()
    {
        string name = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";
        foreach (string directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                string candidate = Path.Combine(directory.Trim('"'), name);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch (ArgumentException)
            {
            }
        }

        return null;
    }

    /// <summary>
    /// An MP4 of <paramref name="frames"/> by ffmpeg, handed the pictures raw through its input: H.264
    /// in the pixel format every player takes, padded to even sides as that format needs. Returns
    /// ffmpeg's complaint when it fails, null when it succeeds.
    /// </summary>
    public static string? WriteMp4(string ffmpeg, string path, IReadOnlyList<byte[]> frames, int width, int height, int framesPerSecond)
    {
        var start = new ProcessStartInfo(ffmpeg)
        {
            RedirectStandardInput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (string argument in new[]
        {
            "-y", "-loglevel", "error",
            "-f", "rawvideo", "-pixel_format", "rgba", "-video_size", $"{width}x{height}", "-framerate", $"{framesPerSecond}",
            "-i", "-",
            "-vf", "pad=ceil(iw/2)*2:ceil(ih/2)*2",
            "-c:v", "libx264", "-pix_fmt", "yuv420p", "-crf", "18", "-movflags", "+faststart",
            path,
        })
        {
            start.ArgumentList.Add(argument);
        }

        using Process? process = Process.Start(start);
        if (process is null)
        {
            return "ffmpeg did not start.";
        }

        Task<string> errors = process.StandardError.ReadToEndAsync();
        try
        {
            Stream input = process.StandardInput.BaseStream;
            foreach (byte[] frame in frames)
            {
                input.Write(frame);
            }

            input.Flush();
            process.StandardInput.Close();
        }
        catch (IOException)
        {
            // ffmpeg stopped taking frames; what it said about why is below.
        }

        process.WaitForExit();
        string said = errors.Result.Trim();
        return process.ExitCode == 0 ? null : (said.Length > 0 ? said : $"ffmpeg stopped with code {process.ExitCode}.");
    }
}
