using System;

namespace Dagobert;

internal enum ThinMarketPricingAction
{
  Skip,
  UseAverage,
  UndercutFloor
}

internal readonly record struct ThinMarketPricingDecision(
  ThinMarketPricingAction Action,
  uint ReferencePrice,
  ThinMarketPricingReason Reason);

internal readonly record struct ThinMarketAveragePrice(
  uint UnitPrice,
  int RecentHistoryCount,
  DateTimeOffset? LatestSaleAt);

internal readonly record struct ThinMarketPricingOptions(
  bool Enabled,
  int MaxListings,
  int MinRecentSales,
  int MaxSaleAgeDays,
  float TolerancePercent);

internal static class ThinMarketPricePolicy
{
  public static ThinMarketPricingDecision Decide(
    int listingCount,
    uint? floorPrice,
    ThinMarketAveragePrice? averagePrice,
    ThinMarketPricingOptions options,
    DateTimeOffset now)
  {
    if (!options.Enabled || listingCount > Math.Max(0, options.MaxListings))
    {
      var reason = options.Enabled
        ? ThinMarketPricingReason.TooManyListings
        : ThinMarketPricingReason.FallbackDisabled;
      return new ThinMarketPricingDecision(ThinMarketPricingAction.Skip, 0, reason);
    }

    if (!IsAveragePriceCredible(averagePrice, options, now, out var credibilityReason))
      return new ThinMarketPricingDecision(ThinMarketPricingAction.Skip, 0, credibilityReason);

    var average = averagePrice.Value.UnitPrice;
    if (listingCount == 0)
      return new ThinMarketPricingDecision(
        ThinMarketPricingAction.UseAverage,
        average,
        ThinMarketPricingReason.EmptyBoardUseAverage);

    if (floorPrice is null)
      return new ThinMarketPricingDecision(
        ThinMarketPricingAction.Skip,
        0,
        ThinMarketPricingReason.FloorMissing);

    if (!IsWithinTolerance(floorPrice.Value, average, options.TolerancePercent))
      return new ThinMarketPricingDecision(
        ThinMarketPricingAction.Skip,
        0,
        ThinMarketPricingReason.FloorOutsideTolerance);

    return new ThinMarketPricingDecision(
      ThinMarketPricingAction.UndercutFloor,
      floorPrice.Value,
      ThinMarketPricingReason.FloorWithinTolerance);
  }

  private static bool IsAveragePriceCredible(
    ThinMarketAveragePrice? averagePrice,
    ThinMarketPricingOptions options,
    DateTimeOffset now,
    out ThinMarketPricingReason reason)
  {
    if (averagePrice is null || averagePrice.Value.UnitPrice == 0)
    {
      reason = ThinMarketPricingReason.AverageMissingOrZero;
      return false;
    }

    if (averagePrice.Value.RecentHistoryCount < Math.Max(1, options.MinRecentSales))
    {
      reason = ThinMarketPricingReason.NotEnoughRecentSales;
      return false;
    }

    if (averagePrice.Value.LatestSaleAt is null)
    {
      reason = ThinMarketPricingReason.LatestSaleMissing;
      return false;
    }

    var maxAge = TimeSpan.FromDays(Math.Max(1, options.MaxSaleAgeDays));
    if (averagePrice.Value.LatestSaleAt.Value < now - maxAge)
    {
      reason = ThinMarketPricingReason.LatestSaleTooOld;
      return false;
    }

    reason = ThinMarketPricingReason.FloorWithinTolerance;
    return true;
  }

  private static bool IsWithinTolerance(uint floorPrice, uint averagePrice, float tolerancePercent)
  {
    var tolerance = (decimal)Math.Clamp(tolerancePercent, 0f, 1000f) / 100m;
    var average = (decimal)averagePrice;
    var floor = (decimal)floorPrice;
    var lowerBound = average * (1m - tolerance);
    var upperBound = average * (1m + tolerance);
    return floor >= lowerBound && floor <= upperBound;
  }
}
