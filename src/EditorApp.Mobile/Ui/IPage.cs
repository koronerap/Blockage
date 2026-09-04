namespace EditorApp.Mobile.Ui;

/// <summary>
/// A full-screen page that scrolls.
///
/// It exists for one rule: a page opens at the top. Reopening one where it was left is confusing —
/// the header is off screen and it reads as a different page — but a page rebuilt because a choice
/// on it changed must stay exactly where it was, or every tap would throw the controls out from
/// under the finger. Those are two different moments and only the first resets.
/// </summary>
public interface IPage
{
    void ResetScroll();
}
