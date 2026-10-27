using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Playnite.SDK;

namespace SaveLocker.Playnite
{
    /// <summary>
    /// What the right-click menu knows about playthroughs. <c>GetGameMenuItems</c> runs on every right-click and must not
    /// wait on a network call (SaveLockerPlugin's own note on it), but listing a game's playthroughs takes one agent round
    /// trip that itself asks the server. So the menu reads this memory, and a refresh runs behind it: at startup, on a
    /// right-click once the memory is a little old, and after every switch. The first right-click of a session can therefore
    /// see a game's playthroughs only if the startup refresh has finished; <see cref="PlaythroughActions.PickAndSwitchAsync"/>
    /// is the menu's fallback for the moment it has not.
    /// </summary>
    internal sealed class PlaythroughCache
    {
        private static readonly ILogger Logger = LogManager.GetLogger();
        private static readonly TimeSpan MaxAge = TimeSpan.FromSeconds(30);

        private readonly LocalApiClient client;
        private readonly object gate = new object();
        private List<TrackedGameDto> tracked = new List<TrackedGameDto>();
        private readonly Dictionary<Guid, GamePlaythroughsDto> lists = new Dictionary<Guid, GamePlaythroughsDto>();
        private DateTime refreshedAt = DateTime.MinValue;
        private int refreshing;

        public PlaythroughCache(LocalApiClient client)
        {
            this.client = client;
        }

        public List<TrackedGameDto> Tracked
        {
            get { lock (gate) return tracked; }
        }

        public GamePlaythroughsDto ListFor(Guid gameId)
        {
            lock (gate) return lists.TryGetValue(gameId, out var l) ? l : null;
        }

        public bool IsStale
        {
            get { lock (gate) return DateTime.UtcNow - refreshedAt > MaxAge; }
        }

        public void Forget(Guid gameId)
        {
            lock (gate) lists.Remove(gameId);
        }

        /// <summary>Refreshes the tracked games and the playthrough list of every game with more than one. One refresh at a
        /// time; a failure leaves what was known (an unreachable agent is the normal state of a PC with no SaveLocker running).</summary>
        public Task RefreshAsync()
        {
            if (Interlocked.CompareExchange(ref refreshing, 1, 0) != 0) return Task.FromResult(0);
            return Task.Run(async () =>
            {
                try
                {
                    var games = await client.GetGamesAsync().ConfigureAwait(false);
                    var fresh = new Dictionary<Guid, GamePlaythroughsDto>();
                    foreach (var g in games.Where(x => x.PlaythroughCount > 1))
                    {
                        try { fresh[g.Id] = await client.GetPlaythroughsAsync(g.Id).ConfigureAwait(false); }
                        catch (Exception ex) { Logger.Debug(ex, "SaveLocker: couldn't list playthroughs for " + g.Name); }
                    }
                    lock (gate)
                    {
                        tracked = games;
                        lists.Clear();
                        foreach (var kv in fresh) lists[kv.Key] = kv.Value;
                        refreshedAt = DateTime.UtcNow;
                    }
                }
                catch (Exception ex)
                {
                    Logger.Debug(ex, "SaveLocker: playthrough menu data not refreshed");
                }
                finally
                {
                    Interlocked.Exchange(ref refreshing, 0);
                }
            });
        }
    }
}
