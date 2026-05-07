using System.Windows;
using System.Windows.Threading;
using TitanAILivePC.Services;

namespace TitanAILivePC;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        StartupDiagnostics.Write("OnStartup begin");
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        base.OnStartup(e);
        var licenseService = new LicenseService();
        if (!licenseService.TryValidateStoredLicense(out var licenseError))
        {
            StartupDiagnostics.Write($"License check failed: {licenseError}");
            var licenseWindow = new LicenseActivationWindow(licenseService);
            var ok = licenseWindow.ShowDialog() == true;
            if (!ok)
            {
                StartupDiagnostics.Write("License activation cancelled.");
                Shutdown();
                return;
            }
        }

        var mainWindow = new MainWindow();
        MainWindow = mainWindow;
        mainWindow.Show();
        StartupDiagnostics.Write("OnStartup complete (MainWindow opened)");
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        StartupDiagnostics.Write($"DispatcherUnhandledException: {e.Exception}");
        MessageBox.Show(
            $"Ứng dụng gặp lỗi UI:\n{e.Exception.Message}\n\nChi tiết đã ghi:\n{StartupDiagnostics.LogFilePath}",
            "TITAN AI LIVE",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }

    private void OnUnhandledException(object? sender, UnhandledExceptionEventArgs e)
    {
        StartupDiagnostics.Write($"UnhandledException (IsTerminating={e.IsTerminating}): {e.ExceptionObject}");
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        StartupDiagnostics.Write($"UnobservedTaskException: {e.Exception}");
        e.SetObserved();
    }
}
