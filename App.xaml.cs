namespace MicCompressor;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : System.Windows.Application
{
    private const string MutexName = "SimpleMicCompressor_SingleInstance_Mutex_2026";
    private const string ShowEventName = "SimpleMicCompressor_ShowEvent_2026";
    private const string ExitEventName = "SimpleMicCompressor_ExitEvent_2026";

    private static System.Threading.Mutex? _singleInstanceMutex;
    private static System.Threading.EventWaitHandle? _showEvent;
    private static System.Threading.RegisteredWaitHandle? _registeredWaitHandle;
    private static System.Threading.EventWaitHandle? _exitEvent;
    private static System.Threading.RegisteredWaitHandle? _exitRegisteredWaitHandle;

    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        bool isScreenshot = e.Args.Any(a => a.StartsWith("--screenshot", StringComparison.OrdinalIgnoreCase));
        if (!isScreenshot)
        {
            _singleInstanceMutex = new System.Threading.Mutex(true, MutexName, out bool isNewInstance);

            if (!isNewInstance && TryReplaceOlderRunningInstance())
            {
                _singleInstanceMutex.Dispose();
                _singleInstanceMutex = null;

                for (int i = 0; i < 10; i++)
                {
                    _singleInstanceMutex = new System.Threading.Mutex(true, MutexName, out isNewInstance);
                    if (isNewInstance) break;
                    _singleInstanceMutex.Dispose();
                    _singleInstanceMutex = null;
                    System.Threading.Thread.Sleep(50);
                }
            }

            if (!isNewInstance)
            {
                // Another instance (same or newer version) is already running!
                bool isMinimized = e.Args.Any(a => a.Equals("--minimized", StringComparison.OrdinalIgnoreCase));
                if (!isMinimized)
                {
                    // Signal the running instance to restore and show its window
                    try
                    {
                        if (System.Threading.EventWaitHandle.TryOpenExisting(ShowEventName, out var existingEvent))
                        {
                            existingEvent.Set();
                            existingEvent.Dispose();
                        }
                    }
                    catch { }
                }

                Shutdown();
                return;
            }

            Utils.AutoStartHelper.RefreshAutoStartPathIfEnabled();

            // Create event handle for receiving show/restore signals from subsequent instances
            try
            {
                _showEvent = new System.Threading.EventWaitHandle(false, System.Threading.EventResetMode.AutoReset, ShowEventName);
                _registeredWaitHandle = System.Threading.ThreadPool.RegisterWaitForSingleObject(_showEvent, (state, timedOut) =>
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (MainWindow is MainWindow mw)
                        {
                            mw.RestoreAndActivate();
                        }
                    }));
                }, null, -1, false);
            }
            catch { }

            // Create event handle so future newer versions can ask this instance to exit gracefully
            try
            {
                _exitEvent = new System.Threading.EventWaitHandle(false, System.Threading.EventResetMode.AutoReset, ExitEventName);
                _exitRegisteredWaitHandle = System.Threading.ThreadPool.RegisterWaitForSingleObject(_exitEvent, (state, timedOut) =>
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (MainWindow is MainWindow mw)
                        {
                            mw.ExitApplication();
                        }
                        else
                        {
                            Shutdown();
                        }
                    }));
                }, null, -1, false);
            }
            catch { }
        }

        base.OnStartup(e);
    }

    private static bool TryReplaceOlderRunningInstance()
    {
        var currentVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0, 0);
        int currentPid = Environment.ProcessId;
        bool replacedAny = false;

        var candidates = new List<System.Diagnostics.Process>();
        try
        {
            candidates.AddRange(System.Diagnostics.Process.GetProcessesByName("SimpleMicCompressor"));
            candidates.AddRange(System.Diagnostics.Process.GetProcessesByName("MicCompressor"));

            foreach (var proc in candidates)
            {
                try
                {
                    if (proc.Id == currentPid || proc.HasExited) continue;

                    string? exePath = proc.MainModule?.FileName;
                    if (string.IsNullOrEmpty(exePath)) continue;

                    var fvi = System.Diagnostics.FileVersionInfo.GetVersionInfo(exePath);
                    var runningVersion = new Version(fvi.FileMajorPart, fvi.FileMinorPart, fvi.FileBuildPart, fvi.FilePrivatePart);

                    if (runningVersion < currentVersion)
                    {
                        try
                        {
                            if (System.Threading.EventWaitHandle.TryOpenExisting(ExitEventName, out var exitEv))
                            {
                                exitEv.Set();
                                exitEv.Dispose();
                            }
                        }
                        catch { }

                        if (!proc.WaitForExit(800))
                        {
                            try
                            {
                                proc.Kill();
                                proc.WaitForExit(2000);
                            }
                            catch { }
                        }

                        replacedAny = true;
                    }
                }
                catch { }
            }
        }
        catch { }
        finally
        {
            foreach (var proc in candidates)
            {
                try { proc.Dispose(); } catch { }
            }
        }

        return replacedAny;
    }

    protected override void OnExit(System.Windows.ExitEventArgs e)
    {
        try
        {
            _exitRegisteredWaitHandle?.Unregister(null);
            _exitEvent?.Dispose();
            _registeredWaitHandle?.Unregister(null);
            _showEvent?.Dispose();
            _singleInstanceMutex?.ReleaseMutex();
            _singleInstanceMutex?.Dispose();
        }
        catch { }
        base.OnExit(e);
    }
}
