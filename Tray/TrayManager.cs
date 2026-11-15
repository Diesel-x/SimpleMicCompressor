using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows;
using MicCompressor.ViewModels;

namespace MicCompressor.Tray;

public sealed class TrayManager : IDisposable
{
    private readonly Window _window;
    private readonly MainViewModel _viewModel;
    private System.Windows.Forms.NotifyIcon? _notifyIcon;
    private Icon? _customIcon;

    public TrayManager(Window window, MainViewModel viewModel)
    {
        _window = window;
        _viewModel = viewModel;
        InitializeTray();
    }

    private void InitializeTray()
    {
        if (System.IO.File.Exists("assets/app.ico"))
        {
            try { _customIcon = new Icon("assets/app.ico"); } catch { _customIcon = CreateMicIcon(); }
        }
        else
        {
            _customIcon = CreateMicIcon();
        }

        var contextMenu = new System.Windows.Forms.ContextMenuStrip();

        var openItem = new System.Windows.Forms.ToolStripMenuItem("Открыть SimpleMicCompressor", null, (s, e) => ShowWindow());
        openItem.Font = new Font(openItem.Font, System.Drawing.FontStyle.Bold);
        contextMenu.Items.Add(openItem);

        contextMenu.Items.Add(new System.Windows.Forms.ToolStripSeparator());

        var bypassItem = new System.Windows.Forms.ToolStripMenuItem(Localization.LocalizationService.Instance["Tray_Bypass"], null, (s, e) =>
        {
            _viewModel.BypassAll = !_viewModel.BypassAll;
        });
        contextMenu.Items.Add(bypassItem);

        var toggleEngineItem = new System.Windows.Forms.ToolStripMenuItem(Localization.LocalizationService.Instance["Tray_ToggleEngine"], null, (s, e) =>
        {
            _viewModel.ToggleEngine();
        });
        contextMenu.Items.Add(toggleEngineItem);

        contextMenu.Items.Add(new System.Windows.Forms.ToolStripSeparator());

        var exitItem = new System.Windows.Forms.ToolStripMenuItem(Localization.LocalizationService.Instance["Tray_Exit"], null, (s, e) =>
        {
            _notifyIcon!.Visible = false;
            System.Windows.Application.Current.Shutdown();
        });
        contextMenu.Items.Add(exitItem);

        contextMenu.Opening += (s, e) =>
        {
            openItem.Text = Localization.LocalizationService.Instance["Tray_Open"];
            bypassItem.Text = Localization.LocalizationService.Instance["Tray_Bypass"];
            bypassItem.Checked = _viewModel.BypassAll;
            toggleEngineItem.Text = Localization.LocalizationService.Instance["Tray_ToggleEngine"];
            exitItem.Text = Localization.LocalizationService.Instance["Tray_Exit"];
        };

        _notifyIcon = new System.Windows.Forms.NotifyIcon
        {
            Icon = _customIcon,
            Text = "SimpleMicCompressor — Компрессор микрофона",
            Visible = true,
            ContextMenuStrip = contextMenu
        };

        _notifyIcon.DoubleClick += (s, e) => ShowWindow();
        _notifyIcon.Click += (s, e) =>
        {
            if (e is System.Windows.Forms.MouseEventArgs me && me.Button == System.Windows.Forms.MouseButtons.Left)
            {
                ShowWindow();
            }
        };
    }

    public void ShowWindow()
    {
        _window.Show();
        if (_window.WindowState == WindowState.Minimized)
        {
            _window.WindowState = WindowState.Normal;
        }
        _window.Activate();
    }

    [System.Runtime.InteropServices.DllImport("psapi.dll")]
    private static extern int EmptyWorkingSet(IntPtr hwProc);

    public void MinimizeToTray()
    {
        _window.Hide();
        _notifyIcon?.ShowBalloonTip(1800, "SimpleMicCompressor", "Программа активна в трее. Обработка звука включена.", System.Windows.Forms.ToolTipIcon.Info);
        try
        {
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true, true);
            GC.WaitForPendingFinalizers();
            EmptyWorkingSet(System.Diagnostics.Process.GetCurrentProcess().Handle);
        }
        catch { }
    }

    private static Icon CreateMicIcon()
    {
        try
        {
            using var bmp = new Bitmap(32, 32);
            using var g = Graphics.FromImage(bmp);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            // Background circle
            using var bgBrush = new SolidBrush(System.Drawing.Color.FromArgb(88, 101, 242)); // Discord Blurple
            g.FillEllipse(bgBrush, 2, 2, 28, 28);

            // Microphone body
            using var micBrush = new SolidBrush(System.Drawing.Color.White);
            g.FillRoundedRectangle(micBrush, 12, 6, 8, 13, 4);

            // Arc / cradle
            using var pen = new System.Drawing.Pen(System.Drawing.Color.White, 2f);
            g.DrawArc(pen, 9, 10, 14, 11, 0, 180);

            // Stand
            g.DrawLine(pen, 16, 21, 16, 25);
            g.DrawLine(pen, 12, 25, 20, 25);

            IntPtr hIcon = bmp.GetHicon();
            return Icon.FromHandle(hIcon);
        }
        catch
        {
            return SystemIcons.Application;
        }
    }

    public void Dispose()
    {
        if (_notifyIcon != null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _notifyIcon = null;
        }
        _customIcon?.Dispose();
    }
}

internal static class GraphicsExtensions
{
    public static void FillRoundedRectangle(this Graphics g, Brush brush, int x, int y, int width, int height, int radius)
    {
        using var path = new GraphicsPath();
        int r2 = radius * 2;
        path.AddArc(x, y, r2, r2, 180, 90);
        path.AddArc(x + width - r2, y, r2, r2, 270, 90);
        path.AddArc(x + width - r2, y + height - r2, r2, r2, 0, 90);
        path.AddArc(x, y + height - r2, r2, r2, 90, 90);
        path.CloseFigure();
        g.FillPath(brush, path);
    }
}
