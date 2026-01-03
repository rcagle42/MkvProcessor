using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Navigation;
using MkvProcessor.ViewModels;

namespace MkvProcessor.Views;

/// <summary>
/// Interaction logic for TvRenamerView.xaml
/// </summary>
public partial class TvRenamerView : UserControl
{
    public TvRenamerView()
    {
        InitializeComponent();
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var paths = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (paths != null && DataContext is TvRenamerViewModel viewModel)
            {
                viewModel.AddDroppedFiles(paths);
            }
        }
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void EpisodeBrowserList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is TvRenamerViewModel viewModel && viewModel.AssignEpisodeCommand.CanExecute(null))
        {
            viewModel.AssignEpisodeCommand.Execute(null);
        }
    }

    private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }
}
