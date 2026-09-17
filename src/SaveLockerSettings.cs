using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Playnite.SDK;

namespace SaveLocker.Playnite
{
    /// <summary>Reads this plugin's own version straight out of its installed <c>extension.yaml</c> —
    /// the exact same file Playnite itself reads to load the plugin and the same one the addon-database
    /// submission and self-update check (<see cref="SaveLockerPlugin"/>'s Phase 14 consumer) key off —
    /// rather than a separately-maintained assembly version that could drift from it.</summary>
    internal static class PluginVersion
    {
        private static readonly ILogger Logger = LogManager.GetLogger();

        public static string Current
        {
            get
            {
                var location = Assembly.GetExecutingAssembly().Location;
                try
                {
                    var dir = Path.GetDirectoryName(location);
                    var path = Path.Combine(dir ?? string.Empty, "extension.yaml");
                    foreach (var line in File.ReadAllLines(path))
                    {
                        if (line.StartsWith("Version:", StringComparison.OrdinalIgnoreCase))
                            return line.Substring("Version:".Length).Trim();
                    }
                    Logger.Warn($"SaveLocker: extension.yaml at '{path}' has no Version: line");
                }
                catch (Exception ex)
                {
                    // Falls through to "unknown" — a missing/unreadable extension.yaml shouldn't
                    // crash the settings page over a display-only value. Location is logged
                    // because how Playnite loads this assembly (Assembly.Load(AssemblyName) via
                    // its own resolver, not a plain LoadFrom) determines whether Location even
                    // points at this plugin's real install folder.
                    Logger.Warn(ex, $"SaveLocker: couldn't read extension.yaml next to '{location}'");
                }
                return "unknown";
            }
        }
    }

    /// <summary>
    /// Persisted via <c>Plugin.LoadPluginSettings/SavePluginSettings</c> — plain data only, no logic.
    /// <see cref="StateDir"/> exists purely for testenv (Group 5/Phase 15): the real installed agent
    /// never needs it touched, but a test agent run with <c>SAVELOCKER_STATE_ROOT</c> writes its
    /// token somewhere other than %ProgramData%, and this plugin has to be pointed at the same place.
    /// </summary>
    internal sealed class SaveLockerSettings
    {
        public string AgentUrl { get; set; } = "http://127.0.0.1:5178";
        public string StateDir { get; set; } = Environment.ExpandEnvironmentVariables(@"%ProgramData%\SaveLocker");
    }

    internal sealed class SaveLockerSettingsViewModel : PropertyChangedBase, ISettings
    {
        private static readonly ILogger Logger = LogManager.GetLogger();

        private readonly SaveLockerPlugin plugin;
        private readonly LocalApiClient client;
        private SaveLockerSettings editingClone;

        private SaveLockerSettings settings;
        public SaveLockerSettings Settings
        {
            get { return settings; }
            set { SetAndNotify(ref settings, value); }
        }

        private string connectionStatus = "Not checked yet";
        public string ConnectionStatus
        {
            get { return connectionStatus; }
            private set { SetAndNotify(ref connectionStatus, value); }
        }

        public string PluginVersionDisplay => "v" + PluginVersion.Current;

        public SaveLockerSettingsViewModel(SaveLockerPlugin plugin)
        {
            this.plugin = plugin;
            client = new LocalApiClient(() => Settings);

            var saved = plugin.LoadPluginSettings<SaveLockerSettings>();
            Settings = saved ?? new SaveLockerSettings();
        }

        public void BeginEdit()
        {
            editingClone = new SaveLockerSettings { AgentUrl = Settings.AgentUrl, StateDir = Settings.StateDir };
        }

        public void CancelEdit()
        {
            Settings = editingClone;
        }

        public void EndEdit()
        {
            plugin.SavePluginSettings(Settings);
        }

        public bool VerifySettings(out List<string> errors)
        {
            errors = new List<string>();
            Uri parsed;
            if (string.IsNullOrWhiteSpace(Settings.AgentUrl) || !Uri.TryCreate(Settings.AgentUrl, UriKind.Absolute, out parsed))
                errors.Add("Agent URL must be a full URL, e.g. http://127.0.0.1:5178");
            if (string.IsNullOrWhiteSpace(Settings.StateDir))
                errors.Add("State directory can't be empty.");
            return errors.Count == 0;
        }

        /// <summary>Fire-and-forget from the settings view's "Test connection" button — never blocks
        /// the UI thread, since an unreachable agent (the common case while setting this up) would
        /// otherwise hang the whole settings dialog on the HttpClient timeout.</summary>
        public void RefreshConnectionStatus()
        {
            ConnectionStatus = "Checking…";
            Task.Run(async () =>
            {
                try
                {
                    var state = await client.GetStateAsync().ConfigureAwait(false);
                    ConnectionStatus = state.Connected
                        ? $"Connected — {state.MachineName}, {state.GamesTracked} game(s) tracked, agent {state.CurrentVersion}"
                        : "Agent reachable, but not registered against a server yet.";
                }
                catch (Exception ex)
                {
                    Logger.Warn(ex, "SaveLocker: connection check failed");
                    ConnectionStatus = "Can't reach the agent at " + Settings.AgentUrl + " — is it running?";
                }
            });
        }
    }
}
