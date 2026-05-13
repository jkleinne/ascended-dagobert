using Dagobert;
using Dalamud.Plugin.Services;
using System.Net;
using System.Text;

namespace Dagobert.Tests;

internal static class Program
{
  public static async Task<int> Main()
  {
    var tests = new (string Name, Func<Task> Run)[]
    {
      ("NQ average uses only NQ history rows", NqAverageUsesOnlyNqHistoryRows),
      ("HQ average uses only HQ history rows", HqAverageUsesOnlyHqHistoryRows),
      ("Bait guard keeps listing-only target without sale reference", BaitGuardKeepsListingOnlyTargetWithoutSaleReference),
      ("Bait guard skips tiny cluster below sale median floor", BaitGuardSkipsTinyClusterBelowSaleMedianFloor),
      ("Bait guard accepts below-floor cluster with enough listings", BaitGuardAcceptsBelowFloorClusterWithEnoughListings),
      ("Bait guard accepts below-floor cluster with enough quantity", BaitGuardAcceptsBelowFloorClusterWithEnoughQuantity)
    };

    var failures = 0;
    foreach (var test in tests)
    {
      try
      {
        await test.Run();
        Console.WriteLine($"PASS {test.Name}");
      }
      catch (Exception ex)
      {
        failures++;
        Console.WriteLine($"FAIL {test.Name}: {ex.Message}");
      }
    }

    return failures == 0 ? 0 : 1;
  }

  private static async Task NqAverageUsesOnlyNqHistoryRows()
  {
    const long newestHqSale = 1778591298;
    const long onlyNqSale = 1777327167;
    const long olderHqSale = 1777327171;
    var provider = CreateProvider(
      $$"""
      {
        "averagePriceNQ": 8199,
        "averagePriceHQ": 9536,
        "recentHistory": [
          { "hq": true, "pricePerUnit": 9536, "timestamp": {{newestHqSale}} },
          { "hq": false, "pricePerUnit": 8199, "timestamp": {{onlyNqSale}} },
          { "hq": true, "pricePerUnit": 1000, "timestamp": {{olderHqSale}} }
        ]
      }
      """);

    var average = await provider.GetAverageSalePriceAsync(34, 3920, false, 3, CancellationToken.None);

    var price = average ?? throw new InvalidOperationException("expected an NQ average");
    AssertEqual((uint)8199, price.UnitPrice, "NQ average price");
    AssertEqual(1, price.RecentHistoryCount, "NQ recent history count");
    AssertEqual(DateTimeOffset.FromUnixTimeSeconds(onlyNqSale), price.LatestSaleAt, "NQ latest sale");
  }

  private static async Task HqAverageUsesOnlyHqHistoryRows()
  {
    const long newestNqSale = 1778600000;
    const long newestHqSale = 1778500000;
    const long olderHqSale = 1778400000;
    var provider = CreateProvider(
      $$"""
      {
        "averagePriceNQ": 1000,
        "averagePriceHQ": 2500,
        "recentHistory": [
          { "hq": false, "pricePerUnit": 1000, "timestamp": {{newestNqSale}} },
          { "hq": true, "pricePerUnit": 2500, "timestamp": {{newestHqSale}} },
          { "hq": true, "pricePerUnit": 2000, "timestamp": {{olderHqSale}} }
        ]
      }
      """);

    var average = await provider.GetAverageSalePriceAsync(34, 3920, true, 3, CancellationToken.None);

    var price = average ?? throw new InvalidOperationException("expected an HQ average");
    AssertEqual((uint)2500, price.UnitPrice, "HQ average price");
    AssertEqual(2, price.RecentHistoryCount, "HQ recent history count");
    AssertEqual(DateTimeOffset.FromUnixTimeSeconds(newestHqSale), price.LatestSaleAt, "HQ latest sale");
  }

  private static Task BaitGuardKeepsListingOnlyTargetWithoutSaleReference()
  {
    var listings = CreateListings((100u, 1u), (200u, 1u), (300u, 1u));
    var target = BaitGuard.SelectTargetIndex(listings, [0, 1, 2], DefaultBaitOptions(), null);

    AssertEqual<int?>(0, target, "listing-only target");
    return Task.CompletedTask;
  }

  private static Task BaitGuardSkipsTinyClusterBelowSaleMedianFloor()
  {
    var listings = CreateListings((100u, 1u), (105u, 1u), (300u, 1u));
    var saleReference = new RecentSaleReference(300, 3, DateTimeOffset.UtcNow);
    var target = BaitGuard.SelectTargetIndex(listings, [0, 1, 2], DefaultBaitOptions(), saleReference);

    AssertEqual<int?>(2, target, "tiny low cluster target");
    return Task.CompletedTask;
  }

  private static Task BaitGuardAcceptsBelowFloorClusterWithEnoughListings()
  {
    var listings = CreateListings((100u, 1u), (103u, 1u), (105u, 1u), (300u, 1u));
    var saleReference = new RecentSaleReference(300, 3, DateTimeOffset.UtcNow);
    var target = BaitGuard.SelectTargetIndex(listings, [0, 1, 2, 3], DefaultBaitOptions(), saleReference);

    AssertEqual<int?>(0, target, "listing-backed low cluster target");
    return Task.CompletedTask;
  }

  private static Task BaitGuardAcceptsBelowFloorClusterWithEnoughQuantity()
  {
    var listings = CreateListings((100u, 9u), (104u, 12u), (300u, 1u));
    var saleReference = new RecentSaleReference(300, 3, DateTimeOffset.UtcNow);
    var target = BaitGuard.SelectTargetIndex(listings, [0, 1, 2], DefaultBaitOptions(), saleReference);

    AssertEqual<int?>(0, target, "quantity-backed low cluster target");
    return Task.CompletedTask;
  }

  private static BaitGuard.Options DefaultBaitOptions() => new(
    Enabled: true,
    FloorPercent: 30.0f,
    SampleListings: 5,
    GapPercent: 50.0f,
    MinQuantity: 1,
    SaleMedianFloorPercent: 50.0f,
    LowClusterListings: 3,
    LowClusterQuantity: 20,
    LowClusterPriceTolerancePercent: 5.0f);

  private static List<TestMarketBoardItemListing> CreateListings(
    params (uint PricePerUnit, uint Quantity)[] listings)
  {
    return listings
      .Select(listing => new TestMarketBoardItemListing(listing.PricePerUnit, listing.Quantity))
      .ToList();
  }

  private static UniversalisAveragePriceProvider CreateProvider(string json)
  {
    var response = new HttpResponseMessage(HttpStatusCode.OK)
    {
      Content = new StringContent(json, Encoding.UTF8, "application/json")
    };
    var httpClient = new HttpClient(new StaticResponseHandler(response))
    {
      BaseAddress = new Uri("https://universalis.test/api/v2/")
    };

    return new UniversalisAveragePriceProvider(httpClient, new TestPluginLog());
  }

  private static void AssertEqual<T>(T expected, T actual, string label)
  {
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
      throw new InvalidOperationException($"{label}: expected {expected}, got {actual}");
  }
}

internal sealed class StaticResponseHandler(HttpResponseMessage response) : HttpMessageHandler
{
  protected override Task<HttpResponseMessage> SendAsync(
    HttpRequestMessage request,
    CancellationToken cancellationToken)
  {
    return Task.FromResult(response);
  }
}

internal sealed record TestMarketBoardItemListing(
  uint PricePerUnit,
  uint ItemQuantity) : Dalamud.Game.Network.Structures.IMarketBoardItemListing;

internal sealed class TestPluginLog : IPluginLog
{
  public void Warning(string messageTemplate, params object?[] propertyValues)
  {
  }

  public void Warning(Exception exception, string messageTemplate, params object?[] propertyValues)
  {
  }
}
