using Dalamud.Game.Addon.Events;
using Dalamud.Game.Addon.Events.EventDataTypes;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.ClientState.Keys;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using ECommons;
using ECommons.Automation;
using ECommons.Automation.LegacyTaskManager;
using ECommons.DalamudServices;
using ECommons.ImGuiMethods;
using ECommons.UIHelpers.AddonMasterImplementations;
using ECommons.UIHelpers.AtkReaderImplementations;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Common.Math;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Dalamud.Bindings.ImGui;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Speech.Synthesis;
using Dalamud.Game.Text.SeStringHandling;
using static ECommons.UIHelpers.AtkReaderImplementations.ReaderContextMenu;
// Type alias rather than a namespace using: Lumina.Excel.Sheets also exports common
// identifiers (Action, Item, ...) that collide with System types used in this file.
using Addon = Lumina.Excel.Sheets.Addon;

namespace Dagobert
{
  internal sealed class AutoPinch : Window, IDisposable
  {
    private readonly MarketBoardHandler _mbHandler;
    private int? _oldPrice;
    private int? _newPrice;
    private PricingDebugDetail? _pricingDebugDetail;
    private bool _skipCurrentItem = false;
    private bool _loggedMarketPriceWait;
    private readonly TaskManager _taskManager;
    private readonly AutoRetainerSuppressionCoordinator _autoRetainerSuppressionCoordinator;
    private readonly AutoPinchTaskGuard _autoPinchTaskGuard;
    private readonly RecentPinchTracker _recentPinchTracker;
    private readonly Func<long> _getTickCount;
    private bool _hasTalkAddonListeners;
    private Dictionary<string, int?> _cachedPrices = [];
    private const uint ComparePricesButtonNodeId = 4;
    private const int ComparePricesCallbackId = 4;
    private const int LegacyDirectTaskTimeoutMs = 10_000;
    private const int SaleHistoryDwellMs = 500;
    private const int SaleHistoryUiSettleDelayMs = 100;
    private const uint ViewSaleHistoryAddonRowId = 2382;
    private const string RetainerHistoryAddonName = "RetainerHistory";
    private const uint SellItemsPlayerInventoryAddonRowId = 2380;
    private const uint SellItemsRetainerInventoryAddonRowId = 2381;
    private const int FallbackSellItemsEntryIndex = 2;
    private readonly IReadOnlyList<string> _sellItemsLabels;
    private readonly string? _saleHistoryLabel;

    public AutoPinch(
      ISaleReferenceProvider saleReferenceProvider,
      MarketBoardRequestTracker marketBoardRequestTracker,
      AutoRetainerSuppressionCoordinator autoRetainerSuppressionCoordinator,
      RecentPinchTracker recentPinchTracker)
      : base("Ascended Dagobert", ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.AlwaysUseWindowPadding | ImGuiWindowFlags.AlwaysAutoResize, true)
    {
      _mbHandler = new MarketBoardHandler(
        saleReferenceProvider,
        marketBoardRequestTracker);
      _mbHandler.NewPriceReceived += MBHandler_NewPriceReceived;

      // window
      Position = new System.Numerics.Vector2(0, 0);
      IsOpen = true;
      ShowCloseButton = false;
      RespectCloseHotkey = false;
      DisableWindowSounds = true;
      SizeConstraints = new WindowSizeConstraints()
      {
        MaximumSize = new System.Numerics.Vector2(0, 0),
      };

      _taskManager = new TaskManager
      {
        TimeLimitMS = LegacyDirectTaskTimeoutMs,
        AbortOnTimeout = true
      };
      _autoRetainerSuppressionCoordinator = autoRetainerSuppressionCoordinator;
      _recentPinchTracker = recentPinchTracker;
      _getTickCount = () => Environment.TickCount64;
      _autoPinchTaskGuard = new AutoPinchTaskGuard(
        autoRetainerSuppressionCoordinator,
        () => _taskManager.Abort(),
        RemoveTalkAddonListeners,
        LogAutoPinchTaskException,
        LogAutoPinchTaskTimeout,
        _getTickCount);
      _saleHistoryLabel = ResolveAddonSheetText(ViewSaleHistoryAddonRowId);
      if (_saleHistoryLabel is null)
        Svc.Log.Warning(
          "Could not resolve the sale history menu label from Addon sheet row {RowId}; sale history visits are disabled this session",
          ViewSaleHistoryAddonRowId);
      _sellItemsLabels = ResolveSellItemsLabels();
      if (_sellItemsLabels.Count == 0)
        Svc.Log.Warning(
          "Could not resolve the sell items menu label from Addon sheet rows {PlayerInventoryRowId} and {RetainerInventoryRowId}; falling back to the fixed menu position",
          SellItemsPlayerInventoryAddonRowId,
          SellItemsRetainerInventoryAddonRowId);
      // Fails on non-windows
      try
      {
        var tts = new SpeechSynthesizer();
        tts.SelectVoice(tts.Voice.Name);
        Plugin.Configuration.DontUseTTS = false;
        Plugin.Configuration.Save();
      }
      catch
      {
        Plugin.Configuration.DontUseTTS = true;
        Plugin.Configuration.Save();
      }

      Svc.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, "RetainerSell", RetainerSellPostSetup);
    }

    public void Dispose()
    {
      ExecuteCleanupActions(AutoPinchCleanupPlan.PlanDisposeActions(
        _autoRetainerSuppressionCoordinator.HasActiveSession,
        _hasTalkAddonListeners));
      Svc.AddonLifecycle.UnregisterListener(AddonEvent.PostSetup, "RetainerSell", RetainerSellPostSetup);
      _mbHandler.NewPriceReceived -= MBHandler_NewPriceReceived;
      _mbHandler.Dispose();
    }

    public override void Draw()
    {
      try
      {
        DrawForRetainerList();
        DrawForRetainerSellList();
      }
      catch (Exception ex)
      {
        ExecuteCleanupActions(AutoPinchCleanupPlan.PlanDrawCatchActions(_autoRetainerSuppressionCoordinator.HasActiveSession), ex);
      }

      ExecuteCleanupActions(AutoPinchCleanupPlan.PlanIdleActions(
        _autoRetainerSuppressionCoordinator.HasActiveSession,
        _hasTalkAddonListeners,
        _taskManager.IsBusy));
    }

    private void DrawForRetainerList()
    {
      unsafe
      {
        if (GenericHelpers.TryGetAddonByName<AtkUnitBase>("RetainerList", out var addon) && GenericHelpers.IsAddonReady(addon))
        {
          if (Plugin.Configuration.EnablePinchKey && Plugin.KeyState[Plugin.Configuration.PinchKey])
            PinchAllRetainers();

          var node = addon->UldManager.NodeList[27];

          if (node == null)
            return;

          var oldSize = ImGuiSetup(node);
          DrawAutoPinchButton(PinchAllRetainers);
          ImGuiPostSetup(oldSize);
        }
      }
    }

    private void DrawForRetainerSellList()
    {
      unsafe
      {
        if (GenericHelpers.TryGetAddonByName<AtkUnitBase>("RetainerSellList", out var addon) && GenericHelpers.IsAddonReady(addon))
        {
          if (Plugin.Configuration.EnablePinchKey && Plugin.KeyState[Plugin.Configuration.PinchKey])
            PinchAllRetainerItems();

          var node = addon->UldManager.NodeList[17];

          if (node == null)
            return;

          var oldSize = ImGuiSetup(node);
          DrawAutoPinchButton(PinchAllRetainerItems);
          ImGuiPostSetup(oldSize);
        }
      }
    }

    private unsafe float ImGuiSetup(AtkResNode* node)
    {
      var position = GetNodePosition(node);
      var scale = GetNodeScale(node);
      var size = new Vector2(node->Width, node->Height) * scale;

      ImGuiHelpers.ForceNextWindowMainViewport();
      ImGuiHelpers.SetNextWindowPosRelativeMainViewport(position);

      ImGui.PushStyleColor(ImGuiCol.WindowBg, 0);
      var oldSize = ImGui.GetFont().Scale;
      ImGui.GetFont().Scale *= scale.X;
      ImGui.PushFont(ImGui.GetFont());
      ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 0f.Scale());
      ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(3f.Scale(), 3f.Scale()));
      ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(0f.Scale(), 0f.Scale()));
      ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f.Scale());
      ImGui.PushStyleVar(ImGuiStyleVar.WindowMinSize, size);
      ImGui.Begin($"###AutoPinch{node->NodeId}", ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoNavFocus
          | ImGuiWindowFlags.AlwaysUseWindowPadding | ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoSavedSettings);

      return oldSize;
    }

    private static void ImGuiPostSetup(float oldSize)
    {
      ImGui.End();
      ImGui.PopStyleVar(5);
      ImGui.GetFont().Scale = oldSize;
      ImGui.PopFont();
      ImGui.PopStyleColor();
    }

    private void DrawAutoPinchButton(Action specificPinchFunction)
    {
      if (_taskManager.IsBusy)
      {
        if (ImGui.Button("Cancel"))
        {
          ExecuteCleanupActions(AutoPinchCleanupPlan.PlanCancelActions(_autoRetainerSuppressionCoordinator.HasActiveSession));
        }
        if (ImGui.IsItemHovered())
        {
          ImGui.BeginTooltip();
          ImGui.SetTooltip("Cancels the auto pinching process");
          ImGui.EndTooltip();
        }
      }
      else
      {
        if (ImGui.Button("Auto Pinch"))
          specificPinchFunction();
        if (ImGui.IsItemHovered())
        {
          ImGui.BeginTooltip();
          ImGui.SetTooltip("Starts auto pinching\r\n" +
                           "Please do not interact with the game while this process is running");
          ImGui.EndTooltip();
        }
      }
    }

    private unsafe void PinchAllRetainers()
    {
      if (_taskManager.IsBusy)
        return;
  
      ClearState();
      if (GenericHelpers.TryGetAddonByName<AtkUnitBase>("RetainerList", out var addon) && GenericHelpers.IsAddonReady(addon))
      {
        // we cache the number of retainers because AddonMaster will be disposed once the RetainerList addon is closed.
        var retainerList = new AddonMaster.RetainerList(addon);
        var retainers = retainerList.Retainers;
        
        var retainerNames = retainers.Select(retainer => retainer.Name).ToArray();
        var characterContentId = Svc.PlayerState.ContentId;
        var skipWindow = RecentPinchTracker.GetSkipWindow(Plugin.Configuration.SkipRecentlyPinchedMinutes);
        var recentlyPinchedNames = _recentPinchTracker.GetRecentlyPinchedNames(
          characterContentId,
          retainerNames,
          skipWindow);
        var selection = AutoPinchRunPlanner.SelectResumeRetainerIndexes(
          retainerNames,
          Plugin.Configuration.EnabledRetainerNames,
          Configuration.ALL_DISABLED_SENTINEL,
          recentlyPinchedNames);

        if (selection.Indexes.Count == 0 && Plugin.Configuration.EnabledRetainerNames.Contains(Configuration.ALL_DISABLED_SENTINEL))
        {
          Communicator.PrintAllRetainersDisabled();
          return;
        }

        if (selection.Indexes.Count == 0)
          return;

        Communicator.PrintRecentlyPinchedSkipped(selection.SkippedRetainerNames);

        RegisterTalkAddonListeners();
        _autoRetainerSuppressionCoordinator.BeginRun();

        foreach (var retainerIndex in selection.Indexes)
        {
          EnqueueSingleRetainer(
            retainerIndex,
            new RetainerPinchKey(characterContentId, retainerNames[retainerIndex]));
        }

        EnqueueAutoPinchAction(RemoveTalkAddonListeners, nameof(RemoveTalkAddonListeners));
        if (Plugin.Configuration.TTSWhenAllDone)
          EnqueueAutoPinchTask(() => SpeakTTS(Plugin.Configuration.TTSWhenAllDoneMsg), "SpeakTTSAll");
      }
    }

    private void EnqueueSingleRetainer(int index, RetainerPinchKey pinchKey)
    {
      EnqueueAutoPinchTask(() => ClickRetainer(index), $"ClickRetainer{index}");
      EnqueueAutoPinchDelay(100, $"DelayAfterClickRetainer{index}");
      EnqueueAutoPinchTask(ClickSellItems, $"ClickSellItems{index}");
      EnqueueAutoPinchDelay(500, $"DelayAfterClickSellItems{index}");
      EnqueueAutoPinchTask(
        () => AutoPinchRunPlanner.ShouldCompleteSelectedRetainerTask(EnqueueAllRetainerItems(InsertSingleItem, true)),
        $"EnqueueAllRetainerItems{index}");
      // Item tasks are inserted ahead of this queue position at runtime, so the
      // mark fires only after every item completes; a timeout abort clears the
      // queue first and intentionally leaves this retainer unmarked.
      EnqueueAutoPinchAction(() => _recentPinchTracker.MarkPinched(pinchKey), $"MarkRetainerPinched{index}");
      EnqueueAutoPinchDelay(500, $"DelayAfterRetainerItems{index}");
      EnqueueAutoPinchTask(CloseRetainerSellList, $"CloseRetainerSellList{index}");
      EnqueueAutoPinchDelay(100, $"DelayAfterCloseRetainerSellList{index}");
      EnqueueSaleHistoryVisit(index, pinchKey.RetainerName);
      EnqueueAutoPinchTask(CloseRetainer, $"CloseRetainer{index}");
      EnqueueAutoPinchDelay(100, $"DelayAfterCloseRetainer{index}");
    }

    private static unsafe bool? ClickRetainer(int index)
    {
      if (GenericHelpers.TryGetAddonByName<AtkUnitBase>("RetainerList", out var addon) && GenericHelpers.IsAddonReady(addon))
      {
        Communicator.PrintRetainerName(new AddonMaster.RetainerList(addon).Retainers[index].Name);
        ECommons.Automation.Callback.Fire(addon, true, 2, index);
        return true;
      }
      else
        return false;
    }

    private unsafe bool? ClickSellItems()
    {
      if (GenericHelpers.TryGetAddonByName<AtkUnitBase>("SelectString", out var addon) && GenericHelpers.IsAddonReady(addon))
      {
        var entries = new AddonMaster.SelectString(addon).Entries;
        var entryTexts = entries.Select(entry => entry.Text).ToArray();
        var entryIndex = AutoPinchRunPlanner.FindFirstMenuEntryIndex(entryTexts, _sellItemsLabels);
        if (entryIndex is null)
        {
          Svc.Log.Warning(
            "No sell items entry matched the Addon sheet labels (entries: {Entries}); falling back to menu position {FallbackIndex}",
            string.Join(" | ", entryTexts),
            FallbackSellItemsEntryIndex);
          entryIndex = FallbackSellItemsEntryIndex;
        }

        entries[entryIndex.Value].Select();
        return true;
      }
      else
        return false;
    }

    private static unsafe bool? CloseRetainerSellList()
    {
      if (GenericHelpers.TryGetAddonByName<AtkUnitBase>("RetainerSellList", out var addon) && GenericHelpers.IsAddonReady(addon))
      {
        addon->Close(true);
        return true;
      }
      else
        return false;
    }

    private static unsafe bool? CloseRetainer()
    {
      if (GenericHelpers.TryGetAddonByName<AtkUnitBase>("SelectString", out var addon) && GenericHelpers.IsAddonReady(addon))
      {
        addon->Close(true);
        return true;
      }
      else
        return false;
    }

    private static string? ResolveAddonSheetText(uint rowId)
    {
      try
      {
        if (!Svc.Data.GetExcelSheet<Addon>().TryGetRow(rowId, out var row))
          return null;

        var text = row.Text.GetText();
        return string.IsNullOrWhiteSpace(text) ? null : text;
      }
      catch (Exception ex)
      {
        Svc.Log.Warning(ex, "Failed to read Addon sheet row {RowId}", rowId);
        return null;
      }
    }

    private static IReadOnlyList<string> ResolveSellItemsLabels()
    {
      var labels = new List<string>();
      foreach (var rowId in new[] { SellItemsPlayerInventoryAddonRowId, SellItemsRetainerInventoryAddonRowId })
      {
        var label = ResolveAddonSheetText(rowId);
        if (label is not null)
          labels.Add(label);
      }

      return labels;
    }

    private void EnqueueSaleHistoryVisit(int index, string retainerName)
    {
      var steps = AutoPinchRunPlanner.PlanSaleHistorySteps(
        Plugin.Configuration.OpenSaleHistoryDuringAutoPinch,
        _saleHistoryLabel);
      if (steps.Count == 0)
        return;

      var visit = new SaleHistoryVisit();
      foreach (var step in steps)
        EnqueueSaleHistoryStep(visit, step, index, retainerName);
    }

    private void EnqueueSaleHistoryStep(SaleHistoryVisit visit, SaleHistoryStep step, int index, string retainerName)
    {
      switch (step)
      {
        case SaleHistoryStep.OpenSaleHistory:
          EnqueueSaleHistoryTask(visit, () => TryOpenSaleHistory(visit, retainerName), $"OpenSaleHistory{index}", retainerName);
          break;
        case SaleHistoryStep.DelayAfterOpenSaleHistory:
          EnqueueSaleHistoryDelay(visit, SaleHistoryUiSettleDelayMs, $"DelayAfterOpenSaleHistory{index}", retainerName);
          break;
        case SaleHistoryStep.WaitForRetainerHistory:
          EnqueueSaleHistoryTask(visit, IsRetainerHistoryReady, $"WaitForRetainerHistory{index}", retainerName);
          break;
        case SaleHistoryStep.DwellOnSaleHistory:
          EnqueueSaleHistoryDelay(visit, SaleHistoryDwellMs, $"DwellOnSaleHistory{index}", retainerName);
          break;
        case SaleHistoryStep.CloseSaleHistory:
          EnqueueSaleHistoryTask(visit, CloseSaleHistoryWindow, $"CloseSaleHistory{index}", retainerName);
          break;
        case SaleHistoryStep.DelayAfterCloseSaleHistory:
          EnqueueSaleHistoryDelay(visit, SaleHistoryUiSettleDelayMs, $"DelayAfterCloseSaleHistory{index}", retainerName);
          break;
        default:
          throw new ArgumentOutOfRangeException(nameof(step), step, null);
      }
    }

    private void EnqueueSaleHistoryTask(SaleHistoryVisit visit, Func<bool> runStep, string taskName, string retainerName)
    {
      EnqueueAutoPinchTask(CreateSaleHistoryStepTask(visit, runStep, taskName, retainerName), taskName);
    }

    private void EnqueueSaleHistoryDelay(SaleHistoryVisit visit, int delayMs, string taskName, string retainerName)
    {
      var delay = AutoPinchDelayTask.Create(delayMs, _getTickCount);
      EnqueueSaleHistoryTask(visit, () => delay() == true, taskName, retainerName);
    }

    private Func<bool?> CreateSaleHistoryStepTask(SaleHistoryVisit visit, Func<bool> runStep, string taskName, string retainerName)
    {
      long? startedAt = null;
      return () =>
      {
        var now = _getTickCount();
        startedAt ??= now;
        var isStepComplete = !visit.Skipped && runStep();
        var status = AutoPinchRunPlanner.GetSaleHistoryStepStatus(
          visit.Skipped,
          isStepComplete,
          startedAt.Value,
          now,
          AutoPinchTimeoutPolicy.SaleHistoryStepDeadlineMs);

        switch (status)
        {
          case SaleHistoryStepStatus.Skipped:
          case SaleHistoryStepStatus.Complete:
            return true;
          case SaleHistoryStepStatus.KeepWaiting:
            return false;
          case SaleHistoryStepStatus.DeadlineExceeded:
            AbandonSaleHistoryVisit(visit, taskName, retainerName);
            return true;
          default:
            throw new ArgumentOutOfRangeException(nameof(status), status, null);
        }
      };
    }

    private unsafe bool TryOpenSaleHistory(SaleHistoryVisit visit, string retainerName)
    {
      if (!GenericHelpers.TryGetAddonByName<AtkUnitBase>("SelectString", out var addon) || !GenericHelpers.IsAddonReady(addon))
        return false;

      var entries = new AddonMaster.SelectString(addon).Entries;
      var entryTexts = entries.Select(entry => entry.Text).ToArray();
      // PlanSaleHistorySteps only emits steps when the label resolved, so _saleHistoryLabel is non-null here.
      var entryIndex = AutoPinchRunPlanner.FindMenuEntryIndex(entryTexts, _saleHistoryLabel!);
      if (entryIndex is null)
      {
        visit.Skipped = true;
        Svc.Log.Warning(
          "Retainer {RetainerName} menu has no \"{Label}\" entry (entries: {Entries}); skipping sale history for this retainer",
          retainerName,
          _saleHistoryLabel,
          string.Join(" | ", entryTexts));
        return true;
      }

      entries[entryIndex.Value].Select();
      return true;
    }

    private static unsafe bool IsRetainerHistoryReady()
    {
      return GenericHelpers.TryGetAddonByName<AtkUnitBase>(RetainerHistoryAddonName, out var addon)
        && GenericHelpers.IsAddonReady(addon);
    }

    private static unsafe bool CloseSaleHistoryWindow()
    {
      if (GenericHelpers.TryGetAddonByName<AtkUnitBase>(RetainerHistoryAddonName, out var addon))
        addon->Close(true);

      return true;
    }

    private void AbandonSaleHistoryVisit(SaleHistoryVisit visit, string taskName, string retainerName)
    {
      visit.Skipped = true;
      Svc.Log.Warning(
        "Sale history step {TaskName} for retainer {RetainerName} exceeded its {DeadlineMs} ms deadline; skipping sale history for this retainer",
        taskName,
        retainerName,
        AutoPinchTimeoutPolicy.SaleHistoryStepDeadlineMs);
      CloseSaleHistoryWindow();
    }

    private unsafe void PinchAllRetainerItems()
    {
      _mbHandler.PopulateRetainerCache();
      if (_taskManager.IsBusy)
        return;

      ClearState();
      var sellListWorkState = EnqueueAllRetainerItems(EnqueueSingleItem, false);
      if (!AutoPinchRunPlanner.ShouldStartCurrentRetainerRun(sellListWorkState))
        return;

      _autoRetainerSuppressionCoordinator.BeginRun();
    }

    private unsafe AutoPinchSellListWorkState EnqueueAllRetainerItems(Action<int> enqueueFunc, bool reverseOrder)
    {
      if (GenericHelpers.TryGetAddonByName<AtkUnitBase>("RetainerSellList", out var addon) && GenericHelpers.IsAddonReady(addon))
      {
        var listNode = (AtkComponentNode*)addon->UldManager.NodeList[10];
        var listComponent = (AtkComponentList*)listNode->Component;
        int num = listComponent->ListLength;
        var sellListWorkState = AutoPinchRunPlanner.GetSellListWorkState(
          isSellListAvailable: true,
          itemCount: num);
        if (sellListWorkState != AutoPinchSellListWorkState.HasItems)
          return sellListWorkState;

        if (reverseOrder)
        {
          for (int i = num - 1; i >= 0; i--)
          {
            enqueueFunc(i);
          }
        }
        else
        {
          for (int i = 0; i < num; i++)
          {
            enqueueFunc(i);
          }
        }
        if (Plugin.Configuration.TTSWhenEachDone)
          EnqueueAutoPinchTask(() => SpeakTTS(Plugin.Configuration.TTSWhenEachDoneMsg), "SpeakTTSEach");

        return sellListWorkState;
      }
      else
        return AutoPinchSellListWorkState.Unavailable;
    }

    private void EnqueueSingleItem(int index)
    {
      EnqueueAutoPinchTask(() => OpenItemContextMenu(index), $"OpenItemContextMenu{index}");
      EnqueueAutoPinchDelay(100, $"DelayAfterOpenItemContextMenu{index}");
      EnqueueAutoPinchTask(ClickAdjustPrice, $"ClickAdjustPrice{index}");
      EnqueueAutoPinchDelay(100, $"DelayAfterClickAdjustPrice{index}");
      EnqueueAutoPinchTask(DelayMarketBoard, $"DelayMB{index}");
      EnqueueAutoPinchTask(ClickComparePrice, $"ClickComparePrice{index}");
      EnqueueAutoPinchDelay(Plugin.Configuration.MarketBoardKeepOpenMS, $"DelayMarketBoardKeepOpen{index}");
      EnqueueAutoPinchTask(
        WaitForMarketPrice,
        $"WaitForMarketPrice{index}",
        AutoPinchTimeoutPolicy.MarketPriceWaitTimeoutMs);
      EnqueueAutoPinchTask(SetNewPrice, $"SetNewPrice{index}");
    }

    private void InsertSingleItem(int index)
    {
      // reverse order because we INSERT
      InsertAutoPinchTask(SetNewPrice, $"SetNewPrice{index}");
      InsertAutoPinchTask(
        WaitForMarketPrice,
        $"WaitForMarketPrice{index}",
        AutoPinchTimeoutPolicy.MarketPriceWaitTimeoutMs);
      InsertAutoPinchDelay(Plugin.Configuration.MarketBoardKeepOpenMS, $"DelayMarketBoardKeepOpen{index}");
      InsertAutoPinchTask(ClickComparePrice, $"ClickComparePrice{index}");
      InsertAutoPinchTask(DelayMarketBoard, $"DelayMB{index}");
      InsertAutoPinchDelay(100, $"DelayAfterClickAdjustPrice{index}");
      InsertAutoPinchTask(ClickAdjustPrice, $"ClickAdjustPrice{index}");
      InsertAutoPinchDelay(100, $"DelayAfterOpenItemContextMenu{index}");
      InsertAutoPinchTask(() => OpenItemContextMenu(index), $"OpenItemContextMenu{index}");
    }

    private static unsafe bool? OpenItemContextMenu(int itemIndex)
    {
      if (GenericHelpers.TryGetAddonByName<AtkUnitBase>("RetainerSellList", out var addon) && GenericHelpers.IsAddonReady(addon))
      {
        Svc.Log.Debug($"Clicking item {itemIndex}");
        ECommons.Automation.Callback.Fire(addon, true, 0, itemIndex, 1); // click item
        return true;
      }

      return false;
    }

    private unsafe bool? ClickAdjustPrice()
    {
      if (GenericHelpers.TryGetAddonByName<AtkUnitBase>("ContextMenu", out var addon) && GenericHelpers.IsAddonReady(addon))
      {
        var reader = new ReaderContextMenu(addon);
        if (!IsItemMannequin(reader.Entries))
        {
          Svc.Log.Debug($"Clicking adjust price");
          ECommons.Automation.Callback.Fire(addon, true, 0, 0, 0, 0, 0); // click adjust price
        }
        else
        {
          Svc.Log.Debug("Current item is a mannequin item and will be skipped");
          _skipCurrentItem = true;
          addon->Close(true);
        }

        return true;
      }

      return false;
    }

    /// <summary>
    /// Checks if an item is a mannequin item, by checking if there is
    /// the "adjust price" entry in the given <paramref name="contextMenuEntries"/>.
    /// </summary>
    /// <param name="contextMenuEntries">Context menu entries to check.</param>
    /// <returns>True if item is a mannequin item, false otherwise.</returns>
    private static bool IsItemMannequin(List<ContextMenuEntry> contextMenuEntries)
    {
      return !contextMenuEntries.Any((e) => e.Name.Equals("adjust price", StringComparison.CurrentCultureIgnoreCase)
                                        || e.Name.Equals("preis ändern", StringComparison.CurrentCultureIgnoreCase)
                                        || e.Name.Equals("価格を変更する", StringComparison.CurrentCultureIgnoreCase)
                                        || e.Name.Equals("changer le prix", StringComparison.CurrentCultureIgnoreCase));
    }

    private unsafe bool? DelayMarketBoard()
    {
      if (_skipCurrentItem)
        return true;

      if (GenericHelpers.TryGetAddonByName<AddonRetainerSell>("RetainerSell", out var addon) && GenericHelpers.IsAddonReady(&addon->AtkUnitBase))
      {
        var itemName = addon->ItemName->NodeText.ToString();
        if (!_cachedPrices.TryGetValue(itemName, out int? value) || value <= 0)
        {
          Svc.Log.Debug($"{itemName} has no cached price (or that price was <= 0), delaying next mb open");
          InsertAutoPinchDelay(Plugin.Configuration.GetMBPricesDelayMS, "DelayMarketBoardPriceFetch");
        }

        return true;
      }

      return false;
    }

    private unsafe bool? ClickComparePrice()
    {
      if (_skipCurrentItem)
        return true;

      if (GenericHelpers.TryGetAddonByName<AddonRetainerSell>("RetainerSell", out var addon) && GenericHelpers.IsAddonReady(&addon->AtkUnitBase))
      {
        // if we have a cached price, dont click compare
        var itemName = addon->ItemName->NodeText.ToString();
        if (_cachedPrices.TryGetValue(itemName, out int? value) && value > 0)
        {
          Svc.Log.Debug($"{itemName}: using cached price");
          _newPrice = value;
          _pricingDebugDetail = new PricingDebugDetail(PricingDebugReason.CachedPrice)
          {
            SelectedPrice = value
          };
          return true;
        }
        else
        {
          Svc.Log.Debug(
            "{ItemName}: clicking compare prices, pending market board request {IsPricePending}",
            itemName,
            _mbHandler.IsPricePending);
          _mbHandler.PrepareForPriceRequest();
          ECommons.Automation.Callback.Fire(&addon->AtkUnitBase, true, ComparePricesCallbackId);
          return true;
        }
      }

      return false;
    }

    private unsafe bool? ClickComparePriceFresh()
    {
      if (_skipCurrentItem)
        return true;

      if (GenericHelpers.TryGetAddonByName<AddonRetainerSell>("RetainerSell", out var addon) && GenericHelpers.IsAddonReady(&addon->AtkUnitBase))
      {
        var itemName = addon->ItemName->NodeText.ToString();
        Svc.Log.Debug(
          "{ItemName}: forcing fresh compare prices, pending market board request {IsPricePending}",
          itemName,
          _mbHandler.IsPricePending);
        ClearCurrentPriceRequestState();
        _mbHandler.PrepareForPriceRequest();
        ECommons.Automation.Callback.Fire(&addon->AtkUnitBase, true, ComparePricesCallbackId);
        return true;
      }

      return false;
    }

    private unsafe bool? SetNewPrice()
    {
      try
      {
        if (_skipCurrentItem)
          return true;

        // close compare price window
        if (GenericHelpers.TryGetAddonByName<AtkUnitBase>("ItemSearchResult", out var addon))
          addon->Close(true);

        if (GenericHelpers.TryGetAddonByName<AddonRetainerSell>("RetainerSell", out var retainerSell) && GenericHelpers.IsAddonReady(&retainerSell->AtkUnitBase))
        {
          var ui = &retainerSell->AtkUnitBase;
          var itemName = retainerSell->ItemName->NodeText.ToString();
          _oldPrice = retainerSell->AskingPrice->Value;
          if (_newPrice.HasValue && _newPrice > 0)
          {
            var cutPercentage = ((float)_newPrice.Value - _oldPrice.Value) / _oldPrice.Value * 100f;
            if (cutPercentage >= -Plugin.Configuration.MaxUndercutPercentage)
            {
              Svc.Log.Debug($"Setting new price");
              _cachedPrices.TryAdd(itemName, _newPrice);
              retainerSell->AskingPrice->SetValue(_newPrice.Value);
              Communicator.PrintPriceUpdate(itemName, _oldPrice.Value, _newPrice.Value, cutPercentage);
              Communicator.PrintPricingDebug(itemName, _pricingDebugDetail);
            }
            else
            {
              Communicator.PrintAboveMaxCutError(itemName);
              Communicator.PrintPricingDebug(itemName, _pricingDebugDetail);
            }

            ECommons.Automation.Callback.Fire(&retainerSell->AtkUnitBase, true, 0); // confirm
            ui->Close(true);

            return true;
          }
          else
          {
            Svc.Log.Warning(
              "{ItemName}: no price to set, old price {OldPrice}, received price {NewPrice}, pending market board request {IsPricePending}, skip current item {SkipCurrentItem}, pricing reason {PricingReason}",
              itemName,
              _oldPrice,
              _newPrice,
              _mbHandler.IsPricePending,
              _skipCurrentItem,
              _pricingDebugDetail?.Reason);
            Communicator.PrintNoPriceToSetError(itemName, _pricingDebugDetail);
            Communicator.PrintPricingDebug(itemName, _pricingDebugDetail);
            ECommons.Automation.Callback.Fire(&retainerSell->AtkUnitBase, true, 1); // cancel
            ui->Close(true);
            return true;
          }
        }
        else
          return false;
      }
      finally
      {
        ClearCurrentPriceRequestState();
      }
    }

    private void MBHandler_NewPriceReceived(object? sender, NewPriceEventArgs e)
    {
      Svc.Log.Debug(
        "New price received: {NewPrice}, pricing reason {PricingReason}",
        e.NewPrice,
        e.DebugDetail?.Reason);
      _newPrice = e.NewPrice;
      _pricingDebugDetail = e.DebugDetail;
    }

    private unsafe void SkipRetainerDialog(AddonEvent type, AddonArgs args)
    {
      // fallback for when something was improperly cleaned up
      if (!_taskManager.IsBusy)
        RemoveTalkAddonListeners();
      else
      {
        if (((AtkUnitBase*)args.Addon.Address)->IsVisible)
          new AddonMaster.Talk(args.Addon).Click();
      }
    }

    private unsafe void RetainerSellPostSetup(AddonEvent type, AddonArgs args)
    {
      var addon = (AddonRetainerSell*)args.Addon.Address;
      if (!GenericHelpers.IsAddonReady(&addon->AtkUnitBase))
        return;

      RegisterComparePricePostPinchHandler(addon);

      var actions = PostPinchWorkflow.PlanSellAddonSetupActions(
        isFeatureEnabled: Plugin.Configuration.EnablePostPinchkey,
        isPostPinchKeyHeld: Plugin.KeyState[Plugin.Configuration.PostPinchKey],
        isTaskManagerBusy: _taskManager.IsBusy,
        isSellAddonReady: true);

      if (actions.Count == 0)
        return;

      Svc.Log.Debug(
        "Post pinch key {PostPinchKey} detected, enqueueing posted price update tasks",
        Plugin.Configuration.PostPinchKey);

      foreach (var action in actions)
        ExecutePostPinchWorkflowAction(action);
    }

    private unsafe void RegisterComparePricePostPinchHandler(AddonRetainerSell* addon)
    {
      var comparePricesButton = addon->GetComponentButtonById(ComparePricesButtonNodeId);
      var ownerNode = comparePricesButton == null
        ? null
        : comparePricesButton->OwnerNode;

      if (ownerNode == null)
      {
        Svc.Log.Debug("RetainerSell compare prices button owner node was not available");
        return;
      }

      var eventHandle = Svc.AddonEventManager.AddEvent(
        new IntPtr(&addon->AtkUnitBase),
        new IntPtr(ownerNode),
        AddonEventType.ButtonPress,
        ComparePriceButtonPress);
      if (eventHandle == null)
        Svc.Log.Debug("RetainerSell compare prices button press event was not added");
    }

    private unsafe void ComparePriceButtonPress(AddonEventType eventType, AddonEventData _)
    {
      var isSellAddonReady =
        GenericHelpers.TryGetAddonByName<AddonRetainerSell>("RetainerSell", out var addon)
        && GenericHelpers.IsAddonReady(&addon->AtkUnitBase);

      var actions = PostPinchWorkflow.PlanActions(
        isCompareButtonPress: eventType == AddonEventType.ButtonPress,
        isFeatureEnabled: Plugin.Configuration.EnablePostPinchkey,
        isPostPinchKeyHeld: Plugin.KeyState[Plugin.Configuration.PostPinchKey],
        isTaskManagerBusy: _taskManager.IsBusy,
        isSellAddonReady: isSellAddonReady);

      foreach (var action in actions)
        ExecutePostPinchWorkflowAction(action);
    }

    private void ExecutePostPinchWorkflowAction(PostPinchWorkflowAction action)
    {
      switch (action)
      {
        case PostPinchWorkflowAction.PreparePriceRequest:
          ClearCurrentPriceRequestState();
          _mbHandler.PrepareForPriceRequest();
          break;
        case PostPinchWorkflowAction.ForceComparePrice:
          _taskManager.Enqueue(ClickComparePriceFresh, "ClickComparePriceFreshPostSetup");
          break;
        case PostPinchWorkflowAction.DelayForMarketBoard:
          _taskManager.DelayNext(Plugin.Configuration.MarketBoardKeepOpenMS);
          break;
        case PostPinchWorkflowAction.WaitForMarketPrice:
          _taskManager.Enqueue(WaitForMarketPrice, "WaitForMarketPricePostCompare");
          break;
        case PostPinchWorkflowAction.SetNewPrice:
          _taskManager.Enqueue(SetNewPrice, "SetNewPricePostCompare");
          break;
        default:
          throw new ArgumentOutOfRangeException(nameof(action), action, null);
      }
    }

    private void EnqueueAutoPinchTask(
      Func<bool?> task,
      string taskName,
      int timeoutMs = AutoPinchTimeoutPolicy.GeneralTaskTimeoutMs)
    {
      _taskManager.Enqueue(
        () => _autoPinchTaskGuard.Run(task, taskName, timeoutMs),
        AutoPinchTimeoutPolicy.GetLegacyBackstopTimeoutMs(timeoutMs),
        true,
        taskName);
    }

    private void EnqueueAutoPinchDelay(int delayMs, string taskName)
    {
      EnqueueAutoPinchTask(AutoPinchDelayTask.Create(delayMs, _getTickCount), taskName);
    }

    private void EnqueueAutoPinchAction(Action action, string taskName)
    {
      EnqueueAutoPinchTask(() =>
      {
        action();
        return true;
      }, taskName);
    }

    private void InsertAutoPinchTask(
      Func<bool?> task,
      string taskName,
      int timeoutMs = AutoPinchTimeoutPolicy.GeneralTaskTimeoutMs)
    {
      _taskManager.Insert(
        () => _autoPinchTaskGuard.Run(task, taskName, timeoutMs),
        AutoPinchTimeoutPolicy.GetLegacyBackstopTimeoutMs(timeoutMs),
        true,
        taskName);
    }

    private void InsertAutoPinchDelay(int delayMs, string taskName)
    {
      InsertAutoPinchTask(AutoPinchDelayTask.Create(delayMs, _getTickCount), taskName);
    }

    private void ExecuteCleanupActions(
      IReadOnlyList<AutoPinchCleanupAction> actions,
      Exception? exception = null)
    {
      foreach (var action in actions)
        ExecuteCleanupAction(action, exception);
    }

    private void ExecuteCleanupAction(AutoPinchCleanupAction action, Exception? exception)
    {
      switch (action)
      {
        case AutoPinchCleanupAction.AbortTasks:
          _taskManager.Abort();
          break;
        case AutoPinchCleanupAction.EndSuppression:
          _autoRetainerSuppressionCoordinator.EndRun();
          break;
        case AutoPinchCleanupAction.LogException:
          if (exception is not null)
            LogAutoPinchException(exception);
          break;
        case AutoPinchCleanupAction.RemoveTalkListeners:
          RemoveTalkAddonListeners();
          break;
        default:
          throw new ArgumentOutOfRangeException(nameof(action), action, null);
      }
    }

    private static void LogAutoPinchTaskException(Exception exception, string taskName)
    {
      Svc.Log.Error(exception, "Auto pinch task {TaskName} failed", taskName);
    }

    private static void LogAutoPinchTaskTimeout(TimeoutException exception, string taskName, int timeoutMs)
    {
      Svc.Log.Error(exception, "Auto pinch task {TaskName} timed out after {TimeoutMs} ms", taskName, timeoutMs);
      if (Plugin.Configuration.ShowErrorsInChat)
      {
        var timeoutSeconds = TimeSpan.FromMilliseconds(timeoutMs).TotalSeconds;
        Svc.Chat.PrintError($"Auto pinch stopped while waiting for {taskName} after {timeoutSeconds:N0} seconds.");
      }
    }

    private static void LogAutoPinchException(Exception exception)
    {
      Svc.Log.Error(exception, "Error while auto pinching");
      if (Plugin.Configuration.ShowErrorsInChat)
        Svc.Chat.PrintError($"Error while auto pinching: {exception.Message}");
    }

    private bool? WaitForMarketPrice()
    {
      if (_skipCurrentItem)
        return true;

      if (_mbHandler.IsPricePending)
      {
        if (!_loggedMarketPriceWait)
        {
          Svc.Log.Debug("Waiting for market board price request before setting retainer price");
          _loggedMarketPriceWait = true;
        }

        return false;
      }

      if (_loggedMarketPriceWait)
      {
        Svc.Log.Debug(
          "Market board price wait finished, received price {NewPrice}, pricing reason {PricingReason}",
          _newPrice,
          _pricingDebugDetail?.Reason);
        _loggedMarketPriceWait = false;
      }

      return true;
    }
    private void RemoveTalkAddonListeners()
    {
      Svc.AddonLifecycle.UnregisterListener(AddonEvent.PostSetup, "Talk", SkipRetainerDialog);
      Svc.AddonLifecycle.UnregisterListener(AddonEvent.PostUpdate, "Talk", SkipRetainerDialog);
      _hasTalkAddonListeners = false;
    }

    private void RegisterTalkAddonListeners()
    {
      Svc.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, "Talk", SkipRetainerDialog);
      Svc.AddonLifecycle.RegisterListener(AddonEvent.PostUpdate, "Talk", SkipRetainerDialog);
      _hasTalkAddonListeners = true;
    }

    private static unsafe Vector2 GetNodePosition(AtkResNode* node)
    {
      var pos = new Vector2(node->X, node->Y);
      var par = node->ParentNode;
      while (par != null)
      {
        pos *= new Vector2(par->ScaleX, par->ScaleY);
        pos += new Vector2(par->X, par->Y);
        par = par->ParentNode;
      }

      return pos;
    }

    private static unsafe Vector2 GetNodeScale(AtkResNode* node)
    {
      if (node == null) return new Vector2(1, 1);
      var scale = new Vector2(node->ScaleX, node->ScaleY);
      while (node->ParentNode != null)
      {
        node = node->ParentNode;
        scale *= new Vector2(node->ScaleX, node->ScaleY);
      }

      return scale;
    }

    private static bool? SpeakTTS(string msg)
    {
      if (!Plugin.Configuration.DontUseTTS)
      {
        SpeechSynthesizer tts = new()
        {
          Volume = Plugin.Configuration.TTSVolume
        };
        tts.SpeakAsync(msg);
        tts.SpeakCompleted += (o, e) =>
        {
          tts.Dispose();
          Svc.Log.Verbose($"Finished message: {msg} - tts disposed");
        };
      }
      return true;
    }

    private void ClearState()
    {
      _newPrice = null;
      _pricingDebugDetail = null;
      _cachedPrices = [];
      _skipCurrentItem = false;
      _loggedMarketPriceWait = false;
    }

    private void ClearCurrentPriceRequestState()
    {
      _oldPrice = null;
      _newPrice = null;
      _pricingDebugDetail = null;
      _skipCurrentItem = false;
      _loggedMarketPriceWait = false;
    }

    /// <summary>
    /// Per-retainer soft-fail marker for the sale history visit. Each retainer's step
    /// closures share one instance, so a skip never leaks into the next retainer.
    /// </summary>
    private sealed class SaleHistoryVisit
    {
      internal bool Skipped;
    }
  }
}
