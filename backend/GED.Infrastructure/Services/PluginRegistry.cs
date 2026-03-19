using GED.Core.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace GED.Infrastructure.Services;

/// <summary>
/// Plugin registry that manages all GED (Electronic Document Management) plugins.
/// 
/// <para>
/// Inspired by Mayan EDMS plugin architecture, this registry provides:
/// <list type="bullet">
///   <item>
///     <term>Plugin discovery</term>
///     <description>
///       Plugins register themselves with a unique ID, name, version, and category.
///     </description>
///   </item>
///   <item>
///     <term>Enable/disable</term>
///     <description>
///       Plugins can be enabled or disabled at runtime via configuration or API.
///     </description>
///   </item>
///   <item>
///     <term>Categorization</term>
///     <description>
///       Plugins are grouped by type: Ingestion, OCR Post-processing, Search, Actions.
///     </description>
///   </item>
///   <item>
///     <term>Lifecycle management</term>
///     <description>
///       Supports InitializeAsync and ShutdownAsync for graceful startup/shutdown.
///     </description>
///   </item>
/// </list>
/// </para>
/// 
/// <para>
/// Plugin categories:
/// <list type="bullet">
///   <item>
///     <term>Ingestion</term>
///     <description>
///       Process documents during upload (custom extraction, transformation).
///     </description>
///   </item>
///   <item>
///     <term>OCR Post-processing</term>
///     <description>
///       Process OCR results (language-specific cleanup, specialized cleaners).
///     </description>
///   </item>
///   <item>
///     <term>Search</term>
///     <description>
///       Extend search capabilities (custom ranking, filters).
///     </description>
///   </item>
///   <item>
///     <term>Actions</term>
///     <description>
///       Custom actions on documents (export, notification, webhook).
///     </description>
///   </item>
/// </list>
/// </para>
/// </summary>
public class PluginRegistry
{
    /// <summary>
    /// All registered plugins keyed by plugin ID.
    /// </summary>
    private readonly Dictionary<string, IGedPlugin> _plugins = new();

    /// <summary>
    /// Enabled state for each plugin.
    /// </summary>
    private readonly Dictionary<string, bool> _enabledPlugins = new();

    private readonly ILogger<PluginRegistry> _logger;

    /// <summary>
    /// Application configuration for plugin settings.
    /// </summary>
    private readonly IConfiguration _configuration;

    /// <summary>
    /// Initializes a new instance of <see cref="PluginRegistry"/>.
    /// </summary>
    /// <param name="logger">Logger for plugin events.</param>
    /// <param name="configuration">Application configuration.</param>
    public PluginRegistry(ILogger<PluginRegistry> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
    }

    /// <summary>
    /// Gets all registered plugins.
    /// </summary>
    public IReadOnlyDictionary<string, IGedPlugin> Plugins => _plugins;

    /// <summary>
    /// Gets only enabled plugins.
    /// </summary>
    public IEnumerable<IGedPlugin> EnabledPlugins => _plugins
        .Where(kvp => IsEnabled(kvp.Key))
        .Select(kvp => kvp.Value);

    /// <summary>
    /// Registers a plugin with the registry.
    /// </summary>
    /// <param name="plugin">The plugin to register.</param>
    /// <remarks>
    /// If a plugin with the same ID is already registered, it will be skipped.
    /// Plugin enabled state is determined by configuration: Plugins:{pluginId}:Enabled
    /// Falls back to plugin's IsEnabledByDefault if not configured.
    /// </remarks>
    public void Register(IGedPlugin plugin)
    {
        if (_plugins.ContainsKey(plugin.Id))
        {
            _logger.LogWarning("Plugin {Id} already registered, skipping", plugin.Id);
            return;
        }

        _plugins[plugin.Id] = plugin;
        
        // Check configuration for plugin enabled state
        var defaultEnabled = _configuration.GetValue<bool>($"Plugins:{plugin.Id}:Enabled", plugin.IsEnabledByDefault);
        _enabledPlugins[plugin.Id] = defaultEnabled;

        _logger.LogInformation("📦 Registered plugin: {Name} v{Version} ({Category})",
            plugin.Name, plugin.Version, plugin.Category);
    }

    /// <summary>
    /// Checks if a plugin is currently enabled.
    /// </summary>
    /// <param name="pluginId">Plugin identifier.</param>
    /// <returns>True if enabled, false otherwise.</returns>
    public bool IsEnabled(string pluginId) => _enabledPlugins.GetValueOrDefault(pluginId, false);

    /// <summary>
    /// Enables or disables a plugin.
    /// </summary>
    /// <param name="pluginId">Plugin identifier.</param>
    /// <param name="enabled">Whether to enable or disable the plugin.</param>
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

    /// <summary>
    /// Gets all ingestion plugins.
    /// </summary>
    /// <returns>Enabled ingestion plugins ordered by priority.</returns>
    public IEnumerable<IDocumentIngestionPlugin> GetIngestionPlugins()
        => EnabledPlugins.OfType<IDocumentIngestionPlugin>().OrderBy(p => p.Priority);

    /// <summary>
    /// Gets all OCR post-processing plugins.
    /// </summary>
    /// <returns>Enabled OCR post-processing plugins ordered by priority.</returns>
    public IEnumerable<IOcrPostProcessingPlugin> GetOcrPostProcessingPlugins()
        => EnabledPlugins.OfType<IOcrPostProcessingPlugin>().OrderBy(p => p.Priority);

    /// <summary>
    /// Gets all search plugins.
    /// </summary>
    /// <returns>Enabled search plugins.</returns>
    public IEnumerable<ISearchPlugin> GetSearchPlugins()
        => EnabledPlugins.OfType<ISearchPlugin>();

    /// <summary>
    /// Gets all document action plugins.
    /// </summary>
    /// <returns>Enabled document action plugins.</returns>
    public IEnumerable<IDocumentActionPlugin> GetActionPlugins()
        => EnabledPlugins.OfType<IDocumentActionPlugin>();

    /// <summary>
    /// Initializes all registered plugins.
    /// </summary>
    /// <remarks>
    /// Calls InitializeAsync on each plugin. Errors are logged but don't stop
    /// initialization of other plugins.
    /// </remarks>
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

    /// <summary>
    /// Shuts down all registered plugins gracefully.
    /// </summary>
    /// <remarks>
    /// Calls ShutdownAsync on each plugin. Errors are logged but don't prevent
    /// shutdown of other plugins.
    /// </remarks>
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

    /// <summary>
    /// Gets a plugin by its ID.
    /// </summary>
    /// <param name="pluginId">Plugin identifier.</param>
    /// <returns>The plugin if found, null otherwise.</returns>
    public IGedPlugin? GetPlugin(string pluginId)
        => _plugins.GetValueOrDefault(pluginId);
}
