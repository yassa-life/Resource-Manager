using Microsoft.Win32;
using ResourceManager.Core;
using System.Runtime.Versioning;

namespace ResourceManager.Infrastructure.Windows;

/// <summary>Per-user options that can be installed from the portable app without a service or driver.</summary>
[SupportedOSPlatform("windows")]
public sealed class LocalComponentManager : IComponentManager
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValue = "ResourceManager";
    private const string MarkerName = ".resource-manager-install";
    private static readonly string[] ShellTargets = [@"Software\Classes\*\shell\ResourceManager.Inspect",
        @"Software\Classes\Directory\shell\ResourceManager.Inspect",
        @"Software\Classes\Drive\shell\ResourceManager.Inspect"];
    private static readonly string InstallDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ResourceManager", "App");
    private static readonly string ShortcutPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Resource Manager.lnk");

    public IReadOnlyList<ComponentManifest> GetCatalog() => ComponentCatalog.CreateDefault().Select(item =>
    {
        var state = item.Id switch
        {
            "desktop" => File.Exists(Path.Combine(InstallDirectory, MarkerName)) ? ComponentState.Installed : ComponentState.Available,
            "startup" => ProbeRegistryComponent(RunEntryExists),
            "explorer" => ProbeRegistryComponent(ExplorerEntryExists),
            _ => item.State
        };
        var reason = state != ComponentState.Unavailable ? null : item.Id switch
        {
            "startup" or "explorer" => "Windows denied access to the current-user registry in this environment.",
            "capture" => "No signed, tested WFP capture package is included in this build.",
            "sensor" => "No signed, tested background service is included in this build.",
            "updates" => "No signed update manifest or updater is included in this build.",
            _ => "This component cannot be installed from this build."
        };
        return item with { State = state, UnavailableReason = reason };
    }).ToArray();

    public InstallPlan Preview(string componentId)
    {
        var item = GetCatalog().FirstOrDefault(x => x.Id == componentId)
            ?? throw new ArgumentException("Unknown component", nameof(componentId));
        if (!item.CanInstall) throw new InvalidOperationException($"{item.Name} cannot be installed from this build.");
        return new InstallPlan(componentId, item.SystemChanges, false, false);
    }

    public ValueTask<InstallReceipt> InstallAsync(InstallPlan plan, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var item = GetCatalog().FirstOrDefault(x => x.Id == plan.ComponentId)
            ?? throw new ArgumentException("Unknown component", nameof(plan));
        if (!item.CanInstall) throw new InvalidOperationException($"{item.Name} is already installed or unavailable.");
        switch (plan.ComponentId)
        {
            case "desktop": InstallDesktop(cancellationToken); break;
            case "explorer": InstallExplorer(); break;
            case "startup": InstallStartup(); break;
            default: throw new InvalidOperationException("No verified installer is bundled for this component.");
        }
        return ValueTask.FromResult(new InstallReceipt(plan.ComponentId, item.Version, DateTimeOffset.Now, item.SystemChanges));
    }

    public ValueTask<RollbackResult> UninstallAsync(string componentId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            switch (componentId)
            {
                case "desktop": RemoveDesktop(); break;
                case "explorer": RemoveExplorer(); break;
                case "startup": RemoveStartup(); break;
                default: return ValueTask.FromResult(new RollbackResult(false, [], "This component is not removable from this build."));
            }
            return ValueTask.FromResult(new RollbackResult(true, [componentId], null));
        }
        catch (Exception ex) { return ValueTask.FromResult(new RollbackResult(false, [], ex.Message)); }
    }

    private static void InstallDesktop(CancellationToken token)
    {
        var source = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        if (string.Equals(source, InstallDirectory, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The app is already running from its installed location.");
        Directory.CreateDirectory(InstallDirectory);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.TopDirectoryOnly))
        {
            token.ThrowIfCancellationRequested();
            File.Copy(file, Path.Combine(InstallDirectory, Path.GetFileName(file)), true);
        }
        var executable = Path.Combine(InstallDirectory, "ResourceManager.App.exe");
        if (!File.Exists(executable)) throw new FileNotFoundException("The app executable is missing from the portable folder.");
        CreateShortcut(executable);
        File.WriteAllText(Path.Combine(InstallDirectory, MarkerName), "Resource Manager per-user installation v1");
    }

    private static void RemoveDesktop()
    {
        var marker = Path.Combine(InstallDirectory, MarkerName);
        if (!File.Exists(marker)) throw new InvalidOperationException("No app-owned installation receipt was found.");
        if (AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar)
            .Equals(InstallDirectory, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Open the portable copy to remove the installed copy while it is closed.");
        if (RunEntryExists() || ExplorerEntryExists())
            throw new InvalidOperationException("Remove Start with Windows and Explorer Integration first; they may still point to this copy.");
        if (File.Exists(ShortcutPath)) File.Delete(ShortcutPath);
        Directory.Delete(InstallDirectory, true);
    }

    private static void CreateShortcut(string executable)
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell")
            ?? throw new InvalidOperationException("Windows shortcut support is unavailable.");
        dynamic shell = Activator.CreateInstance(shellType)!;
        dynamic shortcut = shell.CreateShortcut(ShortcutPath);
        shortcut.TargetPath = executable;
        shortcut.WorkingDirectory = InstallDirectory;
        shortcut.Description = "Resource Manager";
        shortcut.Save();
    }

    private static bool RunEntryExists()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(RunValue) is string;
    }

    private static ComponentState ProbeRegistryComponent(Func<bool> probe)
    {
        try { return probe() ? ComponentState.Installed : ComponentState.Available; }
        catch (System.Security.SecurityException) { return ComponentState.Unavailable; }
        catch (UnauthorizedAccessException) { return ComponentState.Unavailable; }
    }

    private static void InstallStartup()
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, true);
        key.SetValue(RunValue, $"\"{CurrentExecutable()}\"");
    }

    private static void RemoveStartup()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, true);
        key?.DeleteValue(RunValue, false);
    }

    private static bool ExplorerEntryExists()
    {
        using var key = Registry.CurrentUser.OpenSubKey(ShellTargets[1] + @"\command");
        return key?.GetValue(null) is string;
    }

    private static void InstallExplorer()
    {
        var command = $"\"{CurrentExecutable()}\" --inspect \"%1\"";
        foreach (var path in ShellTargets)
        {
            using var key = Registry.CurrentUser.CreateSubKey(path, true);
            key.SetValue(null, "Inspect with Resource Manager");
            using var commandKey = key.CreateSubKey("command", true);
            commandKey.SetValue(null, command);
        }
    }

    private static void RemoveExplorer()
    {
        foreach (var path in ShellTargets) Registry.CurrentUser.DeleteSubKeyTree(path, false);
    }

    private static string CurrentExecutable() => Environment.ProcessPath
        ?? throw new InvalidOperationException("The current executable path is unavailable.");
}
