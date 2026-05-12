using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using ECommons.DalamudServices;
using Lumina.Excel;
using Lumina.Excel.Sheets;

namespace Dagobert;

public static class Communicator
{
  private static readonly ExcelSheet<Item> ItemSheet = Svc.Data.GetExcelSheet<Item>();

  public static void PrintPriceUpdate(string itemName, int? oldPrice, int? newPrice, float cutPercentage)
  {
    if (!Plugin.Configuration.ShowPriceAdjustmentsMessages)
      return;

    if (oldPrice == null || newPrice == null || oldPrice.Value == newPrice.Value)
      return;

    var dec = oldPrice.Value > newPrice.Value ? "cut" : "increase";
    var itemPayload = RawItemNameToItemPayload(itemName);

    if (itemPayload != null)
    {
      var seString = new SeStringBuilder()
          .AddItemLink(itemPayload.ItemId, itemPayload.IsHQ)
          .AddText($": Pinching from {oldPrice.Value:N0} to {newPrice.Value:N0} gil, a {dec} of {MathF.Abs(MathF.Round(cutPercentage, 2))}%")
          .Build();

      Svc.Chat.Print(seString);
    }
    else
      Svc.Chat.Print($"{itemName}: Pinching from {oldPrice.Value:N0} to {newPrice.Value:N0}, a {dec} of {MathF.Abs(MathF.Round(cutPercentage, 2))}%");
  }

  internal static void PrintPricingDebug(string itemName, PricingDebugDetail? debugDetail)
  {
    if (!Plugin.Configuration.ShowPricingDebug || debugDetail is null)
      return;

    var debugText = FormatPricingDebug(debugDetail);
    Svc.Log.Debug($"{itemName}: Pricing debug: {debugText}");

    var itemPayload = RawItemNameToItemPayload(itemName);
    if (itemPayload != null)
    {
      var seString = new SeStringBuilder()
          .AddItemLink(itemPayload.ItemId, itemPayload.IsHQ)
          .AddText($": Pricing debug: {debugText}")
          .Build();

      Svc.Chat.Print(seString);
    }
    else
      Svc.Chat.Print($"{itemName}: Pricing debug: {debugText}");
  }

  private static string FormatPricingDebug(PricingDebugDetail debugDetail)
  {
    return debugDetail.Reason switch
    {
      PricingDebugReason.MarketBoardRequestFailed => $"market board request failed with status {debugDetail.RequestStatus ?? "unknown"}",
      PricingDebugReason.DuplicateMarketBoardRequest => "ignored duplicate market board response",
      PricingDebugReason.NoEligibleListings => $"no eligible market board listings were found; {FormatListingCount(debugDetail)}",
      PricingDebugReason.NoCredibleListing => $"skipped because bait guard found no credible listing; {FormatListingCount(debugDetail)}",
      PricingDebugReason.OwnPriceAlreadyLowest => FormatOwnPriceDebug(debugDetail),
      PricingDebugReason.UndercutCompetitor => FormatUndercutDebug(debugDetail),
      PricingDebugReason.ThinMarketUseAverage => FormatThinMarketUseAverage(debugDetail),
      PricingDebugReason.ThinMarketUndercutFloor => FormatThinMarketUndercutFloor(debugDetail),
      PricingDebugReason.ThinMarketSkip => FormatThinMarketSkip(debugDetail),
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

  private static string FormatThinMarketUseAverage(PricingDebugDetail debugDetail)
  {
    return $"thin market used Universalis average {FormatAveragePrice(debugDetail)} gil as the price; {FormatThinContext(debugDetail)}";
  }

  private static string FormatThinMarketUndercutFloor(PricingDebugDetail debugDetail)
  {
    return $"thin market checked Universalis average {FormatAveragePrice(debugDetail)} gil and undercut floor {FormatGil(debugDetail.FloorPrice)} gil to {FormatGil(debugDetail.SelectedPrice)} gil; {FormatThinContext(debugDetail)}";
  }

  private static string FormatThinMarketSkip(PricingDebugDetail debugDetail)
  {
    var reason = debugDetail.ThinMarketReason switch
    {
      ThinMarketPricingReason.FallbackDisabled => "average fallback is disabled",
      ThinMarketPricingReason.TooManyListings => "listing count is above the thin market limit",
      ThinMarketPricingReason.AverageMissingOrZero => "Universalis returned no positive average",
      ThinMarketPricingReason.NotEnoughRecentSales => $"Universalis returned {FormatRecentSalesCount(debugDetail)} recent sales, minimum is {Math.Max(1, debugDetail.MinRecentSales ?? 1)}",
      ThinMarketPricingReason.LatestSaleMissing => "Universalis did not return a timestamped recent sale",
      ThinMarketPricingReason.LatestSaleTooOld => $"newest Universalis sale is {FormatLatestSaleAge(debugDetail.AveragePrice?.LatestSaleAt)} and max age is {Math.Max(1, debugDetail.MaxSaleAgeDays ?? 1)} days",
      ThinMarketPricingReason.FloorMissing => "there is no competitor floor",
      ThinMarketPricingReason.FloorOutsideTolerance => $"floor {FormatGil(debugDetail.FloorPrice)} gil is outside {FormatPercent(debugDetail.TolerancePercent)}% tolerance of Universalis average {FormatAveragePrice(debugDetail)} gil",
      _ => "thin market policy skipped the item"
    };

    return $"thin market skipped because {reason}; {FormatThinContext(debugDetail)}";
  }

  private static string FormatThinContext(PricingDebugDetail debugDetail)
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
      parts.Add($"newest sale {FormatLatestSaleAge(debugDetail.AveragePrice.Value.LatestSaleAt)}");
    }

    return string.Join(", ", parts);
  }

  private static string FormatUndercutMode(PricingDebugDetail debugDetail)
  {
    if (debugDetail.UndercutMode == UndercutMode.Percentage)
      return $"{FormatPercent(debugDetail.UndercutPercent)}% undercut";

    return $"{Math.Max(1, debugDetail.UndercutAmount ?? 1):N0} gil undercut";
  }

  private static string FormatListingCount(PricingDebugDetail debugDetail)
  {
    return $"listings {Math.Max(0, debugDetail.ListingCount ?? 0)}";
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
      : debugDetail.AveragePrice.Value.RecentHistoryCount.ToString();
  }

  private static string FormatLatestSaleAge(DateTimeOffset? latestSaleAt)
  {
    if (latestSaleAt is null)
      return "unknown";

    var age = DateTimeOffset.UtcNow - latestSaleAt.Value;
    if (age < TimeSpan.Zero)
      return "in the future";

    if (age.TotalDays >= 1)
      return $"{(int)age.TotalDays} days ago";

    if (age.TotalHours >= 1)
      return $"{(int)age.TotalHours} hours ago";

    return $"{Math.Max(0, (int)age.TotalMinutes)} minutes ago";
  }

  private static string FormatGil(int? gil)
  {
    return gil is null ? "unknown" : $"{gil.Value:N0}";
  }

  private static string FormatPercent(float? percent)
  {
    return percent is null ? "unknown" : $"{percent.Value:0.#}";
  }

  private static ItemPayload? RawItemNameToItemPayload(string itemName)
  {
    // Parse as SeString
    var seString = SeString.Parse(Encoding.UTF8.GetBytes(itemName));

    // Find all text payloads
    var textPayloads = seString.Payloads
        .OfType<TextPayload>()
        .ToList();

    if (textPayloads.Count == 0)
      return null;

    var cleanedName = "";
    var isHq = false;

    if (textPayloads.Count == 1)
    {
      // Single text payload - just trim it
      cleanedName = textPayloads[0].Text?.Trim();
    }
    else if (textPayloads.Count >= 2)
    {
      // Skip the first payload (it's always just "%" with ETX)
      // Concatenate payloads starting from index 1
      var nameParts = new StringBuilder();

      for (int i = 1; i < textPayloads.Count; i++)
      {
        var text = textPayloads[i].Text;

        // First payload after the initial marker has a prefix: ANY_CHAR + ETX (U+0003)
        if (i == 1 && text?.Length >= 2 && text[1] == '\u0003')
          text = text[2..];

        nameParts.Append(text);
      }

      cleanedName = nameParts.ToString();

      // Check and clean HQ symbol at the very end
      if (cleanedName.Length >= 1 && cleanedName[^1] == '\uE03C')
      {
        isHq = true;
        cleanedName = cleanedName[..^1].TrimEnd();
      }
      else
        cleanedName = cleanedName.TrimEnd();
    }

    // Search for the item
    var item = ItemSheet.FirstOrDefault(i =>
        i.Name.ToString().Equals(cleanedName, StringComparison.OrdinalIgnoreCase));

    if (item.RowId > 0)
    {
      var itemPayloadResult = new ItemPayload(item.RowId, isHq);
      return itemPayloadResult;
    }

    return null;
  }

  public static void PrintAboveMaxCutError(string itemName)
  {
    if (!Plugin.Configuration.ShowErrorsInChat)
      return;

    var itemPayload = RawItemNameToItemPayload(itemName);

    if (itemPayload != null)
    {
      var seString = new SeStringBuilder()
          .AddItemLink(itemPayload.ItemId, itemPayload.IsHQ)
          .AddText($": Item ignored because it would cut the price by more than {Plugin.Configuration.MaxUndercutPercentage}%")
          .Build();

      Svc.Chat.PrintError(seString);
    }
    else
      Svc.Chat.PrintError($"{itemName}: Item ignored because it would cut the price by more than {Plugin.Configuration.MaxUndercutPercentage}%");
  }

  public static void PrintRetainerName(string name)
  {
    if (!Plugin.Configuration.ShowRetainerNames)
      return;

    var seString = new SeStringBuilder()
        .AddText("Now Pinching items of retainer: ")
        .AddUiForeground(name, 561)
        .Build();
    Svc.Chat.Print(seString);
  }

  public static void PrintNoPriceToSetError(string itemName)
  {
    if (!Plugin.Configuration.ShowErrorsInChat)
      return;

    var itemPayload = RawItemNameToItemPayload(itemName);
    if (itemPayload != null)
    {
      var seString = new SeStringBuilder()
          .AddItemLink(itemPayload.ItemId, itemPayload.IsHQ)
          .AddText($": No price to set, please set price manually")
          .Build();

      Svc.Chat.PrintError(seString);
    }
    else
      Svc.Chat.PrintError($"{itemName}: No price to set, please set price manually");
  }

    public static void PrintAllRetainersDisabled()
    {
        var seString = new SeStringBuilder()
            .AddText("All retainers are disabled. Open configuration with ")
            .Add(Plugin.ConfigLinkPayload)
            .AddUiForeground("/dagobert", 31) // Bright yellow color for better visibility
            .Build();
        
        Svc.Chat.PrintError(seString);
    }
}
