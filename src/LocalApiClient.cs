using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Playnite.SDK;

namespace SaveLocker.Playnite
{
    /// <summary>
    /// Thin caller of the agent's own local API (SaveLocker/src/Agent.Core/AgentApiServer.cs) — the
    /// same loopback surface the agent-ui React app and the CLI use. This plugin holds no sync rules
    /// of its own; every method here either reads state or forwards to a route whose *decision* lives
    /// entirely in the agent's SyncEngine. A transport failure from any of these must read as "agent
    /// unreachable," never as a reason to change what a caller does with a launch — see
    /// SaveLockerPlugin's fail-open handling, not this class.
    /// </summary>
    internal sealed class LocalApiClient
    {
        private static readonly ILogger Logger = LogManager.GetLogger();

        // HttpClient.Timeout applies per-request regardless of any CancellationToken passed to
        // SendAsync, so a single shared instance can't have a short timeout for most calls and a long
        // one for post-exit-sync's save upload at the same time — it's left infinite here and every
        // call enforces its own timeout via a linked CancellationTokenSource instead (see SendAsync).
        private static readonly HttpClient Http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };

        // The agent is local (loopback) for every route except post-exit-sync's underlying save
        // upload, so a hung — not merely unreachable — agent should still fail the pre-launch gate
        // open in well under a minute rather than the 10 minutes a single shared timeout previously
        // forced (SaveLockerPlugin's ActivateGlobalProgress dialog for the pre-launch check is not
        // cancelable, so this is the only thing standing between a hung agent and a 10-minute-blocked
        // launch).
        private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(20);

        private readonly Func<SaveLockerSettings> settings;

        public LocalApiClient(Func<SaveLockerSettings> settings)
        {
            this.settings = settings;
        }

        // Lets callers that only hold a LocalApiClient (SyncNowAction, ConflictResolver) build a
        // "open the agent at ___" message without each needing their own reference to Settings.
        public string AgentUrl => settings().AgentUrl;

        /// <summary>
        /// <see cref="LocalAuth"/> (SaveLocker/src/Agent.Core/LocalAuth.cs) mints this once per
        /// machine-install beside config.json, 0600. Read fresh every call — it is a handful of
        /// bytes, and caching it risks acting on a stale token across an agent reinstall.
        /// </summary>
        private string ReadToken()
        {
            var path = Path.Combine(settings().StateDir, "api-token");
            return File.Exists(path) ? File.ReadAllText(path).Trim() : null;
        }

        // `tolerateConflict` scopes AgentApiServer.cs's "409 means a decision DTO, not an error" routes
        // (pre-launch-sync's ProceedSyncPaused, post-exit-sync's already-syncing skip) to only those
        // routes — a bare 409 from anything else (e.g. /resolve, /alias) is a real error and must not
        // be silently swallowed as success.
        private async Task<T> SendAsync<T>(
            HttpMethod method, string route, object body, Func<object, T> parse, CancellationToken ct,
            bool tolerateConflict = false, TimeSpan? timeout = null)
        {
            using (var cts = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                cts.CancelAfter(timeout ?? DefaultTimeout);

                var url = settings().AgentUrl.TrimEnd('/') + route;
                using (var request = new HttpRequestMessage(method, url))
                {
                    var token = ReadToken();
                    if (!string.IsNullOrEmpty(token))
                        request.Headers.Add(LocalAuthHeaderName, token);

                    if (body != null)
                        request.Content = new StringContent(Json.Write(body), Encoding.UTF8, "application/json");

                    using (var response = await Http.SendAsync(request, cts.Token).ConfigureAwait(false))
                    {
                        var text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        var isTolerableConflict = tolerateConflict && response.StatusCode == System.Net.HttpStatusCode.Conflict;
                        if (!response.IsSuccessStatusCode && !isTolerableConflict)
                            throw new HttpRequestException($"{(int)response.StatusCode} from {route}: {text}");

                        return parse(Json.Parse(text));
                    }
                }
            }
        }

        // Matches SaveLocker.Agent.LocalAuth.HeaderName exactly (SaveLocker/src/Agent.Core/LocalAuth.cs)
        // — duplicated here rather than referenced, since this plugin does not link that assembly.
        private const string LocalAuthHeaderName = "X-SaveLocker-Token";

        public async Task<AgentStateDto> GetStateAsync(CancellationToken ct = default(CancellationToken))
        {
            return await SendAsync(HttpMethod.Get, "/api/state", null, o => AgentStateDto.FromJson(Json.AsObject(o)), ct).ConfigureAwait(false);
        }

        public async Task<List<TrackedGameDto>> GetGamesAsync(CancellationToken ct = default(CancellationToken))
        {
            return await SendAsync(HttpMethod.Get, "/api/games", null,
                o => { var list = new List<TrackedGameDto>(); foreach (var g in Json.AsObjectArray(o)) list.Add(TrackedGameDto.FromJson(g)); return list; },
                ct).ConfigureAwait(false);
        }

        public async Task<LaunchGateResult> PreLaunchSyncAsync(Guid gameId, CancellationToken ct)
        {
            // 409 here is AgentApiServer.cs's ProceedSyncPaused decision (a sync already holds the
            // gate), not an error — SendAsync still parses the body into a LaunchGateResult either way.
            return await SendAsync(HttpMethod.Post, $"/api/games/{gameId}/pre-launch-sync", new { }, o => LaunchGateResult.FromJson(Json.AsObject(o)), ct, tolerateConflict: true).ConfigureAwait(false);
        }

        public async Task PostExitSyncAsync(Guid gameId, CancellationToken ct = default(CancellationToken))
        {
            // The one route whose own work (the actual save upload) can legitimately take a while;
            // 409 here just means another sync already held the gate and this exit-sync was skipped —
            // safe to treat as done, since it will retry on the next sync regardless.
            await SendAsync<object>(HttpMethod.Post, $"/api/games/{gameId}/post-exit-sync", new { }, o => null, ct, tolerateConflict: true, timeout: TimeSpan.FromMinutes(10)).ConfigureAwait(false);
        }

        public async Task<ConflictDto> GetConflictAsync(Guid conflictId, CancellationToken ct = default(CancellationToken))
        {
            return await SendAsync(HttpMethod.Get, $"/api/conflicts/{conflictId}", null, o => ConflictDto.FromJson(Json.AsObject(o)), ct).ConfigureAwait(false);
        }

        public async Task<SaveVersionDto> GetVersionAsync(Guid versionId, CancellationToken ct = default(CancellationToken))
        {
            return await SendAsync(HttpMethod.Get, $"/api/versions/{versionId}", null, o => SaveVersionDto.FromJson(Json.AsObject(o)), ct).ConfigureAwait(false);
        }

        public async Task<VersionStatsDto> GetVersionStatsAsync(Guid versionId, CancellationToken ct = default(CancellationToken))
        {
            return await SendAsync(HttpMethod.Get, $"/api/versions/{versionId}/stats", null, o => VersionStatsDto.FromJson(Json.AsObject(o)), ct).ConfigureAwait(false);
        }

        public async Task ResolveConflictAsync(Guid conflictId, Guid winningVersionId, bool keepBoth, CancellationToken ct = default(CancellationToken))
        {
            await SendAsync<object>(HttpMethod.Post, $"/api/conflicts/{conflictId}/resolve",
                new { winningVersionId = winningVersionId.ToString(), keepBoth }, o => null, ct).ConfigureAwait(false);
        }

        public async Task<CandidateLookupResult> CandidatesLookupAsync(string name, string installDir, string steamAppId, string store, CancellationToken ct = default(CancellationToken))
        {
            return await SendAsync(HttpMethod.Post, "/api/candidates/lookup",
                new { name, installDir, steamAppId, store },
                o => CandidateLookupResult.FromJson(Json.AsObject(o)), ct).ConfigureAwait(false);
        }

        public async Task<List<string>> ManifestSearchAsync(string query, CancellationToken ct = default(CancellationToken))
        {
            var url = "/api/manifest/search?q=" + Uri.EscapeDataString(query ?? "");
            return await SendAsync(HttpMethod.Get, url, null,
                o => { var list = new List<string>(); var arr = o as IList<object>; if (arr != null) foreach (var x in arr) list.Add(Convert.ToString(x)); return list; },
                ct).ConfigureAwait(false);
        }

        public async Task SetAliasAsync(Guid gameId, string alias, CancellationToken ct = default(CancellationToken))
        {
            await SendAsync<object>(HttpMethod.Post, $"/api/games/{gameId}/alias", new { alias }, o => null, ct).ConfigureAwait(false);
        }

        /// <summary>Sets a manually-browsed path onto a still-cached candidate (tasks/playnite-plugin/plan.md
        /// Phase 12, tier 4). The server re-validates through SavePathGuard the same as a typed Add
        /// Games path — a refusal surfaces as an HttpRequestException whose body carries the reason.</summary>
        public async Task SetCandidateFolderAsync(int candidateId, string path, CancellationToken ct = default(CancellationToken))
        {
            await SendAsync<object>(HttpMethod.Post, $"/api/candidates/{candidateId}/folder", new { path }, o => null, ct).ConfigureAwait(false);
        }

        public async Task<EnrollResult> EnrollAsync(int candidateId, CancellationToken ct = default(CancellationToken))
        {
            return await SendAsync(HttpMethod.Post, "/api/enroll", new { ids = new[] { candidateId } },
                o => EnrollResult.FromJson(Json.AsObject(o)), ct).ConfigureAwait(false);
        }
    }
}
