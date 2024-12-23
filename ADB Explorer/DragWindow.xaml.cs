﻿using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.Services;

namespace ADB_Explorer;

/// <summary>
/// Interaction logic for DragWindow.xaml
/// </summary>
public partial class DragWindow : Window
{
    private readonly DispatcherTimer DragTimer = new() { Interval = TimeSpan.FromMilliseconds(50) };

    public bool MouseWithinApp = false;

    public DragWindow()
    {
        InitializeComponent();

        DragTimer.Tick += DragTimer_Tick;
    }

    private void DragTimer_Tick(object sender, EventArgs e)
    {
        App.Current.Dispatcher.Invoke(() =>
        {
            if (Data.CopyPaste.DragFiles.Length > 0 && Data.CopyPaste.CurrentDropEffect is not DragDropEffects.None)
            {
                var target = MouseWithinApp
                    ? " to " + FileHelper.GetFullName(Data.CopyPaste.DropTarget)
                    : "";

                DragTooltip.Text = $"{Data.CopyPaste.CurrentDropEffect} {Data.CopyPaste.DragFiles.Length} item(s){target}";
            }
            else
                DragTooltip.Text = "";
        });
    }

    private string ProcessUnderMouse = "";

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        NativeMethods.InitInterceptMouse(NativeMethods.MouseMessages.WM_MOUSEMOVE,
            (NativeMethods.POINT point) =>
            {
                if (Data.RuntimeSettings.DragBitmap is null)
                    return;
                
                Top = point.Y - DragImage.ActualHeight - 4;
                Left = point.X - (DragImage.ActualWidth / 2);

                MouseWithinApp = Data.RuntimeSettings.MainWinRect.Contains(point);
                if (!MouseWithinApp)
                {
                    if (Data.CopyPaste.DragStatus is CopyPasteService.DragState.None)
                        Data.RuntimeSettings.DragBitmap = null;

                    if (NativeMethods.GetPidFromPoint() is int pid
                        && Process.GetProcessById(pid) is Process proc)
                    {
                        ProcessUnderMouse = proc.ProcessName;
                        Data.RuntimeSettings.DragWithinSlave = ProcessUnderMouse == Properties.Resources.AppDisplayName;
                    }
                    else
                        ProcessUnderMouse = "";
                }
                else
                    Data.RuntimeSettings.DragWithinSlave = false;
            });

        DragTimer.Start();
    }

    private void Window_Closing(object sender, CancelEventArgs e)
    {
        NativeMethods.CloseInterceptMouse();
    }

    private void Border_MouseUp(object sender, MouseButtonEventArgs e)
    {
        Data.RuntimeSettings.DragBitmap = null;
    }
}

