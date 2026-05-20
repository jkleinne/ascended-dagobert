using System;
using System.Diagnostics.CodeAnalysis;

namespace Dagobert;

internal readonly record struct AutoRetainerIpcState(bool IsAutoRetainerLoaded)
{
  [SuppressMessage(
    "Design",
    "CA1031:Do not catch general exception types",
    Justification = "IPC delegates can throw plugin boundary exceptions that must be converted to false with warning context.")]
  internal bool TryReadSuppressed(
    Func<bool> readSuppressed,
    Action<Exception> logWarning,
    out bool isSuppressed)
  {
    isSuppressed = false;
    if (!IsAutoRetainerLoaded)
      return false;

    try
    {
      isSuppressed = readSuppressed();
      return true;
    }
    catch (Exception ex)
    {
      logWarning(ex);
      return false;
    }
  }

  [SuppressMessage(
    "Design",
    "CA1031:Do not catch general exception types",
    Justification = "IPC delegates can throw plugin boundary exceptions that must be converted to false with warning context.")]
  internal bool TryWriteSuppressed(
    bool isSuppressed,
    Action<bool> writeSuppressed,
    Action<Exception> logWarning)
  {
    if (!IsAutoRetainerLoaded)
      return false;

    try
    {
      writeSuppressed(isSuppressed);
      return true;
    }
    catch (Exception ex)
    {
      logWarning(ex);
      return false;
    }
  }
}
