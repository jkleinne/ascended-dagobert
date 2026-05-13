using Dalamud.Game.Network.Structures;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Dagobert;

internal readonly record struct RecentSaleReference(
  uint MedianUnitPrice,
  int RecentHistoryCount,
  DateTimeOffset LatestSaleAt);

/// <summary>
/// Picks a credible competing listing to undercut, rather than blindly taking the
/// lowest price. Defends against bait listings (e.g. a single-unit listing priced
/// far below the real market to trick a bot into undercutting to near-zero).
/// </summary>
/// <remarks>
/// Pure function — all decisions derive from the listings passed in and the
/// supplied options. No I/O, no global state.
/// </remarks>
internal static class BaitGuard
{
  internal const float DefaultSaleMedianFloorPercent = 50.0f;
  internal const int DefaultLowClusterListings = 3;
  internal const int DefaultLowClusterQuantity = 20;
  internal const float DefaultLowClusterPriceTolerancePercent = 5.0f;

  public readonly record struct Options(
    bool Enabled,
    float FloorPercent,
    int SampleListings,
    float GapPercent,
    int MinQuantity,
    float SaleMedianFloorPercent,
    int LowClusterListings,
    int LowClusterQuantity,
    float LowClusterPriceTolerancePercent)
  {
    public Options(
      bool enabled,
      float floorPercent,
      int sampleListings,
      float gapPercent,
      int minQuantity)
      : this(
        enabled,
        floorPercent,
        sampleListings,
        gapPercent,
        minQuantity,
        DefaultSaleMedianFloorPercent,
        DefaultLowClusterListings,
        DefaultLowClusterQuantity,
        DefaultLowClusterPriceTolerancePercent)
    {
    }
  }

  /// <summary>
  /// Returns the index in <paramref name="listings"/> that should be undercut,
  /// or <c>null</c> if no listing among <paramref name="candidateIndices"/> looks
  /// credible. A null return means the caller should wait for more listings or
  /// skip the item — never undercut against unfiltered bait.
  /// </summary>
  /// <param name="listings">Full listing set (caller does not need to sort).</param>
  /// <param name="candidateIndices">
  /// Indices into <paramref name="listings"/> the caller considers eligible
  /// (HQ filter, own-retainer filter, etc. already applied).
  /// </param>
  public static int? SelectTargetIndex(
    IReadOnlyList<IMarketBoardItemListing> listings,
    IReadOnlyList<int> candidateIndices,
    Options opts,
    RecentSaleReference? saleReference = null)
  {
    if (candidateIndices.Count == 0)
      return null;

    if (!opts.Enabled)
      return MinByPrice(listings, candidateIndices);

    // Sort candidates cheapest-first; bait analysis only makes sense on a sorted set.
    var sorted = candidateIndices
      .OrderBy(i => listings[i].PricePerUnit)
      .ToList();

    // A single candidate has no comparison set, so neither the floor nor the gap
    // filter can run. Returning it would trust a lone listing at face value, which
    // is exactly the case bait guard exists to prevent. Refuse instead; the caller
    // will accumulate more batches or hold position.
    if (sorted.Count == 1)
      return null;

    // Filter 1: minimum stack-size threshold. Items normally sold in stacks
    // (materia, gathered mats, food) frequently see 1-unit bait listings; raising
    // this knob makes the bot ignore them outright. With listing-level anchoring
    // (filter 2), MinQuantity is the primary defense against many-decoy attacks.
    var minQty = (uint)Math.Max(1, opts.MinQuantity);
    var passQty = sorted
      .Where(i => listings[i].ItemQuantity >= minQty)
      .ToList();
    if (passQty.Count == 0)
      return null;

    // Filter 2: listing-median price floor. Take the cheapest N listings (each
    // listing = one stack = one atomic transaction on the market board), use
    // their median price-per-unit as the anchor, and reject anything below
    // FloorPercent of the anchor. Each listing counts once regardless of stack
    // size — buyers can't fractionally split a stack, so a stack is the natural
    // unit of market-price evidence.
    uint floor = ComputeListingMedianFloor(listings, passQty, opts.SampleListings, opts.FloorPercent);
    var passFloor = passQty
      .Where(i => listings[i].PricePerUnit >= floor)
      .ToList();
    if (passFloor.Count == 0)
      return null;

    var sequence = ApplyGapPromotion(listings, passFloor, opts.GapPercent);
    return SelectBySaleReference(listings, sequence, opts, saleReference);
  }

  private static int MinByPrice(
    IReadOnlyList<IMarketBoardItemListing> listings,
    IReadOnlyList<int> candidates)
  {
    int best = candidates[0];
    for (int k = 1; k < candidates.Count; k++)
      if (listings[candidates[k]].PricePerUnit < listings[best].PricePerUnit)
        best = candidates[k];
    return best;
  }

  private static IReadOnlyList<int> ApplyGapPromotion(
    IReadOnlyList<IMarketBoardItemListing> listings,
    IReadOnlyList<int> candidates,
    float gapPercent)
  {
    if (candidates.Count < 2)
      return candidates;

    var firstPrice = listings[candidates[0]].PricePerUnit;
    var secondPrice = listings[candidates[1]].PricePerUnit;
    if ((float)firstPrice * 100f >= gapPercent * secondPrice)
      return candidates;

    return candidates.Skip(1).ToList();
  }

  private static int? SelectBySaleReference(
    IReadOnlyList<IMarketBoardItemListing> listings,
    IReadOnlyList<int> candidates,
    Options opts,
    RecentSaleReference? saleReference)
  {
    if (candidates.Count == 0)
      return null;

    if (saleReference is null || saleReference.Value.MedianUnitPrice == 0)
      return candidates[0];

    var saleFloor = ComputeSaleMedianFloor(saleReference.Value.MedianUnitPrice, opts.SaleMedianFloorPercent);
    foreach (var candidate in candidates)
    {
      if (listings[candidate].PricePerUnit >= saleFloor)
        return candidate;

      if (HasEnoughLowClusterEvidence(listings, candidates, candidate, opts))
        return candidate;
    }

    return null;
  }

  private static uint ComputeSaleMedianFloor(uint medianUnitPrice, float saleMedianFloorPercent)
  {
    return (uint)(medianUnitPrice * Math.Max(0f, saleMedianFloorPercent) / 100f);
  }

  private static bool HasEnoughLowClusterEvidence(
    IReadOnlyList<IMarketBoardItemListing> listings,
    IReadOnlyList<int> candidates,
    int candidate,
    Options opts)
  {
    var candidatePrice = listings[candidate].PricePerUnit;
    var toleranceRatio = (decimal)Math.Max(0f, opts.LowClusterPriceTolerancePercent) / 100m;
    var upperBound = candidatePrice * (1m + toleranceRatio);
    var cluster = candidates
      .Where(index => listings[index].PricePerUnit >= candidatePrice)
      .Where(index => listings[index].PricePerUnit <= upperBound)
      .ToList();

    if (cluster.Count >= Math.Max(1, opts.LowClusterListings))
      return true;

    var totalQuantity = cluster.Sum(index => (long)listings[index].ItemQuantity);
    return totalQuantity >= Math.Max(1, opts.LowClusterQuantity);
  }

  /// <summary>
  /// Returns <paramref name="floorPercent"/>% of the median unit-price across
  /// the cheapest <paramref name="sampleListings"/> entries in
  /// <paramref name="sortedAsc"/>. If fewer listings exist than the sample size,
  /// the median is computed over what's available.
  /// </summary>
  private static uint ComputeListingMedianFloor(
    IReadOnlyList<IMarketBoardItemListing> listings,
    IReadOnlyList<int> sortedAsc,
    int sampleListings,
    float floorPercent)
  {
    int take = Math.Min(sortedAsc.Count, Math.Max(1, sampleListings));
    if (take == 0)
      return 0;

    // sortedAsc is already price-ascending, so the median is the middle entry
    // (or the lower-mid on even counts). Even-count averaging is unnecessary
    // here: we're computing a coarse floor, not a precise statistic.
    int medianIdx = sortedAsc[take / 2];
    uint median = listings[medianIdx].PricePerUnit;
    return (uint)(median * floorPercent / 100f);
  }
}
