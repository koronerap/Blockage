namespace EditorApp.Ui;

public enum ReportKind
{
    Info,
    Warning,
    Error,
}

public sealed record Report(string Text, ReportKind Kind, double PostedAt);

/// <summary>
/// What the editor has just done or failed to do — "Saved castle.vxlevel", "Copied 48 voxels",
/// "Could not open …" — shown in the status bar for a few seconds and then gone, as Blender reports.
///
/// It used to sit in the menu bar for good, so a message about a file opened an hour ago stayed on
/// screen as though it were news. Errors stay longer than the rest, and in red, because they are the
/// ones that must not be missed. The last few are kept for anyone who looks away at the wrong moment.
/// </summary>
public sealed class ReportLog(Func<double> clock)
{
    public const double InfoSeconds = 4;
    public const double WarningSeconds = 6;
    public const double ErrorSeconds = 10;

    /// <summary>The last stretch of a report's life, over which it fades out rather than vanishing.</summary>
    public const double FadeSeconds = 0.6;

    private const int Kept = 20;

    private readonly List<Report> _recent = [];

    // Posted to from wherever work finishes, which is not always the frame's own thread.
    private readonly Lock _gate = new();

    public ReportLog()
        : this(() => Environment.TickCount64 / 1000.0)
    {
    }

    /// <summary>
    /// The editor's one log. Shared rather than handed round because reports come from everywhere —
    /// menus, keys, dialogs, the project — and the status bar is the one place they all go.
    /// </summary>
    public static ReportLog Shared { get; } = new();

    /// <summary>The most recent first, as a copy.</summary>
    public IReadOnlyList<Report> Recent
    {
        get
        {
            lock (_gate)
            {
                return [.. _recent];
            }
        }
    }

    public void Post(string text, ReportKind kind = ReportKind.Info)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        lock (_gate)
        {
            _recent.Insert(0, new Report(text, kind, clock()));
            if (_recent.Count > Kept)
            {
                _recent.RemoveAt(_recent.Count - 1);
            }
        }
    }

    /// <summary>The report still on screen, or null once the latest has run its time.</summary>
    public Report? Current
    {
        get
        {
            lock (_gate)
            {
                return _recent.Count > 0 && clock() - _recent[0].PostedAt < Lifetime(_recent[0].Kind) ? _recent[0] : null;
            }
        }
    }

    /// <summary>How visible the current report is: 1, easing to 0 over its last moments.</summary>
    public float Opacity
    {
        get
        {
            if (Current is not { } report)
            {
                return 0f;
            }

            double left = Lifetime(report.Kind) - (clock() - report.PostedAt);
            return (float)Math.Clamp(left / FadeSeconds, 0.0, 1.0);
        }
    }

    public static double Lifetime(ReportKind kind) => kind switch
    {
        ReportKind.Error => ErrorSeconds,
        ReportKind.Warning => WarningSeconds,
        _ => InfoSeconds,
    };
}
