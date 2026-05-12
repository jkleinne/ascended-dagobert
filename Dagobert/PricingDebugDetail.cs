namespace Dagobert;

internal enum PricingDebugReason
{
  MarketBoardRequestFailed,
  DuplicateMarketBoardRequest,
  NoEligibleListings,
  NoCredibleListing,
  OwnPriceAlreadyLowest,
  UndercutCompetitor,
  ThinMarketUseAverage,
  ThinMarketUndercutFloor,
  ThinMarketSkip,
  CachedPrice
}

internal enum ThinMarketPricingReason
{
  FallbackDisabled,
  TooManyListings,
  AverageMissingOrZero,
  NotEnoughRecentSales,
  LatestSaleMissing,
  LatestSaleTooOld,
  EmptyBoardUseAverage,
  FloorMissing,
  FloorOutsideTolerance,
  FloorWithinTolerance
}

internal sealed record PricingDebugDetail(PricingDebugReason Reason)
{
  public int? SelectedPrice { get; init; }

  public int? ListingCount { get; init; }

  public int? FloorPrice { get; init; }

  public int? OwnLowestPrice { get; init; }

  public int? CompetitorPrice { get; init; }

  public ThinMarketAveragePrice? AveragePrice { get; init; }

  public ThinMarketPricingReason? ThinMarketReason { get; init; }

  public int? MinRecentSales { get; init; }

  public int? MaxSaleAgeDays { get; init; }

  public float? TolerancePercent { get; init; }

  public UndercutMode? UndercutMode { get; init; }

  public int? UndercutAmount { get; init; }

  public float? UndercutPercent { get; init; }

  public string? RequestStatus { get; init; }
}

internal readonly record struct PricingDecisionResult(
  int Price,
  PricingDebugDetail DebugDetail);
