using System;

namespace Dagobert;

internal enum ThinMarketPricingAction
{
  Skip,
  UseReference,
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
    int comparableListingCount,
    uint? floorPrice,
    uint? ownLowestPrice,
    SaleReference? saleReference,
    ThinMarketPricingOptions options,
    DateTimeOffset now)
  {
    if (!options.Enabled || comparableListingCount > Math.Max(0, options.MaxListings))
    {
      var reason = options.Enabled
        ? ThinMarketPricingReason.TooManyListings
        : ThinMarketPricingReason.FallbackDisabled;
      return new ThinMarketPricingDecision(ThinMarketPricingAction.Skip, 0, reason);
    }

    if (!IsSaleReferenceCredible(saleReference, options, now, out var credibilityReason))
      return new ThinMarketPricingDecision(ThinMarketPricingAction.Skip, 0, credibilityReason);

    var reference = saleReference.GetValueOrDefault().MedianUnitPrice;
    if (floorPrice is null && ownLowestPrice is null)
      return new ThinMarketPricingDecision(
        ThinMarketPricingAction.UseReference,
        reference,
        ThinMarketPricingReason.EmptyBoardUseReference);

    if (floorPrice is null)
    {
      if (IsWithinTolerance(ownLowestPrice.GetValueOrDefault(), reference, options.TolerancePercent))
        return new ThinMarketPricingDecision(
          ThinMarketPricingAction.UseReference,
          reference,
          ThinMarketPricingReason.OwnPriceWithinTolerance);

      return new ThinMarketPricingDecision(
        ThinMarketPricingAction.Skip,
        0,
        ThinMarketPricingReason.OwnPriceOutsideTolerance);
    }

    if (!IsWithinTolerance(floorPrice.Value, reference, options.TolerancePercent))
      return new ThinMarketPricingDecision(
        ThinMarketPricingAction.Skip,
        0,
        ThinMarketPricingReason.FloorOutsideTolerance);

    return new ThinMarketPricingDecision(
      ThinMarketPricingAction.UndercutFloor,
      floorPrice.Value,
      ThinMarketPricingReason.FloorWithinTolerance);
  }

  private static bool IsSaleReferenceCredible(
    SaleReference? saleReference,
    ThinMarketPricingOptions options,
    DateTimeOffset now,
    out ThinMarketPricingReason reason)
  {
    if (saleReference is null || saleReference.Value.MedianUnitPrice == 0)
    {
      reason = ThinMarketPricingReason.SaleReferenceMissingOrZero;
      return false;
    }

    if (saleReference.Value.RecentHistoryCount < Math.Max(1, options.MinRecentSales))
    {
      reason = ThinMarketPricingReason.NotEnoughRecentSales;
      return false;
    }

    var maxAge = TimeSpan.FromDays(Math.Max(1, options.MaxSaleAgeDays));
    if (saleReference.Value.LatestSaleAt < now - maxAge)
    {
      reason = ThinMarketPricingReason.LatestSaleTooOld;
      return false;
    }

    reason = ThinMarketPricingReason.FloorWithinTolerance;
    return true;
  }

  private static bool IsWithinTolerance(uint candidatePrice, uint referencePrice, float tolerancePercent)
  {
    var tolerance = (decimal)Math.Clamp(tolerancePercent, 0f, 1000f) / 100m;
    var reference = (decimal)referencePrice;
    var candidate = (decimal)candidatePrice;
    var lowerBound = reference * (1m - tolerance);
    var upperBound = reference * (1m + tolerance);
    return candidate >= lowerBound && candidate <= upperBound;
  }
}
