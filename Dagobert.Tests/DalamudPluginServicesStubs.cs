using System;

namespace Dalamud.Plugin.Services;

internal interface IPluginLog
{
  void Warning(string messageTemplate, params object?[] propertyValues);

  void Warning(Exception exception, string messageTemplate, params object?[] propertyValues);
}
