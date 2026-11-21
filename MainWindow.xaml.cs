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

            int langIdx = Array.FindIndex(args, a => a.Equals("--lang", StringComparison.OrdinalIgnoreCase));
            if (langIdx != -1 && langIdx + 1 < args.Length)
            {
                string langCode = args[langIdx + 1].ToLowerInvariant();
                var opt = vm.AvailableLanguages.FirstOrDefault(l => l.Code.Equals(langCode, StringComparison.OrdinalIgnoreCase));
                if (opt != null)
                {
                    vm.SelectedLanguage = opt;
                }
            }

            if (args.Any(a => a.StartsWith("--screenshot", StringComparison.OrdinalIgnoreCase)))
            {
                vm.IsOnboardingVisible = false;
            }

            if (args.Any(a => a.Equals("--screenshot", StringComparison.OrdinalIgnoreCase)))
            {
                int idx = Array.FindIndex(args, a => a.Equals("--screenshot", StringComparison.OrdinalIgnoreCase));
                string path = (idx + 1 < args.Length && !args[idx + 1].StartsWith("--")) ? args[idx + 1] : "assets/screenshot.png";
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ContextIdle, new Action(() =>
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
                vm.ProfileNameInput = vm.SelectedLanguage?.Code == "en" ? "Streaming & Discord" : "Стрим и Discord";
                vm.IsSaveProfileDialogVisible = true;
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ContextIdle, new Action(() =>
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
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ContextIdle, new Action(() =>
                {
                    CaptureAndSave(path);
                    System.Windows.Application.Current.Shutdown();
                }));
                return;
            }

            if (args.Any(a => a.Equals("--screenshot-settings", StringComparison.OrdinalIgnoreCase)))
            {
                int idx = Array.FindIndex(args, a => a.Equals("--screenshot-settings", StringComparison.OrdinalIgnoreCase));
                string path = (idx + 1 < args.Length && !args[idx + 1].StartsWith("--")) ? args[idx + 1] : "assets/screenshot_settings.png";
                vm.IsSettingsDialogVisible = true;
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ContextIdle, new Action(() =>
                {
                    CaptureAndSave(path);
                    System.Windows.Application.Current.Shutdown();
                }));
                return;
            }

            if (args.Any(a => a.Equals("--screenshot-res", StringComparison.OrdinalIgnoreCase)))
            {
                int idx = Array.FindIndex(args, a => a.Equals("--screenshot-res", StringComparison.OrdinalIgnoreCase));
                double customW = 1180;
                double customH = 800;
                string path = "assets/screenshot_res.png";
                if (idx + 1 < args.Length && double.TryParse(args[idx + 1], System.Globalization.CultureInfo.InvariantCulture, out double w)) customW = w;
                if (idx + 2 < args.Length && double.TryParse(args[idx + 2], System.Globalization.CultureInfo.InvariantCulture, out double h)) customH = h;
                if (idx + 3 < args.Length && !args[idx + 3].StartsWith("--")) path = args[idx + 3];

                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ContextIdle, new Action(() =>
                {
                    CaptureAndSave(path, customW, customH);
                    System.Windows.Application.Current.Shutdown();
                }));
                return;
            }

            bool startMinimized = vm.Settings.StartMinimized || args.Any(a => a.Equals("--minimized", StringComparison.OrdinalIgnoreCase));

            if (startMinimized)
            {
                MinimizeToTray();
            }

            // Check for updates asynchronously on interface launch (only once, non-blocking)
            _ = Task.Run(UpdateCheckerService.CheckForUpdatesAsync);
        }
    }

    private void CaptureAndSave(string outputPath, double customWidth = 1180, double customHeight = 800)
    {
        try
        {
            double width = customWidth;
            double height = customHeight;

            Width = width;
            Height = height;
            Measure(new System.Windows.Size(width, height));
            Arrange(new System.Windows.Rect(new System.Windows.Size(width, height)));
            UpdateLayout();

            var dv = new System.Windows.Media.DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                var bg = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#0c0e14"));
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

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    public void RestoreAndActivate()
    {
        Show();
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }
        Activate();
        Topmost = true;
        Topmost = false;
        Focus();

        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero)
        {
            SetForegroundWindow(hwnd);
        }
    }

    public void MinimizeToTray()
    {
        _trayManager?.MinimizeToTray();
    }

    public void ExitApplication()
    {
        _isExplicitExit = true;
        Close();
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
