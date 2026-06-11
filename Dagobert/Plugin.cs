using System;
using System.Collections.Generic;
using System.Net.Http;
using Dalamud.Game.Command;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using Dagobert.Windows;
using ECommons;

namespace Dagobert;

public sealed class Plugin : IDalamudPlugin
{
  [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
  [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
  [PluginService] public static IMarketBoard MarketBoard { get; private set; } = null!;
  [PluginService] public static IKeyState KeyState { get; private set; } = null!;
  [PluginService] public static IAddonLifecycle AddonLifecycle { get; private set; } = null!;
  [PluginService] public static IChatGui ChatGui { get; private set; } = null!;
  [PluginService] public static IClientState ClientState { get; private set; } = null!;
  [PluginService] public static IPlayerState PlayerState { get; private set; } = null!;
  [PluginService] public static IGameInteropProvider GameInteropProvider { get; private set; } = null!;
  [PluginService] public static IPluginLog Log { get; private set; } = null!;

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.
  public static Configuration Configuration { get; private set; } // will never be null
  public static DalamudLinkPayload ConfigLinkPayload { get; private set; } = null!;
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.

  private static readonly TimeSpan UniversalisTimeout = TimeSpan.FromSeconds(4);
  private readonly AutoPinch _autoPinch;
  private readonly HttpClient _universalisHttpClient;
  private readonly MarketBoardRequestTracker _marketBoardRequestTracker;

  public readonly WindowSystem WindowSystem = new("Ascended Dagobert");
  private ConfigWindow ConfigWindow { get; init; }

  public Plugin()
  {
    Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
    MigrateConfiguration(Configuration);
    ConfigWindow = new ConfigWindow();
    WindowSystem.AddWindow(ConfigWindow);

    CommandManager.AddHandler("/dagobert", new CommandInfo(OnDagobertCommand)
    {
      HelpMessage = "Opens the Ascended Dagobert configuration window"
    });

    // Register chat link handler for clickable config link
    ConfigLinkPayload = ChatGui.AddChatLinkHandler(0, (id, _) => ToggleConfigUI());

    PluginInterface.UiBuilder.Draw += DrawUI;
    PluginInterface.UiBuilder.OpenMainUi += ToggleConfigUI;
    PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUI;

    ECommonsMain.Init(PluginInterface, this);
    _universalisHttpClient = CreateUniversalisHttpClient();
    var universalisPriceProvider = new UniversalisPriceProvider(_universalisHttpClient, Log);
    _marketBoardRequestTracker = new MarketBoardRequestTracker(GameInteropProvider, Log);
    var autoRetainerSuppressionCoordinator =
      new AutoRetainerSuppressionCoordinator(new AutoRetainerIPC());
    var recentPinchTracker = new RecentPinchTracker(() => Environment.TickCount64);
    _autoPinch = new AutoPinch(
      universalisPriceProvider,
      _marketBoardRequestTracker,
      autoRetainerSuppressionCoordinator,
      recentPinchTracker);
    WindowSystem.AddWindow(_autoPinch);
  }

  public void Dispose()
  {
    WindowSystem.RemoveAllWindows();
    _autoPinch.Dispose();
    _marketBoardRequestTracker.Dispose();
    _universalisHttpClient.Dispose();
    CommandManager.RemoveHandler("/dagobert");
    ECommonsMain.Dispose();
  }

  // Carries saved configs across renamed settings without dropping existing user choices.
  private static void MigrateConfiguration(Configuration config)
  {
    var shouldSave = false;

    if (config.Version < 1 && config.UndercutMode == UndercutMode.Percentage)
    {
      config.UndercutAmountPercentage = config.UndercutAmount;
      config.Version = 1;
      shouldSave = true;
    }

    if (config.Version < 2 || config.HasLegacyThinMarketSaleReferenceSettings)
    {
      config.MigrateThinMarketSaleReferenceSettings();
      config.Version = 2;
      shouldSave = true;
    }

    if (shouldSave)
      config.Save();
  }

  private void OnDagobertCommand(string command, string args)
  {
    // in response to the slash command, just toggle the display status of our main ui
    ToggleConfigUI();
  }

  private void DrawUI()
  {
    WindowSystem.Draw();
  }

  public void ToggleConfigUI() => ConfigWindow.Toggle();

  private static HttpClient CreateUniversalisHttpClient()
  {
    var httpClient = new HttpClient
    {
      BaseAddress = new Uri("https://universalis.app/api/v2/"),
      Timeout = UniversalisTimeout
    };

    httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
      $"{PluginInterface.InternalName}/{PluginInterface.Manifest.AssemblyVersion}");

    return httpClient;
  }
}
