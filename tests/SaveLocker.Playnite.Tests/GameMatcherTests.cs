using System;
using System.Collections.Generic;
using Playnite.SDK.Models;
using SaveLocker.Playnite;
using Xunit;

namespace SaveLocker.Playnite.Tests
{
    /// <summary>
    /// tasks/playnite-plugin/plan.md Phase 15's "automated coverage stays cheap" — GameMatcher's
    /// matching chain is pure logic over plain data (a <see cref="Game"/> and a list of
    /// <c>TrackedGameDto</c>), so it needs no running Playnite, no local API, and no stub server.
    ///
    /// <b>Deliberately NOT covered here: the Steam AppID tier.</b> Confirmed by reflection (see
    /// docs/Gotchas.md's own convention of checking the real SDK rather than guessing) that a bare
    /// <c>new Game()</c> outside a running Playnite instance always resolves <c>Game.Source</c> to
    /// null regardless of <c>SourceId</c> — the property depends on a live database lookup this
    /// process never has. <c>FindMatch</c>'s <c>isSteam</c> check can therefore never be exercised
    /// true from a standalone test; that tier stays manual/hardware-verified, same as the rest of this
    /// plugin's Playnite-SDK-dependent behavior.
    /// </summary>
    public class GameMatcherTests
    {
        private static TrackedGameDto Tracked(string name = null, string installDir = null, string alias = null, uint? steamAppId = null)
        {
            return new TrackedGameDto
            {
                Id = Guid.NewGuid(),
                Name = name,
                InstallDir = installDir,
                Alias = alias,
                SteamAppId = steamAppId,
            };
        }

        [Fact]
        public void FindMatch_NullGame_ReturnsNull()
        {
            Assert.Null(GameMatcher.FindMatch(null, new List<TrackedGameDto> { Tracked("X") }));
        }

        [Fact]
        public void FindMatch_EmptyTrackedList_ReturnsNull()
        {
            var game = new Game { Name = "X" };
            Assert.Null(GameMatcher.FindMatch(game, new List<TrackedGameDto>()));
        }

        [Fact]
        public void FindMatch_InstallDir_ExactMatch()
        {
            var target = Tracked("Hollow Knight", installDir: @"C:\Games\HollowKnight");
            var game = new Game { Name = "Hollow Knight (renamed locally)", InstallDirectory = @"C:\Games\HollowKnight" };

            var match = GameMatcher.FindMatch(game, new List<TrackedGameDto> { target, Tracked("Other", @"C:\Games\Other") });

            Assert.Same(target, match);
        }

        [Theory]
        [InlineData(@"C:\Games\HollowKnight", @"C:\Games\HollowKnight\")]
        [InlineData(@"C:\Games\HollowKnight", @"c:\games\hollowknight")]
        [InlineData(@"C:\Games\HollowKnight/", @"C:\Games\HollowKnight")]
        public void FindMatch_InstallDir_NormalizesCaseAndTrailingSeparator(string trackedDir, string gameDir)
        {
            var target = Tracked("Hollow Knight", installDir: trackedDir);
            var game = new Game { Name = "Something else entirely", InstallDirectory = gameDir };

            Assert.Same(target, GameMatcher.FindMatch(game, new List<TrackedGameDto> { target }));
        }

        [Fact]
        public void FindMatch_InstallDir_AmbiguousMatch_ReturnsNullWithoutFallingThroughToName()
        {
            // Two tracked games share one install dir (a genuinely malformed config, but GameMatcher's
            // own doc comment is explicit: an ambiguous tier stops rather than falling through to a
            // weaker one, even when the weaker tier would have disambiguated uniquely by name).
            var first = Tracked("Game A", installDir: @"C:\Games\Shared");
            var second = Tracked("Game B", installDir: @"C:\Games\Shared");
            var game = new Game { Name = "Game A", InstallDirectory = @"C:\Games\Shared" };

            Assert.Null(GameMatcher.FindMatch(game, new List<TrackedGameDto> { first, second }));
        }

        [Fact]
        public void FindMatch_Name_ExactCaseInsensitiveMatch()
        {
            var target = Tracked("Celeste");
            var game = new Game { Name = "CELESTE" };

            Assert.Same(target, GameMatcher.FindMatch(game, new List<TrackedGameDto> { target }));
        }

        [Fact]
        public void FindMatch_Alias_MatchesEvenWhenNameDiffers()
        {
            var target = Tracked("Sid Meier's Civilization VII", alias: "Civ VII");
            var game = new Game { Name = "Civ VII" };

            Assert.Same(target, GameMatcher.FindMatch(game, new List<TrackedGameDto> { target }));
        }

        [Fact]
        public void FindMatch_NoSignalMatches_ReturnsNull()
        {
            var tracked = Tracked("Celeste", installDir: @"C:\Games\Celeste");
            var game = new Game { Name = "Hades", InstallDirectory = @"C:\Games\Hades" };

            Assert.Null(GameMatcher.FindMatch(game, new List<TrackedGameDto> { tracked }));
        }

        [Fact]
        public void FindByPathOrName_PrefersPathOverName()
        {
            // Enroller.EnrollAsync names a newly-tracked game after the manifest's canonical spelling,
            // which is frequently NOT the query name a Playnite-side lookup searched with — the save
            // directory the candidate actually resolved to is the reliable signal (GameMatcher's own
            // doc comment on this method).
            var wrongNameSameDir = Tracked("Sid Meier's Civilization VII", installDir: null);
            wrongNameSameDir.Path = @"C:\Users\me\Documents\Civ7";
            var tracked = new List<TrackedGameDto> { wrongNameSameDir };

            var found = GameMatcher.FindByPathOrName(tracked, @"C:\Users\me\Documents\Civ7\", "Civ VII");

            Assert.Same(wrongNameSameDir, found);
        }

        [Fact]
        public void FindByPathOrName_FallsBackToNameWhenPathDoesNotMatch()
        {
            var tracked = Tracked("Civ VII");
            tracked.Path = @"C:\Somewhere\Else";

            var found = GameMatcher.FindByPathOrName(new List<TrackedGameDto> { tracked }, @"C:\Not\It", "Civ VII");

            Assert.Same(tracked, found);
        }

        [Fact]
        public void FindByPathOrName_NeitherMatches_ReturnsNull()
        {
            var tracked = Tracked("Civ VII");
            tracked.Path = @"C:\Somewhere\Else";

            Assert.Null(GameMatcher.FindByPathOrName(new List<TrackedGameDto> { tracked }, @"C:\Not\It", "Not It Either"));
        }

        [Theory]
        [InlineData("Steam", "Steam")]
        [InlineData("GOG Galaxy", "Gog")]
        [InlineData("Epic Games Store", "Epic")]
        [InlineData("Amazon Games", "Amazon")]
        [InlineData("Xbox", null)]
        [InlineData("", null)]
        [InlineData(null, null)]
        public void MapStore_MapsKnownSourcesCaseInsensitively(string sourceName, string expected)
        {
            Assert.Equal(expected, GameMatcher.MapStore(sourceName));
        }
    }
}
