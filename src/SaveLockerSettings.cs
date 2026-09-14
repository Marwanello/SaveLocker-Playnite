using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Playnite.SDK;

namespace SaveLocker.Playnite
{
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
