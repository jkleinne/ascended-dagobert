# Ascended Dagobert

This is a personal fork of [SHOEGAZEssb/Dagobert](https://github.com/SHOEGAZEssb/Dagobert). It adds bait-listing protection and a thin-market average fallback on top of upstream's undercut logic. If you don't need that behavior, install upstream Dagobert instead.

## What this fork adds

### Bait Listing Protection

Upstream Dagobert cuts to the absolute lowest listing on the market board, which lets a single 1-unit decoy at 5 gil drag every retainer's price to the floor while real stacks sit at 1.2k. This fork filters listings for credibility before picking a target, then undercuts the lowest *credible* listing instead.

Three filters apply, in order:

1. **Minimum stack size.** Listings below the configured quantity threshold are ignored (set higher for items normally sold in stacks).
2. **Listing-median price floor.** The bot takes the cheapest N listings and uses their median price-per-unit as the anchor. Anything below `FloorPercent` of that anchor is rejected as bait. Each listing counts once regardless of stack size, because stacks are atomic transactions on the market board (you buy the whole stack or none of it).
3. **Price-gap detector.** If the cheapest credible listing sits below `GapPercent` of the second-cheapest, it gets skipped and the second is targeted instead.

When Universalis has enough recent matching-quality sales, the bot also checks tiny low-price clusters against the recent sale median. A below-median cluster is still accepted when it has enough listings or total quantity to look like a real reset; otherwise the bot skips ahead to the next credible candidate or holds.

If no listing passes the filters, the bot holds off rather than committing to bait. All thresholds are configurable in the "Bait Listing Protection" section of the config window, and the feature can be toggled off entirely to fall back to the legacy "cut to absolute lowest" behavior.

Defaults: 30% listing floor, 5-listing sample, 50% gap, min-quantity 1, 50% sale-median floor, 3 low-cluster listings, 20 low-cluster quantity, 5% low-cluster tolerance, 3 sale-reference sales, 30-day sale-reference age.

### Thin-Market Average Fallback

Upstream Dagobert can't price an item with no competing listings (it has nothing to undercut), and one or two listings are fragile inputs because a single decoy below the bait threshold leaves no credible target. This fork queries Universalis for the home-world average sale price and uses it to price thin markets without firing blind.

Three branches apply once a market is classified as thin (≤ `Max Listings`, default 2):

1. **Empty board → use the average.** Zero listings on your home world: the bot prices at the trusted average outright.
2. **Thin board, floor within tolerance → undercut the floor.** One or two listings whose floor sits within `Average Tolerance` of the average (default ±40%) are treated as honest, and the bot undercuts the floor as it normally would.
3. **Thin board, floor outside tolerance → skip.** A floor too far below the average is treated as bait and the bot holds off — the average is never used to bid against a decoy.

The average is only trusted when Universalis returns at least `Min Recent Sales` rows for the requested quality (default 3) and the newest matching sale is no older than `Max Sale Age` (default 30 days). All thresholds live in the "Thin Market Average Fallback" section of the config window, and the feature can be toggled off entirely.

Defaults: max 2 listings, 40% tolerance, 3 recent sales, 30-day sale age.

### Pricing Debug

Enable `Show Pricing Debug` in the config window when you want to see why a price was selected or skipped. Debug lines appear in chat and in the plugin log, including whether Universalis was consulted, the average sale price returned, recent sale count, newest sale age, current floor, and the selected target price.

### Sale History Visit

When "Open Sale History During Auto Pinch" is enabled in the config (off by default), a
full auto-pinch run briefly opens each retainer's sale history window before moving on.
Sale-tracking plugins that listen for the sale history packet can then record completed
sales without you opening the window manually on every retainer. The visit adds roughly
one second per retainer and never interrupts the run: if the window cannot be opened,
that retainer is skipped with a warning in the plugin log.

## Upstream features

Inherited unchanged from upstream:

- Automatic market board undercutting by fixed gil or percentage
- Configurable per-item behavior
- Text-to-speech notifications on completed pinches

## Installing

This plugin is distributed through the [ascended-plugins](https://github.com/jkleinne/ascended-plugins) Dalamud repository, which aggregates all `ascended-*` plugin forks under a single URL. Add it once and any future plugin in that namespace becomes available without extra repository entries.

- Open `/xlsettings` in chat and switch to the Experimental tab
- Scroll past DevPlugins to the Custom Plugin Repositories section
- Paste this URL into a free text input:

```
https://raw.githubusercontent.com/jkleinne/ascended-plugins/main/pluginmaster.json
```

- Click `+`, tick the new entry's checkbox, and save
- Reopen Dalamud's plugin installer; "Ascended Dagobert" appears under Available Plugins

**Coexistence with upstream Dagobert:** this fork uses a distinct `InternalName` (`ascended-dagobert`), so Dalamud treats it as a separate plugin. It does, however, register the same `/dagobert` chat command as upstream, so the two cannot run simultaneously without command-registration conflicts. Uninstall upstream Dagobert before installing this fork.

If you'd rather use the official upstream distribution:

```
https://raw.githubusercontent.com/SHOEGAZEssb/DalamudPluginRepo/master/pluginmaster.json
```

## Contributing

Fork-specific behavior (Bait Listing Protection and the Thin-Market Average Fallback) is fair game for issues and PRs against this repository. General Dagobert changes should be contributed upstream at [SHOEGAZEssb/Dagobert](https://github.com/SHOEGAZEssb/Dagobert); upstream commits flow into this fork on the next sync.
