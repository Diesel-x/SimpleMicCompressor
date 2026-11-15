using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;
using MicCompressor.Tray;
using MicCompressor.Utils;
using MicCompressor.ViewModels;

namespace MicCompressor;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private TrayManager? _trayManager;
    private GlobalHotkeyService? _hotkeyService;
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

            // Initialize Global Hotkeys (Low-level system hook, works in all fullscreen games)
            _hotkeyService = new GlobalHotkeyService(id => vm.HandleHotkey(id));
            _hotkeyService.Start(vm.Settings);

            vm.RequestUpdateHotkeys = () =>
            {
                _hotkeyService?.UpdateHotkeys(vm.Settings);
            };

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

            // Check command line arguments
            string[] args = Environment.GetCommandLineArgs();

            if (args.Any(a => a.Equals("--screenshot", StringComparison.OrdinalIgnoreCase)))
            {
                int idx = Array.FindIndex(args, a => a.Equals("--screenshot", StringComparison.OrdinalIgnoreCase));
                string path = (idx + 1 < args.Length && !args[idx + 1].StartsWith("--")) ? args[idx + 1] : "assets/screenshot.png";
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() =>
                {
                    CaptureAndSave(path);
                    System.Windows.Application.Current.Shutdown();
                }));
                return;
            }

            if (args.Any(a => a.Equals("--screenshot-modal", StringComparison.OrdinalIgnoreCase)))
            {
                int idx = Array.FindIndex(args, a => a.Equals("--screenshot-modal", StringComparison.OrdinalIgnoreCase));
                string path = (idx + 1 < args.Length && !args[idx + 1].StartsWith("--")) ? args[idx + 1] : "assets/screenshot_modal.png";
                vm.ProfileNameInput = "Стрим и Discord";
                vm.IsSaveProfileDialogVisible = true;
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() =>
                {
                    CaptureAndSave(path);
                    System.Windows.Application.Current.Shutdown();
                }));
                return;
            }

            if (args.Any(a => a.Equals("--screenshot-hotkeys", StringComparison.OrdinalIgnoreCase)))
            {
                int idx = Array.FindIndex(args, a => a.Equals("--screenshot-hotkeys", StringComparison.OrdinalIgnoreCase));
                string path = (idx + 1 < args.Length && !args[idx + 1].StartsWith("--")) ? args[idx + 1] : "assets/screenshot_hotkeys.png";
                vm.IsHotkeysDialogVisible = true;
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() =>
                {
                    CaptureAndSave(path);
                    System.Windows.Application.Current.Shutdown();
                }));
                return;
            }

            bool startMinimized = vm.Settings.StartMinimized || args.Any(a => a.Equals("--minimized", StringComparison.OrdinalIgnoreCase));

            if (startMinimized)
            {
                MinimizeToTray();
            }
        }
    }

    private void CaptureAndSave(string outputPath)
    {
        try
        {
            double width = 1160;
            double height = 840;

            Width = width;
            Height = height;
            Measure(new System.Windows.Size(width, height));
            Arrange(new System.Windows.Rect(new System.Windows.Size(width, height)));
            UpdateLayout();

            var dv = new System.Windows.Media.DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                var bg = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#131418"));
                dc.DrawRectangle(bg, null, new System.Windows.Rect(0, 0, width, height));
                var vb = new System.Windows.Media.VisualBrush(this) { Stretch = System.Windows.Media.Stretch.None };
                dc.DrawRectangle(vb, null, new System.Windows.Rect(0, 0, width, height));
            }

            var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(
                (int)width, (int)height, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            rtb.Render(dv);

            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));

            string? dir = System.IO.Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(dir))
            {
                System.IO.Directory.CreateDirectory(dir);
            }

            using var fs = System.IO.File.Create(outputPath);
            encoder.Save(fs);
        }
        catch (Exception ex)
        {
            System.IO.File.WriteAllText("screenshot_error.txt", ex.ToString());
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

    private void OnHyperlinkRequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            e.Handled = true;
        }
        catch { }
    }

    protected override void OnClosed(EventArgs e)
    {
        _hotkeyService?.Dispose();
        _trayManager?.Dispose();
        (DataContext as MainViewModel)?.Dispose();
        base.OnClosed(e);
    }
}
