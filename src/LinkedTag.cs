using System;
using System.Collections.Generic;
using System.Linq;
using Playnite.SDK;
using Playnite.SDK.Models;

namespace SaveLocker.Playnite
{
    /// <summary>
    /// Marks a Playnite game as SaveLocker-enrolled the only way the SDK actually supports showing
    /// that generically across themes: a Tag. There is no per-game grid/list icon extension point in
    /// the Playnite SDK at all (checked every method on the Plugin base class) — GetGameViewControl
    /// was the SDK's only per-game visual hook, and a status control built on it was removed again:
    /// it only ever appears on the details page, and only under themes whose XAML wires a matching
    /// slot in (no stock theme does, Harmony or Default included, confirmed against Playnite's own
    /// source — nor does Harmony bind to Tags anywhere in its own XAML, confirmed by inspection).
    /// Tags still make linked games filterable/searchable from Playnite's own sidebar regardless of
    /// theme, and will render as visible chips under most other themes if the active one ever changes.
    /// </summary>
    internal static class LinkedTag
    {
        private const string Name = "SaveLocker: Linked";

        public static void Ensure(IPlayniteAPI api, Game game)
        {
            var tag = api.Database.Tags.Add(Name);
            if (game.TagIds != null && game.TagIds.Contains(tag.Id)) return;

            game.TagIds = (game.TagIds ?? new List<Guid>()).Concat(new[] { tag.Id }).ToList();
            api.Database.Games.Update(game);
        }
    }
}
