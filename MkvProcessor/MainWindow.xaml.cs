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
    private bool _forceClose = false;

    public MainWindow()
    {
        InitializeComponent();

        _viewModel = new MainViewModel();
        _tvRenamerViewModel = new TvRenamerViewModel();
        _subtitleConverterViewModel = new SubtitleConverterViewModel();

        DataContext = _viewModel;
        TvRenamerView.DataContext = _tvRenamerViewModel;
        SubtitleConverterView.DataContext = _subtitleConverterViewModel;

        // Subscribe to processing completed for tray notifications
        _viewModel.OnProcessingCompleted += OnProcessingCompleted;

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

    #region Window Closing / System Tray

    private void OnClosing(object sender, CancelEventArgs e)
    {
        if (_viewModel.Settings.MinimizeToTray && !_forceClose)
        {
            e.Cancel = true;
            Hide();
            TrayIcon.ShowBalloonTip(
                "MKV Batch Processor",
                "Application minimized to tray. Double-click to restore.",
                Hardcodet.Wpf.TaskbarNotification.BalloonIcon.Info);
        }
        else
        {
            // Save settings on close
            _viewModel.SaveSettings();
            _tvRenamerViewModel.SaveSettings();
            _subtitleConverterViewModel.SaveSettings();
            TrayIcon.Dispose();
        }
    }

    private void TrayIcon_TrayMouseDoubleClick(object sender, RoutedEventArgs e)
    {
        ShowAndActivate();
    }

    private void ShowWindow_Click(object sender, RoutedEventArgs e)
    {
        ShowAndActivate();
    }

    private void PauseResume_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.IsProcessing)
        {
            if (_viewModel.IsPaused)
                _viewModel.ResumeProcessingCommand.Execute(null);
            else
                _viewModel.PauseProcessingCommand.Execute(null);
        }
    }

    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.IsProcessing)
        {
            var result = MessageBox.Show(
                "Processing is in progress. Are you sure you want to exit?",
                "Confirm Exit",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
                return;

            _viewModel.CancelProcessingCommand.Execute(null);
        }

        _forceClose = true;
        Close();
    }

    private void ShowAndActivate()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void OnProcessingCompleted(ProcessingSummary summary)
    {
        if (_viewModel.Settings.ShowCompletionNotification)
        {
            var message = $"Completed: {summary.SuccessfulFiles} successful, {summary.FailedFiles} failed, {summary.SkippedFiles} skipped";
            TrayIcon.ShowBalloonTip("Processing Complete", message, Hardcodet.Wpf.TaskbarNotification.BalloonIcon.Info);
        }
    }

    #endregion
}
