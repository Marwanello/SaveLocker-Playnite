using System;
using System.Collections.Generic;

namespace SaveLocker.Playnite
{
    /// <summary>
    /// Mirrors the agent's local-API DTOs (SaveLocker/src/Agent.Core/AgentApiServer.cs,
    /// SaveLocker/src/Agent.Core/SyncEngine.cs, SaveLocker/src/Shared/Contracts.cs) closely enough to
    /// read their JSON — not a generated client, so keep this in sync by hand if those shapes change.
    /// </summary>
    internal sealed class TrackedGameDto
    {
        public Guid Id;
        public string Name;
        public string Path;
        public List<string> ProcessNames;
        public string Alias;
        public uint? SteamAppId;
        public bool? PullBeforeLaunchEnabled;
        public bool? HasSteamCloud;
        public bool? PushAfterExitEnabled;
        public string InstallDir;

        public static TrackedGameDto FromJson(IDictionary<string, object> o)
        {
            return new TrackedGameDto
            {
                Id = Json.GetGuid(o, "id"),
                Name = Json.GetString(o, "name"),
                Path = Json.GetString(o, "path"),
                ProcessNames = Json.GetStringArray(o, "processNames"),
                Alias = Json.GetString(o, "alias"),
                SteamAppId = Json.GetNullableUInt(o, "steamAppId"),
                PullBeforeLaunchEnabled = Json.GetNullableBool(o, "pullBeforeLaunchEnabled"),
                HasSteamCloud = Json.GetNullableBool(o, "hasSteamCloud"),
                PushAfterExitEnabled = Json.GetNullableBool(o, "pushAfterExitEnabled"),
                InstallDir = Json.GetString(o, "installDir"),
            };
        }
    }

    /// <summary>LaunchDecision, SaveLocker/src/Agent.Core/SyncEngine.cs — only "Blocked" may stop a launch.</summary>
    internal enum LaunchDecision
    {
        Proceed,
        ProceedSyncPaused,
        Blocked,
    }

    internal sealed class LaunchGateResult
    {
        public LaunchDecision Decision;
        public string Reason;
        public Guid? ConflictId;
        public string HolderMachineName;

        public static LaunchGateResult FromJson(IDictionary<string, object> o)
        {
            var decisionText = Json.GetString(o, "decision") ?? "Proceed";
            LaunchDecision decision;
            switch (decisionText)
            {
                case "ProceedSyncPaused": decision = LaunchDecision.ProceedSyncPaused; break;
                case "Blocked": decision = LaunchDecision.Blocked; break;
                default: decision = LaunchDecision.Proceed; break;
            }

            return new LaunchGateResult
            {
                Decision = decision,
                Reason = Json.GetString(o, "reason"),
                ConflictId = Json.GetNullableGuid(o, "conflictId"),
                HolderMachineName = Json.GetString(o, "holderMachineName"),
            };
        }

        public static readonly LaunchGateResult ProceedFallback = new LaunchGateResult { Decision = LaunchDecision.Proceed };
    }

    internal sealed class AgentStateDto
    {
        public bool Connected;
        public string CurrentVersion;
        public string MachineName;
        public string ServerUrl;
        public int GamesTracked;
        public Guid? MachineId;

        public static AgentStateDto FromJson(IDictionary<string, object> o)
        {
            return new AgentStateDto
            {
                Connected = Json.GetBool(o, "connected"),
                CurrentVersion = Json.GetString(o, "currentVersion"),
                MachineName = Json.GetString(o, "machineName"),
                ServerUrl = Json.GetString(o, "serverUrl"),
                GamesTracked = Json.GetInt(o, "gamesTracked"),
                MachineId = Json.GetNullableGuid(o, "machineId"),
            };
        }
    }

    /// <summary>SaveLocker/src/Shared/Contracts.cs's <c>SyncStatusDto</c> — "is my local save the same
    /// as the cloud's, without downloading it," the cheap check the status chip polls
    /// (tasks/playnite-plugin/plan.md, "Game page" section).</summary>
    internal sealed class SyncStatusDto
    {
        public bool InSync;
        public bool HasOpenConflict;
        public Guid? ConflictId;

        public static SyncStatusDto FromJson(IDictionary<string, object> o)
        {
            return new SyncStatusDto
            {
                InSync = Json.GetBool(o, "inSync"),
                HasOpenConflict = Json.GetBool(o, "hasOpenConflict"),
                ConflictId = Json.GetNullableGuid(o, "conflictId"),
            };
        }
    }

    internal sealed class ConflictDto
    {
        public Guid Id;
        public Guid GameId;
        public Guid VersionAId;
        public Guid VersionBId;

        public static ConflictDto FromJson(IDictionary<string, object> o)
        {
            return new ConflictDto
            {
                Id = Json.GetGuid(o, "id"),
                GameId = Json.GetGuid(o, "gameId"),
                VersionAId = Json.GetGuid(o, "versionAId"),
                VersionBId = Json.GetGuid(o, "versionBId"),
            };
        }
    }

    internal sealed class SaveVersionDto
    {
        public Guid Id;
        public string MachineName;
        public DateTime CreatedAt;
        public long Size;

        public static SaveVersionDto FromJson(IDictionary<string, object> o)
        {
            return new SaveVersionDto
            {
                Id = Json.GetGuid(o, "id"),
                MachineName = Json.GetString(o, "machineName"),
                CreatedAt = Json.GetNullableDateTime(o, "createdAt") ?? DateTime.MinValue,
                Size = Json.GetLong(o, "size"),
            };
        }
    }

    internal sealed class VersionStatsDto
    {
        public int FileCount;
        public DateTime? NewestFileWriteUtc;

        public static VersionStatsDto FromJson(IDictionary<string, object> o)
        {
            return new VersionStatsDto
            {
                FileCount = Json.GetInt(o, "fileCount"),
                NewestFileWriteUtc = Json.GetNullableDateTime(o, "newestFileWriteUtc"),
            };
        }
    }

    internal sealed class CandidateLookupResult
    {
        public int Id;
        public bool Resolved;
        public string SuggestedPath;
        /// <summary>The candidate's own name — for a manifest-driven lookup this is the manifest's
        /// canonical spelling, not necessarily Playnite's own title (Enroller.EnrollAsync always
        /// prefers ManifestKey over the discovered name, tasks/playnite-plugin/plan.md's "Manifest
        /// search" section).</summary>
        public string CandidateName;

        public static CandidateLookupResult FromJson(IDictionary<string, object> o)
        {
            var candidate = Json.AsObject(o != null && o.TryGetValue("candidate", out var c) ? c : null);
            return new CandidateLookupResult
            {
                Id = Json.GetInt(o, "id"),
                Resolved = Json.GetBool(o, "resolved"),
                SuggestedPath = candidate != null ? Json.GetString(candidate, "path") : null,
                CandidateName = candidate != null ? Json.GetString(candidate, "name") : null,
            };
        }
    }

    internal sealed class EnrollResult
    {
        public int Enrolled;
        public int Skipped;

        public static EnrollResult FromJson(IDictionary<string, object> o)
        {
            return new EnrollResult
            {
                Enrolled = Json.GetInt(o, "enrolled"),
                Skipped = Json.GetInt(o, "skipped"),
            };
        }
    }

    /// <summary>SaveLocker/src/Agent.Core/AgentApiServer.cs's <c>PlaynitePluginStatusDto</c> — GET
    /// /api/playnite-plugin's answer to "is a newer version of me waiting on the server"
    /// (tasks/playnite-plugin/plan.md, Phase 14). <see cref="State"/> mirrors
    /// <c>Agent.PlaynitePluginState</c>'s name as a plain string (NoPlaynite/NotInstalled/UpToDate/
    /// Available/Failed/NotApplicable) — only "Available" matters to this plugin; the others are
    /// either not-applicable-to-a-running-instance-of-itself or nothing to act on.</summary>
    internal sealed class PlaynitePluginStatusDto
    {
        public string State;
        public string Message;

        public static PlaynitePluginStatusDto FromJson(IDictionary<string, object> o)
        {
            return new PlaynitePluginStatusDto
            {
                State = Json.GetString(o, "state"),
                Message = Json.GetString(o, "message"),
            };
        }
    }
}
