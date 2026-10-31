namespace ResourceManager.Core;

public static class ComponentCatalog
{
    public static IReadOnlyList<ComponentManifest> CreateDefault() =>
    [
        new("desktop", "Desktop app for this user", "1.0.0", "Resource Manager", "Development build", 0, false, false, false,
            ["Stable app folder in your user profile", "Start menu shortcut", "Portable data stays in the original folder"],
            ["Copies application files to LocalAppData\\ResourceManager\\App", "Creates one Start menu shortcut", "Can be removed in this Capabilities Center"], ComponentState.Available),
        new("capture", "Enhanced network capture", "Planned", "Resource Manager", "Not bundled", 0, true, false, true,
            ["Short-lived flows", "Loopback visibility", "Packet direction and drop metadata"],
            ["Would install a signed WFP driver", "Would register a driver service", "No TLS interception"], ComponentState.Unavailable),
        new("sensor", "Background monitoring service", "Planned", "Resource Manager", "Not bundled", 0, true, false, true,
            ["Monitoring while the window is closed", "Continuous alerts", "Earlier connection attribution"],
            ["Would install a Windows service", "Would write selected events to local history"], ComponentState.Unavailable),
        new("explorer", "Explorer integration", "1.0.0", "Resource Manager", "Development build", 0, false, false, false,
            ["Inspect files, folders, and drives from Explorer"],
            ["Adds a current-user Inspect with Resource Manager context command", "No administrator access required"], ComponentState.Available),
        new("startup", "Start with Windows", "1.0.0", "Resource Manager", "Development build", 0, false, false, true,
            ["Open the app when you sign in"], ["Creates a current-user startup entry"], ComponentState.Available),
        new("updates", "Automatic updates", "Planned", "Resource Manager", "Not bundled", 0, true, true, false,
            ["Signed app and component updates", "Rollback support"],
            ["Would install a signed update helper", "Would connect to the configured update endpoint"], ComponentState.Unavailable),
        new("security-models", "Local security rules", "1.0.0", "Resource Manager", "Bundled", 0, false, false, false,
            ["Explainable local rules", "No cloud lookups or file uploads"],
            ["Included with the app", "Does not install a separate model pack"], ComponentState.Installed)
    ];
}
