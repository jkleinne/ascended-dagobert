namespace Dagobert;

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
