namespace Dagobert;

internal enum PricingDebugReason
{
  MarketBoardRequestFailed,
  DuplicateMarketBoardRequest,
  NoEligibleListings,
  NoCredibleListing,
  OwnPriceAlreadyLowest,
  UndercutCompetitor,
  ThinMarketUseReference,
  ThinMarketUndercutFloor,
  ThinMarketSkip,
  CachedPrice
}

internal enum ThinMarketPricingReason
{
  FallbackDisabled,
  TooManyListings,
  SaleReferenceMissingOrZero,
  NotEnoughRecentSales,
  LatestSaleTooOld,
  EmptyBoardUseReference,
  OwnPriceOutsideTolerance,
  OwnPriceWithinTolerance,
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

  public SaleReference? SaleReference { get; init; }

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
