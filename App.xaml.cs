namespace MicCompressor;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : System.Windows.Application
{
    private const string MutexName = "SimpleMicCompressor_SingleInstance_Mutex_2026";
    private const string ShowEventName = "SimpleMicCompressor_ShowEvent_2026";

    private static System.Threading.Mutex? _singleInstanceMutex;
    private static System.Threading.EventWaitHandle? _showEvent;
    private static System.Threading.RegisteredWaitHandle? _registeredWaitHandle;

    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        bool isScreenshot = e.Args.Any(a => a.StartsWith("--screenshot", StringComparison.OrdinalIgnoreCase));
        if (!isScreenshot)
        {
            _singleInstanceMutex = new System.Threading.Mutex(true, MutexName, out bool isNewInstance);

            if (!isNewInstance)
            {
                // Another instance is already running!
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
        }

        base.OnStartup(e);
    }

    protected override void OnExit(System.Windows.ExitEventArgs e)
    {
        try
        {
            _registeredWaitHandle?.Unregister(null);
            _showEvent?.Dispose();
            _singleInstanceMutex?.ReleaseMutex();
            _singleInstanceMutex?.Dispose();
        }
        catch { }
        base.OnExit(e);
    }
}
