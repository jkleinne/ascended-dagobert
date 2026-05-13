using Dalamud.Plugin.Services;
using System;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Dagobert;

internal interface IAverageSalePriceProvider
{
  Task<ThinMarketAveragePrice?> GetAverageSalePriceAsync(
    uint worldId,
    uint itemId,
    bool isHq,
    int recentHistoryLimit,
    CancellationToken cancellationToken);
}

internal sealed class UniversalisAveragePriceProvider(HttpClient httpClient, IPluginLog log) : IAverageSalePriceProvider
{
  private const string ListingsQueryValue = "0";
  private const string AveragePriceNqField = "averagePriceNQ";
  private const string AveragePriceHqField = "averagePriceHQ";
  private const string RecentHistoryField = "recentHistory";
  private const string HqField = "hq";
  private const string TimestampField = "timestamp";

  public async Task<ThinMarketAveragePrice?> GetAverageSalePriceAsync(
    uint worldId,
    uint itemId,
    bool isHq,
    int recentHistoryLimit,
    CancellationToken cancellationToken)
  {
    var entries = Math.Max(1, recentHistoryLimit);
    var requestUri = string.Format(
      CultureInfo.InvariantCulture,
      "{0}/{1}?listings={2}&entries={3}",
      worldId,
      itemId,
      ListingsQueryValue,
      entries);

    try
    {
      using var response = await httpClient.GetAsync(requestUri, cancellationToken).ConfigureAwait(false);
      if (!response.IsSuccessStatusCode)
      {
        log.Warning(
          "Universalis average price request failed for item {ItemId} on world {WorldId}: {StatusCode}",
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

      return ParseAveragePrice(document.RootElement, isHq);
    }
    catch (OperationCanceledException)
    {
      log.Warning("Universalis average price request timed out for item {ItemId} on world {WorldId}", itemId, worldId);
      return null;
    }
    catch (Exception ex) when (ex is HttpRequestException or JsonException or NotSupportedException)
    {
      log.Warning(ex, "Universalis average price request failed for item {ItemId} on world {WorldId}", itemId, worldId);
      return null;
    }
  }

  private static ThinMarketAveragePrice? ParseAveragePrice(JsonElement root, bool isHq)
  {
    var averagePriceField = isHq ? AveragePriceHqField : AveragePriceNqField;
    if (!TryGetPositiveUInt(root, averagePriceField, out var unitPrice))
      return null;

    var recentHistoryCount = 0;
    DateTimeOffset? latestSaleAt = null;
    if (root.TryGetProperty(RecentHistoryField, out var recentHistory) &&
        recentHistory.ValueKind == JsonValueKind.Array)
    {
      foreach (var sale in recentHistory.EnumerateArray())
      {
        if (!IsMatchingQualitySale(sale, isHq))
          continue;

        recentHistoryCount++;
        if (!sale.TryGetProperty(TimestampField, out var timestamp) ||
            !timestamp.TryGetInt64(out var unixTimestamp))
          continue;

        var saleTime = DateTimeOffset.FromUnixTimeSeconds(unixTimestamp);
        if (latestSaleAt is null || saleTime > latestSaleAt.Value)
          latestSaleAt = saleTime;
      }
    }

    return new ThinMarketAveragePrice(unitPrice, recentHistoryCount, latestSaleAt);
  }

  private static bool IsMatchingQualitySale(JsonElement sale, bool isHq)
  {
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

    if (property.ValueKind == JsonValueKind.Number && property.TryGetDecimal(out var decimalValue))
    {
      var rounded = decimal.Round(decimalValue, 0, MidpointRounding.AwayFromZero);
      if (rounded > 0 && rounded <= uint.MaxValue)
      {
        value = (uint)rounded;
        return true;
      }
    }

    return false;
  }
}
