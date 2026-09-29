using System.Numerics;
using EditorApp.Core.Commands;
using EditorApp.Core.Scene;

namespace EditorApp.Core.Editing;

/// <summary>A shape just added: the object, what it was made as and in, where it was set down, and the undo step it came in.</summary>
public sealed record AddedShape(VoxelObject Object, ShapeSettings Settings, byte Colour, Vector3 Point, Vector3 Normal, ICommand Command);
