using GED.Core.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace GED.Infrastructure.Services;

/// <summary>
/// Plugin registry that manages all GED plugins.
/// Inspired by Mayan EDMS plugin architecture.
/// </summary>
public class PluginRegistry
{
    private readonly Dictionary<string, IGedPlugin> _plugins = new();
    private readonly Dictionary<string, bool> _enabledPlugins = new();
    private readonly ILogger<PluginRegistry> _logger;
    private readonly IConfiguration _configuration;

    public PluginRegistry(ILogger<PluginRegistry> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
    }

    /// <summary>All registered plugins.</summary>
    public IReadOnlyDictionary<string, IGedPlugin> Plugins => _plugins;

    /// <summary>Enabled plugins only.</summary>
    public IEnumerable<IGedPlugin> EnabledPlugins => _plugins
        .Where(kvp => IsEnabled(kvp.Key))
        .Select(kvp => kvp.Value);

    /// <summary>Register a plugin.</summary>
    public void Register(IGedPlugin plugin)
    {
        if (_plugins.ContainsKey(plugin.Id))
        {
            _logger.LogWarning("Plugin {Id} already registered, skipping", plugin.Id);
            return;
        }

        _plugins[plugin.Id] = plugin;
        
        var defaultEnabled = _configuration.GetValue<bool>($"Plugins:{plugin.Id}:Enabled", plugin.IsEnabledByDefault);
        _enabledPlugins[plugin.Id] = defaultEnabled;

        _logger.LogInformation("📦 Registered plugin: {Name} v{Version} ({Category})",
            plugin.Name, plugin.Version, plugin.Category);
    }

    /// <summary>Check if a plugin is enabled.</summary>
    public bool IsEnabled(string pluginId) => _enabledPlugins.GetValueOrDefault(pluginId, false);

    /// <summary>Enable or disable a plugin.</summary>
    public void SetEnabled(string pluginId, bool enabled)
    {
        if (!_plugins.ContainsKey(pluginId))
        {
            _logger.LogWarning("Cannot enable/disable unknown plugin: {Id}", pluginId);
            return;
        }
        _enabledPlugins[pluginId] = enabled;
        _logger.LogInformation("Plugin {Id} {Status}", pluginId, enabled ? "enabled" : "disabled");
    }

    /// <summary>Get all ingestion plugins.</summary>
    public IEnumerable<IDocumentIngestionPlugin> GetIngestionPlugins()
        => EnabledPlugins.OfType<IDocumentIngestionPlugin>().OrderBy(p => p.Priority);

    /// <summary>Get all OCR post-processing plugins.</summary>
    public IEnumerable<IOcrPostProcessingPlugin> GetOcrPostProcessingPlugins()
        => EnabledPlugins.OfType<IOcrPostProcessingPlugin>().OrderBy(p => p.Priority);

    /// <summary>Get all search plugins.</summary>
    public IEnumerable<ISearchPlugin> GetSearchPlugins()
        => EnabledPlugins.OfType<ISearchPlugin>();

    /// <summary>Get all action plugins.</summary>
    public IEnumerable<IDocumentActionPlugin> GetActionPlugins()
        => EnabledPlugins.OfType<IDocumentActionPlugin>();

    /// <summary>Initialize all registered plugins.</summary>
    public async Task InitializeAllAsync()
    {
        _logger.LogInformation("Initializing {Count} plugins...", _plugins.Count);
        foreach (var plugin in _plugins.Values)
        {
            try
            {
                await plugin.InitializeAsync();
                _logger.LogInformation("✅ Initialized plugin: {Id}", plugin.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to initialize plugin: {Id}", plugin.Id);
            }
        }
    }

    /// <summary>Shutdown all plugins.</summary>
    public async Task ShutdownAllAsync()
    {
        _logger.LogInformation("Shutting down {Count} plugins...", _plugins.Count);
        foreach (var plugin in _plugins.Values)
        {
            try
            {
                await plugin.ShutdownAsync();
                _logger.LogInformation("✅ Shutdown plugin: {Id}", plugin.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error shutting down plugin: {Id}", plugin.Id);
            }
        }
    }

    /// <summary>Get plugin by ID.</summary>
    public IGedPlugin? GetPlugin(string pluginId)
        => _plugins.GetValueOrDefault(pluginId);
}
