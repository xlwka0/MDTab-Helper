using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;

namespace MDHelper;

public partial class App : Application
{
    private static Mutex? _instanceMutex;
    private const string MutexName = @"Local\MDTab-Helper-SingleInstance-Mutex";

    public static void ReleaseInstanceMutex()
    {
        if (_instanceMutex != null)
        {
            try
            {
                _instanceMutex.ReleaseMutex();
                _instanceMutex.Dispose();
            }
            catch { }
            _instanceMutex = null;
        }
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        bool isUpdateRestart = false;
        foreach (var arg in e.Args)
        {
            if (arg.Equals("--updated", StringComparison.OrdinalIgnoreCase))
            {
                isUpdateRestart = true;
                break;
            }
        }

        bool createdNew = false;
        try
        {
            _instanceMutex = new Mutex(true, MutexName, out createdNew);
            if (!createdNew && isUpdateRestart)
            {
                // Give previous instance up to 5 seconds to fully release
                createdNew = _instanceMutex.WaitOne(5000);
            }
        }
        catch (AbandonedMutexException)
        {
            createdNew = true;
        }

        if (!createdNew)
        {
            // Another instance is already running: silently exit immediately
            Shutdown();
            return;
        }

        // 1. UI Thread Exceptions (WPF UI Thread)
        DispatcherUnhandledException += App_DispatcherUnhandledException;

        // 2. Non-UI Thread Exceptions (AppDomain background threads)
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;

        // 3. Task Exceptions (Unobserved async Tasks)
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;

        // 4. Windows Shutdown / Logoff (Ensures all configuration is saved before OS terminates process)
        SessionEnding += (s, args) =>
        {
            if (MainWindow is MDHelper.Windows.MainWindow mw)
            {
                mw.PrepareForExit();
            }
        };

        base.OnStartup(e);

        MDHelper.Services.UpdateService.CleanupOldUpdateFiles();

        ShutdownMode = ShutdownMode.OnMainWindowClose;

        // Instantiate MainWindow silently in background (does not call Show)
        var mainWindow = new MDHelper.Windows.MainWindow();
        MainWindow = mainWindow;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        ReleaseInstanceMutex();
        base.OnExit(e);
    }

    private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        LogException(e.Exception, "UI Thread");
        e.Handled = true; // CRITICAL: Prevents WPF from crashing/exiting!
        MessageBox.Show($"O eroare neprevăzută a apărut pe thread-ul principal. Aplicația va încerca să își revină.\n\nDetalii: {e.Exception.Message}", "Eroare MDTab-Helper", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            LogException(ex, "AppDomain");
        }
    }

    private void TaskScheduler_UnobservedTaskException(object? sender, System.Threading.Tasks.UnobservedTaskExceptionEventArgs e)
    {
        LogException(e.Exception, "TaskScheduler");
        e.SetObserved(); // CRITICAL: Marks task exceptions as observed to prevent crash
    }

    private void LogException(Exception ex, string source)
    {
        try
        {
            string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crashlog.txt");
            string logText = $"[{DateTime.Now}] Source: {source}\nMessage: {ex.Message}\nStackTrace:\n{ex.StackTrace}\n\nInnerException: {ex.InnerException?.Message}\n----------------------------------------\n";
            File.AppendAllText(logPath, logText);
        }
        catch
        {
            // Ignore if writing to disk is blocked
        }
    }
}
