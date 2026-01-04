using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using MkvProcessor.Services;
using MkvProcessor.ViewModels;

namespace MkvProcessor;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly TvRenamerViewModel _tvRenamerViewModel;
    private readonly SubtitleConverterViewModel _subtitleConverterViewModel;

    public MainWindow()
    {
        InitializeComponent();

        _viewModel = new MainViewModel();
        _tvRenamerViewModel = new TvRenamerViewModel();
        _subtitleConverterViewModel = new SubtitleConverterViewModel();

        DataContext = _viewModel;
        TvRenamerView.DataContext = _tvRenamerViewModel;
        SubtitleConverterView.DataContext = _subtitleConverterViewModel;

        // Auto-scroll log to bottom when new content is added
        _viewModel.LogLines.CollectionChanged += (_, _) =>
        {
            Dispatcher.BeginInvoke(() =>
            {
                LogTextBox.ScrollToEnd();
            });
        };

        Loaded += MainWindow_Loaded;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        // Check for FFmpeg
        if (!FFmpegLocator.IsFFmpegAvailable)
        {
            MessageBox.Show(
                "FFmpeg was not found. Please place ffmpeg.exe and ffprobe.exe in the 'ffmpeg' folder next to this application, or ensure FFmpeg is in your system PATH.",
                "FFmpeg Not Found",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        await _viewModel.InitializeAsync();
        await _tvRenamerViewModel.InitializeAsync();
        _subtitleConverterViewModel.Initialize();
    }

    #region Drag and Drop

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

    private async void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var paths = (string[])e.Data.GetData(DataFormats.FileDrop);
            await _viewModel.AddDroppedItemsAsync(paths);
        }
    }

    #endregion

    #region Window Closing

    private void OnClosing(object sender, CancelEventArgs e)
    {
        if (_viewModel.IsProcessing)
        {
            var result = MessageBox.Show(
                "Processing is in progress. Are you sure you want to exit?",
                "Confirm Exit",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
            {
                e.Cancel = true;
                return;
            }

            _viewModel.CancelProcessingCommand.Execute(null);
        }

        // Save settings on close
        _viewModel.SaveSettings();
        _tvRenamerViewModel.SaveSettings();
        _subtitleConverterViewModel.SaveSettings();
    }

    #endregion
}
