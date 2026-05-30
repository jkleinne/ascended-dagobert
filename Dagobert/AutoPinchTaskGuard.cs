using System;
using System.Diagnostics.CodeAnalysis;

namespace Dagobert;

internal sealed class AutoPinchTaskGuard(
  AutoRetainerSuppressionCoordinator suppressionCoordinator,
  Action abortTasks,
  Action removeTalkAddonListeners,
  Action<Exception, string> logException,
  Action<TimeoutException, string, int> reportTimeout,
  Func<long> getTickCount)
{
  internal const string SuppressionFailureDataKey = "AutoPinchTaskGuard.SuppressionFailure";
  internal const string AbortFailureDataKey = "AutoPinchTaskGuard.AbortFailure";
  internal const string ListenerCleanupFailureDataKey = "AutoPinchTaskGuard.ListenerCleanupFailure";
  internal const string LogFailureDataKey = "AutoPinchTaskGuard.LogFailure";

  private string? _activeTaskName;
  private long _activeTaskStartedAt;

  internal bool? Run(Func<bool?> task, string taskName, int timeoutMs)
  {
    ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeoutMs, 0);

    try
    {
      StartTimingIfNeeded(taskName);
      var result = task();

      if (result == false)
      {
        if (HasTimedOut(timeoutMs))
          return HandleTimeout(taskName, timeoutMs);

        return false;
      }

      ClearTiming();
      return result;
    }
    catch (Exception ex)
    {
      ClearTiming();
      HandleException(ex, taskName);
      throw;
    }
  }

  private void StartTimingIfNeeded(string taskName)
  {
    if (_activeTaskName == taskName)
      return;

    _activeTaskName = taskName;
    _activeTaskStartedAt = getTickCount();
  }

  private bool HasTimedOut(int timeoutMs)
  {
    return getTickCount() - _activeTaskStartedAt >= timeoutMs;
  }

  private bool? HandleTimeout(string taskName, int timeoutMs)
  {
    ClearTiming();
    var timeoutException = new TimeoutException($"Auto pinch task {taskName} timed out after {timeoutMs} ms");
    EndSuppressionRun(timeoutException);
    RunCleanup(removeTalkAddonListeners, timeoutException, ListenerCleanupFailureDataKey);
    RunCleanup(() => reportTimeout(timeoutException, taskName, timeoutMs), timeoutException, LogFailureDataKey);
    return null;
  }

  private void HandleException(Exception ex, string taskName)
  {
    EndSuppressionRun(ex);
    RunCleanup(abortTasks, ex, AbortFailureDataKey);
    RunCleanup(removeTalkAddonListeners, ex, ListenerCleanupFailureDataKey);
    RunCleanup(() => logException(ex, taskName), ex, LogFailureDataKey);
  }

  private void ClearTiming()
  {
    _activeTaskName = null;
    _activeTaskStartedAt = 0;
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
    Justification = "Cleanup failures are attached to the original task exception so the guard can rethrow the task failure or report the timeout failure.")]
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
