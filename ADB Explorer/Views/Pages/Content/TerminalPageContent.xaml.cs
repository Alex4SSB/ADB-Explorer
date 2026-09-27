namespace ADB_Explorer.Views.Pages;

public partial class TerminalPageContent : UserControl
{
    public TerminalPageContent()
    {
        InitializeComponent();
    }

    private TerminalViewModel ViewModel => (TerminalViewModel)DataContext;

    private void Input_KeyUp(object sender, KeyEventArgs e)
    {
        if (e.Key is not Key.Enter || ViewModel.Execute(Input.Text) is not { } result)
            return;

        StdOut.Document.Blocks.Clear();
        StdOut.AppendText(result.StdOut);
        StdErr.Text = result.StdErr;
    }
}
