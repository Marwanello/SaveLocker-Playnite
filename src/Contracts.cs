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
        /// <summary>The playthrough this PC follows for the game (null = the game's default), and how many the agent last saw.
        /// Absent on an agent that predates playthroughs, which reads as one.</summary>
        public Guid? PlaythroughId;
        public string PlaythroughName;
        public int PlaythroughCount = 1;

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
                PlaythroughId = Json.GetNullableGuid(o, "playthroughId"),
                PlaythroughName = Json.GetString(o, "playthroughName"),
                PlaythroughCount = Math.Max(1, Json.GetInt(o, "playthroughCount", 1)),
            };
        }
    }

    /// <summary>LaunchDecision, SaveLocker/src/Agent.Core/SyncEngine.cs — only "Blocked" may stop a launch.</summary>
    internal enum LaunchDecision
    {
        Proceed,
        ProceedSyncPaused,
        Blocked,
        /// <summary>SaveLocker tasks/emulator-saves Phase 5c: launching is fine with <see cref="LaunchGateResult.Question"/>'s
        /// default already applied by the agent; the plugin may offer the other choices first.</summary>
        Ask,
    }

    /// <summary>LaunchChoice, SaveLocker/src/Agent.Core/LaunchQuestions.cs.</summary>
    internal sealed class LaunchChoiceDto
    {
        public string Id;
        public string Label;
        public bool Primary;
    }

    /// <summary>LaunchQuestion, SaveLocker/src/Agent.Core/LaunchQuestions.cs — its DefaultChoice is already in effect.</summary>
    internal sealed class LaunchQuestionDto
    {
        public string Id;
        public string Kind;
        public string Title;
        public string Text;
        public List<LaunchChoiceDto> Choices = new List<LaunchChoiceDto>();
        public string DefaultChoice;
        public int TimeoutSeconds;
        public bool CanRemember;

        public static LaunchQuestionDto FromJson(IDictionary<string, object> o)
        {
            if (o == null) return null;
            var q = new LaunchQuestionDto
            {
                Id = Json.GetString(o, "id"),
                Kind = Json.GetString(o, "kind"),
                Title = Json.GetString(o, "title"),
                Text = Json.GetString(o, "text"),
                DefaultChoice = Json.GetString(o, "defaultChoice"),
                TimeoutSeconds = Json.GetInt(o, "timeoutSeconds"),
                CanRemember = Json.GetBool(o, "canRemember"),
            };
            object choices;
            if (o.TryGetValue("choices", out choices) && choices != null)
                foreach (var c in Json.AsObjectArray(choices))
                    q.Choices.Add(new LaunchChoiceDto { Id = Json.GetString(c, "id"), Label = Json.GetString(c, "label"), Primary = Json.GetBool(c, "primary") });
            return string.IsNullOrEmpty(q.Id) || q.Choices.Count == 0 ? null : q;
        }
    }

    internal sealed class LaunchGateResult
    {
        public LaunchDecision Decision;
        public string Reason;
        public Guid? ConflictId;
        public string HolderMachineName;
        public LaunchQuestionDto Question;

        public static LaunchGateResult FromJson(IDictionary<string, object> o)
        {
            var decisionText = Json.GetString(o, "decision") ?? "Proceed";
            LaunchDecision decision;
            switch (decisionText)
            {
                case "ProceedSyncPaused": decision = LaunchDecision.ProceedSyncPaused; break;
                case "Blocked": decision = LaunchDecision.Blocked; break;
                case "Ask": decision = LaunchDecision.Ask; break;
                default: decision = LaunchDecision.Proceed; break;
            }

            return new LaunchGateResult
            {
                Decision = decision,
                Reason = Json.GetString(o, "reason"),
                ConflictId = Json.GetNullableGuid(o, "conflictId"),
                HolderMachineName = Json.GetString(o, "holderMachineName"),
                Question = o.TryGetValue("question", out var question) && question != null ? LaunchQuestionDto.FromJson(Json.AsObject(question)) : null,
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

    /// <summary>SaveLocker/src/Shared/Contracts.cs's <c>PlaythroughDto</c>, the part this plugin reads.</summary>
    internal sealed class PlaythroughDto
    {
        public Guid Id;
        public string Name;
        public bool IsDefault;
        public bool Archived;
        public DateTime? HeadAt;
        public string HeadMachine;
        public bool HasOpenConflict;

        public static PlaythroughDto FromJson(IDictionary<string, object> o)
        {
            object head;
            var headObj = o.TryGetValue("head", out head) && head != null ? Json.AsObject(head) : null;
            return new PlaythroughDto
            {
                Id = Json.GetGuid(o, "id"),
                Name = Json.GetString(o, "name"),
                IsDefault = Json.GetBool(o, "isDefault"),
                Archived = !string.IsNullOrEmpty(Json.GetString(o, "archivedAt")),
                HeadAt = headObj == null ? (DateTime?)null : Json.GetNullableDateTime(headObj, "createdAt"),
                HeadMachine = headObj == null ? null : Json.GetString(headObj, "machineName"),
                HasOpenConflict = Json.GetBool(o, "hasOpenConflict"),
            };
        }
    }

    /// <summary>The agent's <c>GamePlaythroughsDto</c>: the server's list, which one this PC follows, and whether the server
    /// answered. <see cref="Offline"/> means <see cref="Playthroughs"/> is empty because the server could not be reached.</summary>
    internal sealed class GamePlaythroughsDto
    {
        public Guid? SelectedId;
        public string SelectedName;
        public bool Offline;
        public List<PlaythroughDto> Playthroughs = new List<PlaythroughDto>();

        /// <summary>What a device can be put on: archived playthroughs are un-archived in the console first.</summary>
        public List<PlaythroughDto> Choices()
        {
            return Playthroughs.FindAll(p => !p.Archived);
        }

        public static GamePlaythroughsDto FromJson(IDictionary<string, object> o)
        {
            var dto = new GamePlaythroughsDto
            {
                SelectedId = Json.GetNullableGuid(o, "selectedId"),
                SelectedName = Json.GetString(o, "selectedName"),
                Offline = Json.GetBool(o, "offline"),
            };
            object list;
            if (o.TryGetValue("playthroughs", out list) && list != null)
                foreach (var p in Json.AsObjectArray(list)) dto.Playthroughs.Add(PlaythroughDto.FromJson(p));
            return dto;
        }
    }

    /// <summary>The agent's <c>SwitchPlaythroughResponse</c>. A refusal ("the game is running") is Ok=false with the reason in
    /// <see cref="Message"/>; a 409 from the per-game gate arrives as an <c>ErrorResponse</c> and reads the same way.</summary>
    internal sealed class SwitchPlaythroughResult
    {
        public bool Ok;
        public string Message;
        public bool Offline;
        public Guid? PlaythroughId;

        public static SwitchPlaythroughResult FromJson(IDictionary<string, object> o)
        {
            return new SwitchPlaythroughResult
            {
                Ok = Json.GetBool(o, "ok"),
                Message = Json.GetString(o, "message") ?? Json.GetString(o, "error") ?? "The agent gave no reason.",
                Offline = Json.GetBool(o, "offline"),
                PlaythroughId = Json.GetNullableGuid(o, "playthroughId"),
            };
        }
    }

    /// <summary>The agent's <c>StartFileDto</c>: a save file <i>Start fresh</i> can keep, ticked by default for a profile or settings file.</summary>
    internal sealed class StartFileDto
    {
        public string Path;
        public long Size;
        public bool KeepByDefault;

        public static StartFileDto FromJson(IDictionary<string, object> o)
        {
            return new StartFileDto { Path = Json.GetString(o, "path"), Size = Json.GetLong(o, "size"), KeepByDefault = Json.GetBool(o, "keepByDefault") };
        }
    }
}
