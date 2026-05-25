using System;

namespace Dagobert;

internal readonly record struct SaleReference(
  uint MedianUnitPrice,
  int RecentHistoryCount,
  DateTimeOffset LatestSaleAt);
