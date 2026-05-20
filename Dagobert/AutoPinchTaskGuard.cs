using System;
using System.Diagnostics.CodeAnalysis;

namespace Dagobert;

internal sealed class AutoPinchTaskGuard(
  AutoRetainerSuppressionCoordinator suppressionCoordinator,
  Action abortTasks,
  Action removeTalkAddonListeners,
  Action<Exception, string> logException)
{
  internal const string SuppressionFailureDataKey = "AutoPinchTaskGuard.SuppressionFailure";
  internal const string AbortFailureDataKey = "AutoPinchTaskGuard.AbortFailure";
  internal const string ListenerCleanupFailureDataKey = "AutoPinchTaskGuard.ListenerCleanupFailure";
  internal const string LogFailureDataKey = "AutoPinchTaskGuard.LogFailure";

  internal bool? Run(Func<bool?> task, string taskName)
  {
    try
    {
      return task();
    }
    catch (Exception ex)
    {
      EndSuppressionRun(ex);
      RunCleanup(abortTasks, ex, AbortFailureDataKey);
      RunCleanup(removeTalkAddonListeners, ex, ListenerCleanupFailureDataKey);
      RunCleanup(() => logException(ex, taskName), ex, LogFailureDataKey);
      throw;
    }
  }

  private void EndSuppressionRun(Exception taskException)
  {
    var hadActiveSession = suppressionCoordinator.HasActiveSession;

    if (!suppressionCoordinator.EndRun() && hadActiveSession)
      taskException.Data[SuppressionFailureDataKey] = true;
  }

  [SuppressMessage(
    "Design",
    "CA1031:Do not catch general exception types",
    Justification = "Cleanup failures are attached to the original task exception so the guard can rethrow the task failure.")]
  private static void RunCleanup(Action cleanup, Exception taskException, string dataKey)
  {
    try
    {
      cleanup();
    }
    catch (Exception cleanupException)
    {
      taskException.Data[dataKey] = cleanupException;
    }
  }
}
