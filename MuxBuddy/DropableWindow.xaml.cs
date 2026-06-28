using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Shell;
using DataFormats = System.Windows.DataFormats;
using DragDropEffects = System.Windows.DragDropEffects;
using DragEventArgs = System.Windows.DragEventArgs;
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;

namespace MuxBuddy;

public partial class DropableWindow
{
    private bool _allowClose = true;

    private DropableWindowViewModel ViewModel => (DropableWindowViewModel)DataContext;

    public DropableWindow()
    {
        InitializeComponent();

        ViewModel.MessageRequested += ShowMessage;
        ViewModel.EncodingCompleted += FlashTaskbar;
        ViewModel.ScrollToOutputRequested += () => Dispatcher.Invoke(VideoViewPanel.ScrollToEnd);
    }

    private void MainWindow_OnDrop(object sender, DragEventArgs e)
    {
        DropOverlay.Visibility = Visibility.Collapsed;
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            ViewModel.LoadVideo((string[])e.Data.GetData(DataFormats.FileDrop));
        }
    }

    private void Window_DragEnter(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            DropOverlay.Visibility = Visibility.Visible;
            e.Effects = DragDropEffects.Copy;
        }
    }

    private void Window_DragLeave(object sender, DragEventArgs e)
    {
        DropOverlay.Visibility = Visibility.Collapsed;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
            Hide();
        }
    }

    public void ExitApp()
    {
        _allowClose = true;
        Close();
    }

    private void TrayButton_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show("Mux Buddy is running in the background", "Mux Buddy", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void ShowMessage(string message, string caption, MessageBoxImage icon)
    {
        Dispatcher.Invoke(() => MessageBox.Show(message, caption, MessageBoxButton.OK, icon));
    }

    private void FlashTaskbar()
    {
        Dispatcher.Invoke(() =>
        {
            TaskbarInfo.ProgressState = TaskbarItemProgressState.None;
            WindowAttention.FlashTaskbar(new WindowInteropHelper(this).Handle);
        });
    }
}
