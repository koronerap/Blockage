using System.Numerics;
using EditorApp.Core.Editing;
using EditorApp.Core.Export.Mimicraft;
using EditorApp.Core.Voxels;
using EditorApp.Ui;
using ImGuiNET;

namespace EditorApp;

/// <summary>
/// Saving a level as something Mimicraft reads: a <c>.character</c> or a <c>.weapons</c>.
///
/// Not an export in the sense the mesh exporters mean it — nothing is meshed and no texture is
/// written. What leaves is the voxels themselves, in the byte layout the game's own decoders expect,
/// so a model built here arrives there as a model rather than as geometry.
///
/// The dialog leads with what would be refused. Those decoders reject rather than repair, and the
/// only sign on the other side is a line in the Unity console, so the check belongs in front of the
/// person who can still fix it.
/// </summary>
public sealed class MimicraftController(EditorSession session)
{
    private const string PopupId = "mimicraft-save";

    /// <summary>
    /// The visible half is a title; the half after "###" is what ImGui matches on. Both OpenPopup
    /// and BeginPopupModal have to be given this exact string.
    /// </summary>
    private const string Label = "Save for Mimicraft###mimicraft-save";

    private readonly FileBrowserDialog _browser = new();

    private MimicraftTarget _target = MimicraftTarget.Character;
    private MimicraftUpAxis _up = MimicraftUpAxis.Y;
    private int _turnIndex;
    private string _characterName = string.Empty;
    private string _rigId = "steve";
    private string _weaponId = string.Empty;

    private bool _shouldOpenPopup;
    private bool _isOpen;
    private string _status = string.Empty;
    private bool _statusIsError;

    private IReadOnlyList<MimicraftProblem> _problems = [];

    public void Show()
    {
        // As the mesh export: an empty level is said to be one, rather than opening onto nothing.
        if (!session.Scene.Objects.Any(o => o.IsExported && !o.IsEmpty))
        {
            Ui.ReportLog.Shared.Post("Nothing to save for Mimicraft - the level has no voxels that are shown.", Ui.ReportKind.Warning);
            return;
        }

        _isOpen = true;
        _shouldOpenPopup = true;
        _status = string.Empty;
        _statusIsError = false;

        // Both ids follow the level unless they have been typed over, the same rule the mesh export
        // dialog learned the hard way.
        if (_characterName.Length == 0)
        {
            _characterName = session.ProjectName;
        }

        if (_weaponId.Length == 0)
        {
            _weaponId = session.ProjectName;
        }

        Validate();
    }

    private void Validate() => _problems = MimicraftValidation.Check(session.Scene, _target);

    public void Draw()
    {
        if (_shouldOpenPopup)
        {
            ImGui.OpenPopup(Label);
            _shouldOpenPopup = false;
        }

        if (!_isOpen)
        {
            return;
        }

        ImGui.SetNextWindowSize(new Vector2(720f, 0f), ImGuiCond.Always);

        bool open = true;
        if (!ImGui.BeginPopupModal(Label, ref open, ImGuiWindowFlags.NoSavedSettings))
        {
            return;
        }

        DrawTargetPicker();
        ImGui.Spacing();
        DrawOrientation();
        ImGui.Spacing();
        DrawFields();

        ImGui.SeparatorText("Check");
        DrawProblems();

        ImGui.Spacing();
        DrawActions();

        // Inside the popup, so the browser nests rather than replacing this dialog.
        _browser.Draw();

        ImGui.EndPopup();

        if (!open)
        {
            _isOpen = false;
        }
    }

    private void DrawTargetPicker()
    {
        ImGui.Text("Save as");

        int target = (int)_target;
        if (ImGui.RadioButton("Character  -  one rig slot per object, named by the object", ref target, 0) |
            ImGui.RadioButton("Weapon  -  every object merged into one model", ref target, 1))
        {
            if (target != (int)_target)
            {
                _target = (MimicraftTarget)target;
                _status = string.Empty;
                Validate();
            }
        }
    }

    private MimicraftOrientation Orientation =>
        new(_up, MimicraftOrientation.Turns[_turnIndex]);

    /// <summary>
    /// How the model was built, which is the one thing the file cannot say for itself.
    ///
    /// Converting from this editor's right-handed axes to Unity's left-handed ones happens either
    /// way and is not offered as a choice. What is offered is everything a modeller might reasonably
    /// have done: built the thing lying on its side, or facing along a different horizontal axis.
    /// Every combination here is a rotation, so none of them can mirror the model by accident.
    /// </summary>
    private void DrawOrientation()
    {
        ImGui.Text("Built with");

        ImGui.SetNextItemWidth(140f);
        int up = (int)_up;
        if (ImGui.Combo("Up axis", ref up, "Y up\0Z up\0X up\0") && up != (int)_up)
        {
            _up = (MimicraftUpAxis)up;
        }

        ImGui.SameLine();
        ImGui.SetNextItemWidth(140f);
        int turn = _turnIndex;
        if (ImGui.Combo("Turn", ref turn, "0\0 90\0 180\0 270\0") && turn != _turnIndex)
        {
            _turnIndex = turn;
        }

        // The fastest way to tell whether the choice is right without going to Unity: a truck is
        // long along one axis, and if the long number is in the wrong place so is the model.
        if (Size() is { } size)
        {
            ImGui.TextDisabled($"Arrives as {size.X} x {size.Y} x {size.Z} voxels (x, y, z in Unity)");
        }
    }

    /// <summary>The written box, so the dialog can show what the orientation actually produces.</summary>
    private Int3? Size()
    {
        IReadOnlyList<Core.Scene.VoxelObject> objects =
            [.. session.Scene.Objects.Where(o => o.IsExported && !o.IsEmpty)];

        if (objects.Count == 0)
        {
            return null;
        }

        if (_target == MimicraftTarget.Weapon)
        {
            return MimicraftValidation.TryMergedBounds(objects, out Int3 min, out Int3 max)
                ? Orientation.Size(max - min + Int3.One)
                : null;
        }

        // For a character the parts are separate boxes; the focused one is the one being looked at.
        Core.Scene.VoxelObject shown = session.Scene.Focus is { Visible: true, IsEmpty: false } focus
            ? focus
            : objects[0];

        return shown.Grid.TryGetBounds(out Int3 partMin, out Int3 partMax)
            ? Orientation.Size(partMax - partMin + Int3.One)
            : null;
    }

    private void DrawFields()
    {
        if (_target == MimicraftTarget.Character)
        {
            ImGui.SetNextItemWidth(280f);
            string name = _characterName;
            if (ImGui.InputText("Character name", ref name, 64))
            {
                _characterName = name;
            }

            ImGui.SetNextItemWidth(280f);
            string rig = _rigId;
            if (ImGui.InputText("Rig id", ref rig, 64))
            {
                _rigId = rig;
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("The rig this character was built for.\nObject names have to match its Part Ids.");
            }

            ImGui.TextDisabled($"Parts: {string.Join(", ", session.Scene.Objects.Where(o => o.IsExported && !o.IsEmpty).Select(o => o.Name))}");
            return;
        }

        ImGui.SetNextItemWidth(280f);
        string weapon = _weaponId;
        if (ImGui.InputText("Weapon id", ref weapon, 64))
        {
            _weaponId = weapon;
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Has to match the WeaponDefinition's id.\nThe file is usually named after it.");
        }

        ImGui.TextDisabled("Grip points are left unset, so the weapon prefab's own grips are used.");
    }

    private void DrawProblems()
    {
        // Said rather than enforced. The piece size limit lives in Mimicraft's own source, so it is
        // a number that can be raised — but a file that goes over it while the game still holds the
        // stock value is refused there with nothing to show for it but a console line, and that is
        // worth a sentence here.
        int largest = MimicraftValidation.LargestExtent(session.Scene, _target);
        if (largest > MimicraftBody.StockBoxExtent)
        {
            ImGui.TextWrapped(
                $"Largest side is {largest} voxels. An unmodified game stops at "
                + $"{MimicraftBody.StockBoxExtent} - raise MaxPieceBoxExtent in VoxelBodyCodec if you "
                + "have not already, or this is refused on the other side.");
            ImGui.Spacing();
        }

        if (_problems.Count == 0)
        {
            ImGui.TextColored(Theme.Success, "Nothing here would be refused.");
            return;
        }

        ImGui.TextColored(
            Theme.Danger,
            $"{_problems.Count} thing(s) Mimicraft would refuse. The file is not written until they are fixed.");

        foreach (MimicraftProblem problem in _problems)
        {
            ImGui.Bullet();
            ImGui.TextColored(Theme.Highlight, problem.Subject);
            ImGui.SameLine();
            ImGui.TextWrapped(problem.Message);
        }
    }

    private void DrawActions()
    {
        if (_status.Length > 0)
        {
            ImGui.TextColored(_statusIsError ? Theme.Danger : Theme.Success, _status);
        }

        ImGui.BeginDisabled(_problems.Count > 0);
        if (ImGui.Button("Save as...", Theme.ModalButton))
        {
            string extension = _target == MimicraftTarget.Character
                ? MimicraftFiles.CharacterExtension
                : MimicraftFiles.WeaponExtension;

            string suggested = _target == MimicraftTarget.Character ? _characterName : _weaponId;

            _browser.Show(
                FileBrowserMode.Save,
                _target == MimicraftTarget.Character ? "Save character" : "Save weapon",
                extension,
                session.ProjectPath is { } path ? Path.GetDirectoryName(path) : null,
                suggested + extension,
                Write);
        }

        ImGui.EndDisabled();

        ImGui.SameLine();
        if (ImGui.Button("Re-check", Theme.ModalButton))
        {
            Validate();
        }

        ImGui.SameLine();
        if (ImGui.Button("Close", Theme.ModalButton))
        {
            _isOpen = false;
            ImGui.CloseCurrentPopup();
        }
    }

    private void Write(string path)
    {
        try
        {
            // Re-checked at the moment of writing: the level may have been edited since the dialog
            // was opened, and the button being enabled is not proof that it still should be.
            Validate();
            if (_problems.Count > 0)
            {
                _status = "Not written - see the list above.";
                _statusIsError = true;
                return;
            }

            byte[] bytes = _target == MimicraftTarget.Character
                ? MimicraftFiles.EncodeCharacter(
                    _characterName,
                    _rigId,
                    MimicraftScene.BuildCharacterParts(session.Scene),
                    session.Scene.Palette,
                    Orientation)
                : MimicraftFiles.EncodeWeapon(
                    _weaponId,
                    MimicraftScene.BuildWeaponPiece(session.Scene, _weaponId),
                    session.Scene.Palette,
                    Orientation);

            File.WriteAllBytes(path, bytes);

            _status = $"Wrote {Path.GetFileName(path)} ({bytes.Length / 1024.0:0.0} KB) to {Path.GetDirectoryName(path)}.";
            _statusIsError = false;
        }
        catch (Exception exception)
        {
            _status = $"Could not write: {exception.Message}";
            _statusIsError = true;
            CrashLog.Record($"writing {path}", exception);
        }
    }
}
