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
  uint ReferencePrice);

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
      return new ThinMarketPricingDecision(ThinMarketPricingAction.Skip, 0);

    if (!IsAveragePriceCredible(averagePrice, options, now))
      return new ThinMarketPricingDecision(ThinMarketPricingAction.Skip, 0);

    var average = averagePrice.Value.UnitPrice;
    if (listingCount == 0)
      return new ThinMarketPricingDecision(ThinMarketPricingAction.UseAverage, average);

    if (floorPrice is null || !IsWithinTolerance(floorPrice.Value, average, options.TolerancePercent))
      return new ThinMarketPricingDecision(ThinMarketPricingAction.Skip, 0);

    return new ThinMarketPricingDecision(ThinMarketPricingAction.UndercutFloor, floorPrice.Value);
  }

  private static bool IsAveragePriceCredible(
    ThinMarketAveragePrice? averagePrice,
    ThinMarketPricingOptions options,
    DateTimeOffset now)
  {
    if (averagePrice is null || averagePrice.Value.UnitPrice == 0)
      return false;

    if (averagePrice.Value.RecentHistoryCount < Math.Max(1, options.MinRecentSales))
      return false;

    if (averagePrice.Value.LatestSaleAt is null)
      return false;

    var maxAge = TimeSpan.FromDays(Math.Max(1, options.MaxSaleAgeDays));
    return averagePrice.Value.LatestSaleAt.Value >= now - maxAge;
  }

  private static bool IsWithinTolerance(uint floorPrice, uint averagePrice, float tolerancePercent)
  {
    var tolerance = Math.Clamp(tolerancePercent, 0f, 1000f) / 100m;
    var average = (decimal)averagePrice;
    var floor = (decimal)floorPrice;
    var lowerBound = average * (1m - tolerance);
    var upperBound = average * (1m + tolerance);
    return floor >= lowerBound && floor <= upperBound;
  }
}
