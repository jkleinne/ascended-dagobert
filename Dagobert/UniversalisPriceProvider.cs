using Dalamud.Plugin.Services;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Dagobert;

internal interface ISaleReferenceProvider
{
  Task<SaleReference?> GetSaleReferenceAsync(
    uint worldId,
    uint itemId,
    bool isHq,
    int minRecentSales,
    int maxSaleAgeDays,
    DateTimeOffset now,
    CancellationToken cancellationToken);
}

internal sealed class UniversalisPriceProvider(HttpClient httpClient, IPluginLog log) :
  ISaleReferenceProvider
{
  private const string ListingsQueryValue = "0";
  private const int DefaultSaleReferenceEntries = 20;
  private const string RecentHistoryField = "recentHistory";
  private const string HqField = "hq";
  private const string PricePerUnitField = "pricePerUnit";
  private const string TimestampField = "timestamp";

  public async Task<SaleReference?> GetSaleReferenceAsync(
    uint worldId,
    uint itemId,
    bool isHq,
    int minRecentSales,
    int maxSaleAgeDays,
    DateTimeOffset now,
    CancellationToken cancellationToken)
  {
    var entries = Math.Max(DefaultSaleReferenceEntries, minRecentSales);
    var requestUri = string.Format(
      CultureInfo.InvariantCulture,
      "{0}/{1}?listings={2}&entries={3}",
      worldId,
      itemId,
      ListingsQueryValue,
      entries);
    log.Debug(
      "Universalis sale reference request started for item {ItemId} on world {WorldId}, hq {IsHq}, entries {Entries}, minimum sales {MinimumSales}, maximum age days {MaximumAgeDays}",
      itemId,
      worldId,
      isHq,
      entries,
      minRecentSales,
      maxSaleAgeDays);

    try
    {
      using var response = await httpClient.GetAsync(requestUri, cancellationToken).ConfigureAwait(false);
      if (!response.IsSuccessStatusCode)
      {
        log.Warning(
          "Universalis sale reference request failed for item {ItemId} on world {WorldId}: {StatusCode}",
          itemId,
          worldId,
          response.StatusCode);
        return null;
      }

      await using var responseStream = await response.Content
        .ReadAsStreamAsync(cancellationToken)
        .ConfigureAwait(false);
      using var document = await JsonDocument
        .ParseAsync(responseStream, cancellationToken: cancellationToken)
        .ConfigureAwait(false);

      var saleReference = ParseSaleReference(document.RootElement, isHq, minRecentSales, maxSaleAgeDays, now);
      LogSaleReferenceResult(worldId, itemId, isHq, saleReference);
      return saleReference;
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
      return null;
    }
    catch (OperationCanceledException)
    {
      log.Warning("Universalis sale reference request timed out for item {ItemId} on world {WorldId}", itemId, worldId);
      return null;
    }
    catch (Exception ex) when (ex is HttpRequestException or JsonException or NotSupportedException)
    {
      log.Warning(ex, "Universalis sale reference request failed for item {ItemId} on world {WorldId}", itemId, worldId);
      return null;
    }
  }

  private void LogSaleReferenceResult(
    uint worldId,
    uint itemId,
    bool isHq,
    SaleReference? saleReference)
  {
    if (saleReference is null)
    {
      log.Debug(
        "Universalis sale reference request returned no usable sale reference for item {ItemId} on world {WorldId}, hq {IsHq}",
        itemId,
        worldId,
        isHq);
      return;
    }

    log.Debug(
      "Universalis sale reference request parsed item {ItemId} on world {WorldId}, hq {IsHq}, median unit price {MedianUnitPrice}, recent sales {RecentSales}, latest sale {LatestSaleAt}",
      itemId,
      worldId,
      isHq,
      saleReference.Value.MedianUnitPrice,
      saleReference.Value.RecentHistoryCount,
      saleReference.Value.LatestSaleAt);
  }

  private static SaleReference? ParseSaleReference(
    JsonElement root,
    bool isHq,
    int minRecentSales,
    int maxSaleAgeDays,
    DateTimeOffset now)
  {
    if (root.ValueKind != JsonValueKind.Object ||
        !root.TryGetProperty(RecentHistoryField, out var recentHistory) ||
        recentHistory.ValueKind != JsonValueKind.Array)
      return null;

    var prices = new List<uint>();
    DateTimeOffset? latestSaleAt = null;
    foreach (var sale in recentHistory.EnumerateArray())
    {
      if (sale.ValueKind != JsonValueKind.Object)
        continue;

      if (!IsMatchingQualitySale(sale, isHq))
        continue;

      if (!TryGetPositiveUInt(sale, PricePerUnitField, out var pricePerUnit))
        continue;

      if (!sale.TryGetProperty(TimestampField, out var timestamp) ||
          !timestamp.TryGetInt64(out var unixTimestamp))
        continue;

      DateTimeOffset saleAt;
      try
      {
        saleAt = DateTimeOffset.FromUnixTimeSeconds(unixTimestamp);
      }
      catch (ArgumentOutOfRangeException)
      {
        continue;
      }

      prices.Add(pricePerUnit);
      if (latestSaleAt is null || saleAt > latestSaleAt.Value)
        latestSaleAt = saleAt;
    }

    if (prices.Count < Math.Max(1, minRecentSales) || latestSaleAt is null)
      return null;

    if (latestSaleAt.Value < now - TimeSpan.FromDays(Math.Max(1, maxSaleAgeDays)))
      return null;

    prices.Sort();
    var median = prices[(prices.Count - 1) / 2];
    return new SaleReference(median, prices.Count, latestSaleAt.Value);
  }

  private static bool IsMatchingQualitySale(JsonElement sale, bool isHq)
  {
    if (sale.ValueKind != JsonValueKind.Object)
      return false;

    if (!sale.TryGetProperty(HqField, out var hq) ||
        hq.ValueKind is not JsonValueKind.True and not JsonValueKind.False)
      return false;

    return hq.GetBoolean() == isHq;
  }

  private static bool TryGetPositiveUInt(JsonElement root, string propertyName, out uint value)
  {
    value = 0;
    if (!root.TryGetProperty(propertyName, out var property))
      return false;

    if (property.ValueKind == JsonValueKind.Number &&
        property.TryGetUInt32(out var unsignedValue) &&
        unsignedValue > 0)
    {
      value = unsignedValue;
      return true;
    }

    return false;
  }
}
