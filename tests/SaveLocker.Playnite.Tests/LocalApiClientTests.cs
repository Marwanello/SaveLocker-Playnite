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
            var serverTask = RespondOnceAsync(200, "{\"decision\":\"Proceed\"}");
            await client.LaunchAnswerAsync(gameId, "q1", "keep", true);
            var request = await serverTask;

            Assert.Equal("POST", request.HttpMethod);
            Assert.Equal($"/api/games/{gameId}/launch-answer", request.Url.AbsolutePath);
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
