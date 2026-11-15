namespace MicCompressor;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : System.Windows.Application
{
    private static System.Threading.Mutex? _singleInstanceMutex;

    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        bool isScreenshot = e.Args.Any(a => a.StartsWith("--screenshot", StringComparison.OrdinalIgnoreCase));
        if (!isScreenshot)
        {
            const string mutexName = "SimpleMicCompressor_SingleInstance_Mutex_2026";
            _singleInstanceMutex = new System.Threading.Mutex(true, mutexName, out bool isNewInstance);

            if (!isNewInstance)
            {
                // Another instance is already running!
                Shutdown();
                return;
            }
        }

        base.OnStartup(e);
    }

    protected override void OnExit(System.Windows.ExitEventArgs e)
    {
        try
        {
            _singleInstanceMutex?.ReleaseMutex();
            _singleInstanceMutex?.Dispose();
        }
        catch { }
        base.OnExit(e);
    }
}
