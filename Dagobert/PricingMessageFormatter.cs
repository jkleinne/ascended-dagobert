using System;
using System.Collections.Generic;
using System.Globalization;

namespace Dagobert;

internal static class PricingMessageFormatter
{
  private const string FallbackNoPriceReason = "pricing did not return a usable price";
  private const int MinimumListingCount = 0;
  private const int DefaultMinimumRecentSales = 1;
  private const int DefaultMaxSaleAgeDays = 1;
  private const int DefaultUndercutAmount = 1;
  private const int MinimumElapsedMinutes = 0;

  internal static string FormatNoPriceReason(PricingDebugDetail? debugDetail, DateTimeOffset now)
  {
    if (debugDetail is null)
      return FallbackNoPriceReason;

    return debugDetail.Reason switch
    {
      PricingDebugReason.MarketBoardRequestFailed => $"the market board request failed with status {debugDetail.RequestStatus ?? "unknown"}",
      PricingDebugReason.DuplicateMarketBoardRequest => "a duplicate market board response was ignored",
      PricingDebugReason.NoEligibleListings => "no eligible market board listings were found",
      PricingDebugReason.NoCredibleListing => "bait guard found no credible listing",
      PricingDebugReason.OwnPriceAlreadyLowest => "your listing is already at or below the credible competitor",
      PricingDebugReason.UndercutCompetitor => "a price was calculated but was not available to set",
      PricingDebugReason.ThinMarketUseAverage => "thin market selected a Universalis average, but the result was not available to set",
      PricingDebugReason.ThinMarketUndercutFloor => "thin market selected an undercut floor, but the result was not available to set",
      PricingDebugReason.ThinMarketSkip => FormatThinMarketSkipReason(debugDetail, now),
      PricingDebugReason.CachedPrice => "a cached price was selected, but the result was not available to set",
      _ => FallbackNoPriceReason
    };
  }

  internal static string FormatPricingDebug(PricingDebugDetail debugDetail, DateTimeOffset now)
  {
    return debugDetail.Reason switch
    {
      PricingDebugReason.MarketBoardRequestFailed => $"market board request failed with status {debugDetail.RequestStatus ?? "unknown"}",
      PricingDebugReason.DuplicateMarketBoardRequest => "ignored duplicate market board response",
      PricingDebugReason.NoEligibleListings => $"no eligible market board listings were found; {FormatListingCount(debugDetail)}",
      PricingDebugReason.NoCredibleListing => $"skipped because bait guard found no credible listing; {FormatListingCount(debugDetail)}",
      PricingDebugReason.OwnPriceAlreadyLowest => FormatOwnPriceDebug(debugDetail),
      PricingDebugReason.UndercutCompetitor => FormatUndercutDebug(debugDetail),
      PricingDebugReason.ThinMarketUseAverage => FormatThinMarketUseAverage(debugDetail, now),
      PricingDebugReason.ThinMarketUndercutFloor => FormatThinMarketUndercutFloor(debugDetail, now),
      PricingDebugReason.ThinMarketSkip => FormatThinMarketSkipDebug(debugDetail, now),
      PricingDebugReason.CachedPrice => $"used cached price {FormatGil(debugDetail.SelectedPrice)} gil from a previous decision for this item",
      _ => "no pricing debug detail available"
    };
  }

  private static string FormatOwnPriceDebug(PricingDebugDetail debugDetail)
  {
    if (debugDetail.CompetitorPrice is null)
      return $"kept own listing at {FormatGil(debugDetail.OwnLowestPrice)} gil because no credible competitor was lower";

    return $"kept own listing at {FormatGil(debugDetail.OwnLowestPrice)} gil because it is already at or below credible competitor {FormatGil(debugDetail.CompetitorPrice)} gil";
  }

  private static string FormatUndercutDebug(PricingDebugDetail debugDetail)
  {
    return $"undercut credible competitor at {FormatGil(debugDetail.CompetitorPrice)} gil to {FormatGil(debugDetail.SelectedPrice)} gil using {FormatUndercutMode(debugDetail)}";
  }

  private static string FormatThinMarketUseAverage(PricingDebugDetail debugDetail, DateTimeOffset now)
  {
    return $"thin market used Universalis average {FormatAveragePrice(debugDetail)} gil as the price; {FormatThinContext(debugDetail, now)}";
  }

  private static string FormatThinMarketUndercutFloor(PricingDebugDetail debugDetail, DateTimeOffset now)
  {
    return $"thin market checked Universalis average {FormatAveragePrice(debugDetail)} gil and undercut floor {FormatGil(debugDetail.FloorPrice)} gil to {FormatGil(debugDetail.SelectedPrice)} gil; {FormatThinContext(debugDetail, now)}";
  }

  private static string FormatThinMarketSkipDebug(PricingDebugDetail debugDetail, DateTimeOffset now)
  {
    return $"thin market skipped because {FormatThinMarketCondition(debugDetail, now)}; {FormatThinContext(debugDetail, now)}";
  }

  private static string FormatThinMarketSkipReason(PricingDebugDetail debugDetail, DateTimeOffset now)
  {
    return $"thin market skipped because {FormatThinMarketCondition(debugDetail, now)}";
  }

  private static string FormatThinMarketCondition(PricingDebugDetail debugDetail, DateTimeOffset now)
  {
    return debugDetail.ThinMarketReason switch
    {
      ThinMarketPricingReason.FallbackDisabled => "average fallback is disabled",
      ThinMarketPricingReason.TooManyListings => "listing count is above the thin market limit",
      ThinMarketPricingReason.AverageMissingOrZero => "Universalis returned no positive average",
      ThinMarketPricingReason.NotEnoughRecentSales => $"Universalis returned {FormatRecentSalesCount(debugDetail)} recent {PluralizeSaleCount(debugDetail)}, minimum is {GetMinimumRecentSales(debugDetail)}",
      ThinMarketPricingReason.LatestSaleMissing => "Universalis did not return a timestamped recent sale",
      ThinMarketPricingReason.LatestSaleTooOld => $"newest Universalis sale is {FormatLatestSaleAge(debugDetail.AveragePrice?.LatestSaleAt, now)} and max age is {FormatDuration(GetMaxSaleAgeDays(debugDetail), "day")}",
      ThinMarketPricingReason.FloorMissing => "there is no competitor floor",
      ThinMarketPricingReason.FloorOutsideTolerance => $"floor {FormatGil(debugDetail.FloorPrice)} gil is outside {FormatPercent(debugDetail.TolerancePercent)}% tolerance of Universalis average {FormatAveragePrice(debugDetail)} gil",
      ThinMarketPricingReason.EmptyBoardUseAverage => "thin market selected an average for an empty board, but the result was not available to set",
      ThinMarketPricingReason.FloorWithinTolerance => "thin market found a valid floor, but the result was not available to set",
      _ => "thin market policy skipped the item"
    };
  }

  private static string FormatThinContext(PricingDebugDetail debugDetail, DateTimeOffset now)
  {
    var parts = new List<string>
    {
      FormatListingCount(debugDetail)
    };

    if (debugDetail.FloorPrice is not null)
      parts.Add($"floor {FormatGil(debugDetail.FloorPrice)} gil");

    if (debugDetail.OwnLowestPrice is not null)
      parts.Add($"own lowest {FormatGil(debugDetail.OwnLowestPrice)} gil");

    if (debugDetail.AveragePrice is not null)
    {
      parts.Add($"recent sales {debugDetail.AveragePrice.Value.RecentHistoryCount}");
      parts.Add($"newest sale {FormatLatestSaleAge(debugDetail.AveragePrice.Value.LatestSaleAt, now)}");
    }

    return string.Join(", ", parts);
  }

  private static string FormatUndercutMode(PricingDebugDetail debugDetail)
  {
    if (debugDetail.UndercutMode == UndercutMode.Percentage)
      return $"{FormatPercent(debugDetail.UndercutPercent)}% undercut";

    return $"{Math.Max(DefaultUndercutAmount, debugDetail.UndercutAmount ?? DefaultUndercutAmount):N0} gil undercut";
  }

  private static string FormatListingCount(PricingDebugDetail debugDetail)
  {
    return debugDetail.ListingCount is null
      ? "listings unknown"
      : $"listings {Math.Max(MinimumListingCount, debugDetail.ListingCount.Value)}";
  }

  private static string FormatAveragePrice(PricingDebugDetail debugDetail)
  {
    return debugDetail.AveragePrice is null
      ? "unknown"
      : $"{debugDetail.AveragePrice.Value.UnitPrice:N0}";
  }

  private static string FormatRecentSalesCount(PricingDebugDetail debugDetail)
  {
    return debugDetail.AveragePrice is null
      ? "0"
      : debugDetail.AveragePrice.Value.RecentHistoryCount.ToString(CultureInfo.CurrentCulture);
  }

  private static int GetMinimumRecentSales(PricingDebugDetail debugDetail)
  {
    return Math.Max(DefaultMinimumRecentSales, debugDetail.MinRecentSales ?? DefaultMinimumRecentSales);
  }

  private static int GetMaxSaleAgeDays(PricingDebugDetail debugDetail)
  {
    return Math.Max(DefaultMaxSaleAgeDays, debugDetail.MaxSaleAgeDays ?? DefaultMaxSaleAgeDays);
  }

  private static string PluralizeSaleCount(PricingDebugDetail debugDetail)
  {
    return debugDetail.AveragePrice?.RecentHistoryCount == 1 ? "sale" : "sales";
  }

  private static string FormatLatestSaleAge(DateTimeOffset? latestSaleAt, DateTimeOffset now)
  {
    if (latestSaleAt is null)
      return "unknown";

    var age = now - latestSaleAt.Value;
    if (age < TimeSpan.Zero)
      return "in the future";

    if (age.TotalDays >= 1)
      return $"{FormatDuration((int)age.TotalDays, "day")} ago";

    if (age.TotalHours >= 1)
      return $"{FormatDuration((int)age.TotalHours, "hour")} ago";

    return $"{FormatDuration(Math.Max(MinimumElapsedMinutes, (int)age.TotalMinutes), "minute")} ago";
  }

  private static string FormatDuration(int value, string unit)
  {
    return value == 1
      ? $"1 {unit}"
      : $"{value:N0} {unit}s";
  }

  private static string FormatGil(int? gil)
  {
    return gil is null ? "unknown" : $"{gil.Value:N0}";
  }

  private static string FormatPercent(float? percent)
  {
    return percent is null ? "unknown" : $"{percent.Value:0.#}";
  }
}
