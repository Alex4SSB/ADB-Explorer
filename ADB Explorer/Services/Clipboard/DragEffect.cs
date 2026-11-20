namespace ADB_Explorer.Services;

/// <summary>
/// What a drop does, as the drag tooltip shows it. The first four match <see cref="DragDropEffects"/>'s values;
/// the rest are ours, carried as one of those to the drag but told apart for the tooltip's glyph.
/// </summary>
public enum DragEffect
{
    None = DragDropEffects.None,
    Copy = DragDropEffects.Copy,
    Move = DragDropEffects.Move,
    Link = DragDropEffects.Link,
    Install = 0x100,
    NewTab,
    SplitPane,
    OpenInExplorer,
}
