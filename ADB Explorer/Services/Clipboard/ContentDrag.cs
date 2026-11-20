namespace ADB_Explorer.Services;

/// <summary>
/// A drag of something the app draws itself instead of files - a tab header or a device's row. The drag window
/// shows <see cref="CopyPasteService.DragContent"/> from its template, held where it was grabbed.
/// </summary>
public static class ContentDrag
{
    /// <summary>Drags <paramref name="content"/> until the button's released. Esc or a right click cancels, as in a file drag.</summary>
    /// <param name="imageRectPx">The drawn content's bounds in pixels, relative to the cursor.</param>
    /// <returns>Whether the drag ended with the button released, rather than cancelled.</returns>
    public static bool Run(UIElement source, object content, Rect imageRectPx, DataObject data, DragDropEffects allowedEffects)
    {
        Data.CopyPaste.CurrentDropEffect = DragDropEffects.None;
        Data.CopyPaste.DragContent = content;
        Data.CopyPaste.DragImageRectPx = imageRectPx;

        // A drag is under way while a bitmap is set; a blank one stands in, as the content is drawn from its template
        Data.CopyPaste.DragBitmap = BlankBitmap;
        Data.CopyPaste.WasDragging = true;

        var isCancelled = false;
        void OnQueryContinue(object sender, QueryContinueDragEventArgs e)
        {
            if (!e.EscapePressed && !e.KeyStates.HasFlag(DragDropKeyStates.RightMouseButton))
                return;

            isCancelled = true;
            e.Action = DragAction.Cancel;
            e.Handled = true;
        }

        DragDrop.AddGiveFeedbackHandler(source, OnGiveFeedback);
        DragDrop.AddQueryContinueDragHandler(source, OnQueryContinue);
        try
        {
            DragDrop.DoDragDrop(source, data, allowedEffects);
        }
        finally
        {
            DragDrop.RemoveGiveFeedbackHandler(source, OnGiveFeedback);
            DragDrop.RemoveQueryContinueDragHandler(source, OnQueryContinue);

            Data.CopyPaste.DragBitmap = null;
            Data.CopyPaste.DragContent = null;
            Data.CopyPaste.DragImageRectPx = null;
            Data.CopyPaste.DragTabTooltip = null;
        }

        ClearWasDraggingWhenIdle(source);

        return !isCancelled;
    }

    /// <summary>The drag window says what a drop does, so Windows' own cursors are replaced by the plain arrow.</summary>
    private static void OnGiveFeedback(object sender, GiveFeedbackEventArgs e)
    {
        e.UseDefaultCursors = false;
        Mouse.SetCursor(Cursors.Arrow);
        e.Handled = true;
    }

    /// <summary>
    /// A drag cancelled with a button leaves buttons held, whose releases must not act as clicks, so
    /// the flag stays until none is down - checked after each release, once its menus had their chance.
    /// </summary>
    private static void ClearWasDraggingWhenIdle(UIElement source)
    {
        if (Window.GetWindow(source) is not { } window)
        {
            Data.CopyPaste.WasDragging = false;
            return;
        }

        MouseButtonEventHandler? released = null;

        void Check()
        {
            if (MouseState.IsAnyButtonDown)
                return;

            window.RemoveHandler(UIElement.PreviewMouseUpEvent, released!);
            Data.CopyPaste.WasDragging = false;
        }

        released = (_, _) => App.SafeBeginInvoke(Check, DispatcherPriority.Background);
        window.AddHandler(UIElement.PreviewMouseUpEvent, released, true);

        // A drag that ended in a drop has no release still to come
        App.SafeBeginInvoke(Check, DispatcherPriority.Input);
    }

    private static readonly BitmapSource BlankBitmap = CreateBlankBitmap();

    private static BitmapSource CreateBlankBitmap()
    {
        var bitmap = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Pbgra32, null, new byte[4], 4);
        bitmap.Freeze();

        return bitmap;
    }
}
