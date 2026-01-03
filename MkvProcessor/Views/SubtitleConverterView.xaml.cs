using System.Windows;
using System.Windows.Controls;
using MkvProcessor.ViewModels;

namespace MkvProcessor.Views;

/// <summary>
/// Interaction logic for SubtitleConverterView.xaml
/// </summary>
public partial class SubtitleConverterView : UserControl
{
    public SubtitleConverterView()
    {
        InitializeComponent();
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var paths = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (paths != null && DataContext is SubtitleConverterViewModel viewModel)
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
}
