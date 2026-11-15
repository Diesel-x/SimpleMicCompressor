using System.ComponentModel;
using System.Windows;
using MicCompressor.Tray;
using MicCompressor.ViewModels;

namespace MicCompressor;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private TrayManager? _trayManager;
    private bool _isExplicitExit = false;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Closing += OnClosing;
        IsVisibleChanged += (s, e) => UpdateMeterTimerState();
        StateChanged += (s, e) => UpdateMeterTimerState();
    }

    private void UpdateMeterTimerState()
    {
        if (DataContext is MainViewModel vm)
        {
            if (!IsVisible || WindowState == WindowState.Minimized)
            {
                vm.PauseMeters();
            }
            else
            {
                vm.ResumeMeters();
            }
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            _trayManager = new TrayManager(this, vm);
            vm.RequestMinimizeToTray = MinimizeToTray;

            vm.PropertyChanged += (s, args) =>
            {
                if (args.PropertyName == nameof(MainViewModel.IsSaveProfileDialogVisible) && vm.IsSaveProfileDialogVisible)
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        NewProfileTextBox.Focus();
                        NewProfileTextBox.SelectAll();
                    }));
                }
            };

            // Check command line arguments for --minimized
            string[] args = Environment.GetCommandLineArgs();
            bool startMinimized = vm.Settings.StartMinimized || args.Any(a => a.Equals("--minimized", StringComparison.OrdinalIgnoreCase));

            if (startMinimized)
            {
                MinimizeToTray();
            }
        }
    }

    private void OnMinimizeToTrayClicked(object sender, RoutedEventArgs e)
    {
        MinimizeToTray();
    }

    public void MinimizeToTray()
    {
        _trayManager?.MinimizeToTray();
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_isExplicitExit) return;

        if (DataContext is MainViewModel vm && vm.MinimizeToTrayOnClose)
        {
            e.Cancel = true;
            MinimizeToTray();
        }
        else
        {
            _trayManager?.Dispose();
            (DataContext as MainViewModel)?.Dispose();
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _trayManager?.Dispose();
        (DataContext as MainViewModel)?.Dispose();
        base.OnClosed(e);
    }
}
