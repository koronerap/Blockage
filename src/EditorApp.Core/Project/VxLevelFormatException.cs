namespace EditorApp.Core.Project;

/// <summary>Raised when a <c>.vxlevel</c> file is unreadable, corrupt, or from a future version.</summary>
public sealed class VxLevelFormatException(string message) : Exception(message);
