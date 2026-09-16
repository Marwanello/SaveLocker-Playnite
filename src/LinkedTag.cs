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
    /// (LinkStatusButton's "✓ Synced" button) is the SDK's only per-game visual hook, it only ever
    /// appears on the details page, and even there only under themes whose XAML wires it in (Harmony,
    /// used for this project's own hardware verification, does not — nor does it bind to Tags anywhere
    /// in its own XAML, confirmed by inspection). Tags still make linked games filterable/searchable
    /// from Playnite's own sidebar regardless of theme, and will render as visible chips under most
    /// other themes if the active one ever changes.
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
