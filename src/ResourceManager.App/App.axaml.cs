using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using ResourceManager.Core;
using ResourceManager.Infrastructure.Windows;

namespace ResourceManager.App;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("This development build supports Windows only.");
            var dataDirectory = Path.Combine(AppContext.BaseDirectory, "Data");
            var history = new SqliteHistoryRepository(Path.Combine(dataDirectory, "history.db"));
            var serviceManager = new WindowsServiceManager();
            var viewModel = new MainWindowViewModel(
                new WindowsResourceCollector(), new WindowsProcessCollector(), new WindowsNetworkCollector(),
                serviceManager, serviceManager,
                new WindowsDiskScanner(), new WindowsFolderRelationshipScanner(), new LocalThreatAnalyzer(), history,
                new WindowsProcessController(), new WindowsProcessDetailInspector(), new LocalComponentManager());
            var inspectPath = desktop.Args is ["--inspect", var path, ..] ? path : null;
            desktop.MainWindow = new MainWindow { DataContext = viewModel, InitialInspectPath = inspectPath };
            desktop.Exit += async (_, _) => await viewModel.DisposeAsync();
        }
        base.OnFrameworkInitializationCompleted();
    }
}
