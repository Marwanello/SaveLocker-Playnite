using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using SaveLocker.Playnite;
using Xunit;

namespace SaveLocker.Playnite.Tests
{
    /// <summary>
    /// tasks/playnite-plugin/plan.md Phase 15's "drives the plugin's... HTTP client against a stub
    /// local-API server, never against real Playnite" — an <see cref="HttpListener"/>-backed fake of
    /// the agent's local API (the same stub-server technique the main SaveLocker repo's own test
    /// suites use), exercising the token header, JSON parsing, and the tolerateConflict 409 handling
    /// LocalApiClient's own doc comment calls out as easy to get subtly wrong.
    /// </summary>
    public class LocalApiClientTests : IDisposable
    {
        private readonly HttpListener listener;
        private readonly string prefix;
        private readonly string stateDir;

        public LocalApiClientTests()
        {
            var port = GetFreePort();
            prefix = $"http://127.0.0.1:{port}/";
            listener = new HttpListener();
            listener.Prefixes.Add(prefix);
            listener.Start();

            stateDir = Path.Combine(Path.GetTempPath(), "SaveLockerPlayniteTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(stateDir);
            File.WriteAllText(Path.Combine(stateDir, "api-token"), "test-token-123");
        }

        public void Dispose()
        {
            listener.Stop();
            listener.Close();
            try { Directory.Delete(stateDir, recursive: true); } catch { /* best effort */ }
        }

        private static int GetFreePort()
        {
            var l = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
            l.Start();
            var port = ((IPEndPoint)l.LocalEndpoint).Port;
            l.Stop();
            return port;
        }

        private LocalApiClient MakeClient()
        {
            return new LocalApiClient(() => new SaveLockerSettings { AgentUrl = prefix.TrimEnd('/'), StateDir = stateDir });
        }

        // Handles exactly one request, then stops listening for more — every test here makes one call.
        private async Task<HttpListenerRequest> RespondOnceAsync(int statusCode, string json)
        {
            var context = await listener.GetContextAsync().ConfigureAwait(false);
            var request = context.Request;
            context.Response.StatusCode = statusCode;
            context.Response.ContentType = "application/json";
            var bytes = Encoding.UTF8.GetBytes(json);
            context.Response.ContentLength64 = bytes.Length;
            context.Response.OutputStream.Write(bytes, 0, bytes.Length);
            context.Response.OutputStream.Close();
            return request;
        }

        // RespondOnceAsync, with the request body read before the response closes it.
        private async Task<Tuple<HttpListenerRequest, string>> RespondOnceWithBodyAsync(int statusCode, string json)
        {
            var context = await listener.GetContextAsync().ConfigureAwait(false);
            string body;
            using (var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8))
                body = await reader.ReadToEndAsync().ConfigureAwait(false);
            context.Response.StatusCode = statusCode;
            context.Response.ContentType = "application/json";
            var bytes = Encoding.UTF8.GetBytes(json);
            context.Response.ContentLength64 = bytes.Length;
            context.Response.OutputStream.Write(bytes, 0, bytes.Length);
            context.Response.OutputStream.Close();
            return Tuple.Create(context.Request, body);
        }

        [Fact]
        public async Task GetGamesAsync_ParsesJsonArray()
        {
            var client = MakeClient();
            var json = "[{\"id\":\"11111111-1111-1111-1111-111111111111\",\"name\":\"Celeste\"," +
                       "\"path\":\"C:\\\\saves\\\\celeste\",\"processNames\":[\"Celeste.exe\"]," +
                       "\"alias\":null,\"steamAppId\":504230,\"pullBeforeLaunchEnabled\":null," +
                       "\"hasSteamCloud\":true,\"pushAfterExitEnabled\":null,\"installDir\":null}]";

            var serverTask = RespondOnceAsync(200, json);
            var games = await client.GetGamesAsync();
            await serverTask;

            var game = Assert.Single(games);
            Assert.Equal("Celeste", game.Name);
            Assert.Equal((uint?)504230, game.SteamAppId);
            Assert.True(game.HasSteamCloud);
        }

        [Fact]
        public async Task GetPlaythroughsAsync_ParsesTheListAndTheSelectedOne()
        {
            var client = MakeClient();
            var main = "22222222-2222-2222-2222-222222222222";
            var hard = "33333333-3333-3333-3333-333333333333";
            var json = "{\"selectedId\":\"" + hard + "\",\"selectedName\":\"Hard mode\",\"offline\":false,\"selectPending\":false,\"playthroughs\":[" +
                "{\"id\":\"" + main + "\",\"name\":\"Main\",\"isDefault\":true,\"archivedAt\":null,\"head\":{\"createdAt\":\"2026-10-20T10:00:00Z\",\"machineName\":\"Deck\"},\"hasOpenConflict\":false}," +
                "{\"id\":\"" + hard + "\",\"name\":\"Hard mode\",\"isDefault\":false,\"archivedAt\":null,\"head\":null,\"hasOpenConflict\":false}," +
                "{\"id\":\"44444444-4444-4444-4444-444444444444\",\"name\":\"Old\",\"isDefault\":false,\"archivedAt\":\"2026-10-01T00:00:00Z\",\"head\":null,\"hasOpenConflict\":false}],\"copies\":[]}";

            var serverTask = RespondOnceAsync(200, json);
            var list = await client.GetPlaythroughsAsync(Guid.NewGuid());
            var request = await serverTask;

            Assert.Contains("/playthroughs", request.Url.AbsolutePath);
            Assert.Equal(Guid.Parse(hard), list.SelectedId);
            Assert.Equal(3, list.Playthroughs.Count);
            Assert.Equal(new[] { "Main", "Hard mode" }, list.Choices().ConvertAll(p => p.Name).ToArray());
            Assert.Equal("Deck", list.Playthroughs[0].HeadMachine);
            Assert.Null(list.Playthroughs[1].HeadAt);
        }

        [Fact]
        public async Task SwitchPlaythroughAsync_PostsToTheSwitchRouteAndReadsTheResult()
        {
            var client = MakeClient();
            var gameId = Guid.NewGuid();
            var target = Guid.NewGuid();
            var serverTask = RespondOnceAsync(200, "{\"ok\":false,\"message\":\"Celeste is running.\",\"offline\":false,\"playthroughId\":null}");
            var result = await client.SwitchPlaythroughAsync(gameId, target);
            var request = await serverTask;

            Assert.Equal("POST", request.HttpMethod);
            Assert.EndsWith("/api/games/" + gameId + "/playthroughs/" + target + "/switch", request.Url.AbsolutePath);
            Assert.False(result.Ok);
            Assert.Equal("Celeste is running.", result.Message);
        }

        [Fact]
        public async Task SwitchPlaythroughAsync_ReadsAGateConflictAsARefusalNotAnError()
        {
            var client = MakeClient();
            var serverTask = RespondOnceAsync(409, "{\"error\":\"A sync of this game is already running. Watch the activity feed.\"}");
            var result = await client.SwitchPlaythroughAsync(Guid.NewGuid(), Guid.NewGuid());
            await serverTask;

            Assert.False(result.Ok);
            Assert.Contains("already running", result.Message);
        }

        [Fact]
        public async Task NewPlaythroughAsync_SendsTheNameTheStartAndTheKeptFiles()
        {
            var client = MakeClient();
            var serverTask = RespondOnceWithBodyAsync(200, "{\"ok\":true,\"message\":\"Created.\",\"offline\":false,\"playthroughId\":\"55555555-5555-5555-5555-555555555555\"}");
            var result = await client.NewPlaythroughAsync(Guid.NewGuid(), "Dark Urge", true, new[] { "profile8.lsf" }, true);
            var sent = await serverTask;

            Assert.True(result.Ok);
            Assert.Equal(Guid.Parse("55555555-5555-5555-5555-555555555555"), result.PlaythroughId);
            Assert.Contains("\"name\":\"Dark Urge\"", sent.Item2);
            Assert.Contains("\"startFresh\":true", sent.Item2);
            Assert.Contains("\"keepFiles\":[\"profile8.lsf\"]", sent.Item2);
            Assert.Contains("\"playHere\":true", sent.Item2);
        }

        [Fact]
        public async Task GetActivitySinceAsync_ReturnsOnlyTheLinesFromTheSwitchOn()
        {
            var client = MakeClient();
            var json = "{\"current\":{},\"recent\":[{\"timestampUtc\":\"2026-10-27T09:00:00Z\",\"message\":\"old\"},{\"timestampUtc\":\"2026-10-27T10:00:05Z\",\"message\":\"Uploading Main\"}]}";
            var serverTask = RespondOnceAsync(200, json);
            var lines = await client.GetActivitySinceAsync(new DateTime(2026, 10, 27, 10, 0, 0, DateTimeKind.Utc));
            await serverTask;

            Assert.Equal(new[] { "Uploading Main" }, lines.ToArray());
        }

        [Fact]
        public async Task GetGamesAsync_ReadsThePlaythroughFieldsAndDefaultsOldAgentsToOne()
        {
            var client = MakeClient();
            var json = "[{\"id\":\"11111111-1111-1111-1111-111111111111\",\"name\":\"A\",\"playthroughId\":\"22222222-2222-2222-2222-222222222222\",\"playthroughName\":\"Hard mode\",\"playthroughCount\":3}," +
                       "{\"id\":\"33333333-3333-3333-3333-333333333333\",\"name\":\"B\"}]";
            var serverTask = RespondOnceAsync(200, json);
            var games = await client.GetGamesAsync();
            await serverTask;

            Assert.Equal(3, games[0].PlaythroughCount);
            Assert.Equal("Hard mode", games[0].PlaythroughName);
            Assert.Equal(1, games[1].PlaythroughCount);
            Assert.Null(games[1].PlaythroughId);
        }

        [Fact]
        public async Task Requests_CarryTheLocalAuthTokenHeader()
        {
            var client = MakeClient();
            var serverTask = RespondOnceAsync(200, "[]");
            await client.GetGamesAsync();
            var request = await serverTask;

            Assert.Equal("test-token-123", request.Headers["X-SaveLocker-Token"]);
        }

        [Fact]
        public async Task PreLaunchSyncAsync_TreatsHttp409AsADecisionNotAnError()
        {
            var client = MakeClient();
            var json = "{\"decision\":\"ProceedSyncPaused\",\"reason\":\"busy\",\"conflictId\":null,\"holderMachineName\":\"OtherPC\"}";

            var serverTask = RespondOnceAsync(409, json);
            var result = await client.PreLaunchSyncAsync(Guid.NewGuid(), default);
            await serverTask;

            Assert.Equal(LaunchDecision.ProceedSyncPaused, result.Decision);
            Assert.Equal("OtherPC", result.HolderMachineName);
        }

        [Fact]
        public async Task PreLaunchSyncAsync_ParsesAnAskWithItsQuestion()
        {
            var client = MakeClient();
            var json = "{\"decision\":\"Ask\",\"reason\":\"Xenia set to start as Driver.\",\"question\":{\"id\":\"q1\",\"kind\":\"xenia-profile\"," +
                "\"title\":\"Xenia would start as Player1\",\"text\":\"Saves are under Driver.\",\"choices\":[{\"id\":\"profile\",\"label\":\"Start as Driver\",\"primary\":true}," +
                "{\"id\":\"keep\",\"label\":\"Start as Player1\",\"primary\":false}],\"defaultChoice\":\"profile\",\"timeoutSeconds\":30,\"canRemember\":true}}";

            var serverTask = RespondOnceAsync(200, json);
            var result = await client.PreLaunchSyncAsync(Guid.NewGuid(), default);
            await serverTask;

            Assert.Equal(LaunchDecision.Ask, result.Decision);
            Assert.Equal("q1", result.Question.Id);
            Assert.Equal("profile", result.Question.DefaultChoice);
            Assert.Equal(2, result.Question.Choices.Count);
            Assert.Equal("Start as Player1", result.Question.Choices[1].Label);
            Assert.True(result.Question.CanRemember);
        }

        [Fact]
        public async Task An_unknown_decision_still_reads_as_Proceed()
        {
            var client = MakeClient();
            var serverTask = RespondOnceAsync(200, "{\"decision\":\"SomethingNewer\"}");
            var result = await client.PreLaunchSyncAsync(Guid.NewGuid(), default);
            await serverTask;

            Assert.Equal(LaunchDecision.Proceed, result.Decision);
            Assert.Null(result.Question);
        }

        [Fact]
        public async Task LaunchAnswerAsync_PostsTheChoice()
        {
            var client = MakeClient();
            var gameId = Guid.NewGuid();
            var serverTask = RespondOnceWithBodyAsync(200, "{\"decision\":\"Proceed\"}");
            await client.LaunchAnswerAsync(gameId, "q1", "keep", true);
            var request = await serverTask;

            Assert.Equal("POST", request.Item1.HttpMethod);
            Assert.Equal($"/api/games/{gameId}/launch-answer", request.Item1.Url.AbsolutePath);
            // The agent's LaunchAnswerRequest(QuestionId, Choice, Remember), bound case-insensitively.
            var body = Json.AsObject(Json.Parse(request.Item2));
            Assert.Equal("q1", Json.GetString(body, "questionId"));
            Assert.Equal("keep", Json.GetString(body, "choice"));
            Assert.True(Json.GetBool(body, "remember"));
        }

        [Fact]
        public async Task ResolveConflictAsync_Http409IsNotTolerated_Throws()
        {
            // Unlike pre-launch-sync/post-exit-sync, a 409 from /resolve is a genuine error
            // (SendAsync's own tolerateConflict scoping) — a caller must not silently treat a failed
            // resolve as success.
            var client = MakeClient();
            var serverTask = RespondOnceAsync(409, "{\"error\":\"already resolved\"}");

            await Assert.ThrowsAsync<System.Net.Http.HttpRequestException>(
                () => client.ResolveConflictAsync(Guid.NewGuid(), Guid.NewGuid(), keepBoth: false));

            await serverTask;
        }

        [Fact]
        public async Task GetSyncStatusAsync_ParsesConflictFields()
        {
            var client = MakeClient();
            var conflictId = Guid.NewGuid();
            var json = $"{{\"inSync\":false,\"hasOpenConflict\":true,\"conflictId\":\"{conflictId}\"}}";

            var serverTask = RespondOnceAsync(200, json);
            var status = await client.GetSyncStatusAsync(Guid.NewGuid());
            await serverTask;

            Assert.False(status.InSync);
            Assert.True(status.HasOpenConflict);
            Assert.Equal(conflictId, status.ConflictId);
        }
    }
}
