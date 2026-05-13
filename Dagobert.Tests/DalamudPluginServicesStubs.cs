using System;

namespace Dalamud.Plugin.Services;

internal interface IPluginLog
{
  void Debug(string messageTemplate, params object?[] propertyValues);

  void Warning(string messageTemplate, params object?[] propertyValues);

  void Warning(Exception exception, string messageTemplate, params object?[] propertyValues);
}
