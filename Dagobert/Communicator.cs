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

  private const ushort RetainerNameHighlightColor = 561;

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

    var debugText = PricingMessageFormatter.FormatPricingDebug(debugDetail, DateTimeOffset.UtcNow);
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

  public static void PrintManualPriceRespected(string itemName, int keptPrice)
  {
    if (!Plugin.Configuration.ShowPriceAdjustmentsMessages)
      return;

    PrintItemInfo(itemName, $"Manual price of {keptPrice:N0} gil respected; finish and confirm it manually");
  }

  public static void PrintManualEditSkipped(string itemName)
  {
    if (!Plugin.Configuration.ShowErrorsInChat)
      return;

    PrintItemError(itemName, "Item skipped because its price field was edited during the run; the listing keeps its previous price");
  }

  public static void PrintAboveMaxRaiseError(string itemName)
  {
    if (!Plugin.Configuration.ShowErrorsInChat)
      return;

    PrintItemError(itemName, $"Item ignored because it would raise the price by more than {Plugin.Configuration.MaxRaisePercentage}%");
  }

  private static SeString? BuildItemLinkMessage(string itemName, string message)
  {
    var itemPayload = RawItemNameToItemPayload(itemName);
    if (itemPayload == null)
      return null;

    return new SeStringBuilder()
        .AddItemLink(itemPayload.ItemId, itemPayload.IsHQ)
        .AddText($": {message}")
        .Build();
  }

  private static void PrintItemInfo(string itemName, string message)
  {
    var seString = BuildItemLinkMessage(itemName, message);
    if (seString != null)
      Svc.Chat.Print(seString);
    else
      Svc.Chat.Print($"{itemName}: {message}");
  }

  private static void PrintItemError(string itemName, string message)
  {
    var seString = BuildItemLinkMessage(itemName, message);
    if (seString != null)
      Svc.Chat.PrintError(seString);
    else
      Svc.Chat.PrintError($"{itemName}: {message}");
  }

  public static void PrintRetainerName(string name)
  {
    if (!Plugin.Configuration.ShowRetainerNames)
      return;

    var seString = new SeStringBuilder()
        .AddText("Now Pinching items of retainer: ")
        .AddUiForeground(name, RetainerNameHighlightColor)
        .Build();
    Svc.Chat.Print(seString);
  }

  public static void PrintRecentlyPinchedSkipped(IReadOnlyList<string> skippedRetainerNames)
  {
    if (skippedRetainerNames.Count == 0)
      return;

    var builder = new SeStringBuilder()
        .AddText($"Auto pinch: skipping {skippedRetainerNames.Count} recently pinched retainer{(skippedRetainerNames.Count == 1 ? "" : "s")}");

    if (Plugin.Configuration.ShowRetainerNames)
    {
      builder.AddText(": ");
      for (var i = 0; i < skippedRetainerNames.Count; i++)
      {
        if (i > 0)
          builder.AddText(", ");
        builder.AddUiForeground(skippedRetainerNames[i], RetainerNameHighlightColor);
      }
    }

    Svc.Chat.Print(builder.Build());
  }

  public static void PrintNoPriceToSetError(string itemName)
  {
    PrintNoPriceToSetError(itemName, null);
  }

  internal static void PrintNoPriceToSetError(string itemName, PricingDebugDetail? debugDetail)
  {
    if (!Plugin.Configuration.ShowErrorsInChat)
      return;

    var reason = PricingMessageFormatter.FormatNoPriceReason(debugDetail, DateTimeOffset.UtcNow);
    var message = $"No price set: {reason}. Please set price manually.";
    var itemPayload = RawItemNameToItemPayload(itemName);
    if (itemPayload != null)
    {
      var seString = new SeStringBuilder()
          .AddItemLink(itemPayload.ItemId, itemPayload.IsHQ)
          .AddText($": {message}")
          .Build();

      Svc.Chat.PrintError(seString);
    }
    else
      Svc.Chat.PrintError($"{itemName}: {message}");
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
