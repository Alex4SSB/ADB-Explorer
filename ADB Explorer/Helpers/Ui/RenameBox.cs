namespace ADB_Explorer.Helpers;

/// <summary>
/// Connects an inline rename <see cref="TextBox"/> to the file and navigation-tree rename logic.
/// </summary>
public static class RenameBox
{
    public static void PrepareFile(TextBox textBox)
    {
        if (textBox.DataContext is not FileClass file)
            return;

        textBox.ClearValue(TextBox.TextProperty);
        if (textBox.GetBindingExpression(TextBox.TextProperty) is { } expression)
            expression.UpdateTarget();
        else
            textBox.Text = FileHelper.DisplayName(file);

        FileTextChanged(textBox);
        textBox.Focus();
        textBox.SelectAll();
    }

    public static void FileTextChanged(TextBox textBox)
    {
        // Data.DirList can turn null mid-rename (navigation/refresh); guard it like CurrentDrive.
        if (textBox.DataContext is not FileClass file || Data.CurrentDrive is null || Data.DirList is null)
            return;

        var restrictions = DriveHelper.GetRestrictions(file.FullPath);
        textBox.FilterString(restrictions.InvalidNameChars);
        file.ActiveViewModel.UpdateRenameLegality(textBox.Text, restrictions);
    }

    public static void FileKeyDown(TextBox textBox, Key key, Action<FileClass> exitEditMode)
    {
        if (textBox.DataContext is not FileClass file)
            return;

        if (key is Key.Escape or Key.F2)
        {
            if (FileActionLogic.CancelRename(file, discardNew: key is Key.Escape) is { } name)
                textBox.Text = name;

            exitEditMode(file);
        }
        else if (key is Key.Enter)
            FileCommit(textBox, exitEditMode);
    }

    public static void FileCommit(TextBox textBox, Action<FileClass> exitEditMode)
    {
        if (textBox.DataContext is FileClass file && FileActionLogic.CommitRename(file, textBox.Text))
            exitEditMode(file);
    }

    public static void PrepareTreeNode(TextBox textBox, NavigationTreeViewModel tree)
    {
        if (textBox.DataContext is not NavigationTreeNode node)
            return;

        textBox.ClearValue(TextBox.TextProperty);
        textBox.Text = node.DisplayName;
        TreeNodeTextChanged(textBox, tree);
        textBox.Focus();
        textBox.SelectAll();
    }

    public static void TreeNodeTextChanged(TextBox textBox, NavigationTreeViewModel tree)
    {
        if (textBox.DataContext is not NavigationTreeNode node || NavigationTreeViewModel.GetRenameRestrictions(node) is not { } restrictions)
            return;

        textBox.FilterString(restrictions.InvalidNameChars);
        tree.UpdateRenameLegality(node, textBox.Text, restrictions);
    }

    public static void TreeNodeCommit(TextBox textBox, NavigationTreeViewModel tree, bool restorePreviousSelection = true)
    {
        if (textBox.DataContext is NavigationTreeNode)
            tree.CommitEdit(textBox.Text, restorePreviousSelection);
    }

    public static void TreeNodeEscape(TextBox textBox, NavigationTreeViewModel tree)
    {
        if (tree.EditingNode is { IsTemp: false } node && FileHelper.DisplayName(node.File) is { Length: > 0 } name)
            textBox.Text = name;

        tree.EscapeEdit();
    }
}
