using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Decos.Diagnostics.Trace
{
  /// <summary>
  /// Defines a trace listener that queues up log entries to be processed
  /// asynchronously.
  /// </summary>
  public abstract class AsyncTraceListener : TraceListenerBase
  {
    //> Berend #393684
    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    private static int? TryGetCurrentOsThreadId()
    {
      try
      {
        return unchecked((int)GetCurrentThreadId());
      }
      catch
      {
        return null;
      }
    }
    //< Berend #393684

    /// <summary>
    /// Initializes a new instance of the <see cref="AsyncTraceListener"/>
    /// class.
    /// </summary>
    public AsyncTraceListener()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AsyncTraceListener"/>
    /// class with the specified name.
    /// </summary>
    /// <param name="name">The name of the trace listener.</param>
    public AsyncTraceListener(string name)
        : base(name)
    {
    }

    /// <summary>
    /// Gets a value indicating whether the trace listener is thread safe.
    /// </summary>
    public override bool IsThreadSafe => true;

    /// <summary>
    /// Gets or sets the delay in milliseconds in between checking whether
    /// there are log entries to send.
    /// </summary>
    public int EmptyQueueDelay { get; set; } = 100;

    /// <summary>
    /// Gets a queue that contains the log entries to be written.
    /// </summary>
    protected ConcurrentQueue<LogEntry> RequestQueue { get; }
        = new ConcurrentQueue<LogEntry>();

    /// <summary>
    /// Sends log entries that have been queued up.
    /// </summary>
    /// <param name="shutdownToken">
    /// A token to monitor for graceful shutdown requests. When cancellation
    /// is requested, the task will complete after the queue has been
    /// emptied.
    /// </param>
    /// <param name="cancellationToken">
    /// A token to monitor for cancellation requests. When cancellation is
    /// requested, the current operation will be aborted and the queue will
    /// not be finished.
    /// </param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public virtual async Task ProcessQueueAsync(CancellationToken shutdownToken, CancellationToken cancellationToken)
    {
      //> Berend #393684: track live loops so a leak (more than one loop per
      // listener) is directly observable via ActiveProcessingLoopCount.
      Interlocked.Increment(ref _activeProcessingLoops);
      try
      {
        //< Berend #393684
        while (!cancellationToken.IsCancellationRequested)
        {
          while (RequestQueue.TryDequeue(out var logEntry))
          {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
              await TraceAsync(logEntry, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
              throw;
            }
            catch (Exception ex)
            {
              Console.Error.WriteLine($"Unhandled exception in {GetType().Name}.TraceAsync: {ex}.\n\nLog entry: {logEntry}");
            }
          }

          if (RequestQueue.Count == 0 && shutdownToken.IsCancellationRequested)
            break;

          try
          {
            await Task.Delay(EmptyQueueDelay, shutdownToken);
          }
          catch (OperationCanceledException) { }
        }
      }
      finally
      {
        Interlocked.Decrement(ref _activeProcessingLoops);
      }
      //< Berend #393684
    }

    //> Berend #393684
    private int _processingStarted;
    private Task _processingTask;
    private int _activeProcessingLoops;

    /// <summary>
    /// Gets the number of <see cref="ProcessQueueAsync"/> loops currently
    /// running for this listener. With the single-loop guard in place this
    /// is at most 1, no matter how many factories or callers requested
    /// processing (ticket #393684). Intended for diagnostics and tests.
    /// </summary>
    public int ActiveProcessingLoopCount
    {
      get { return Volatile.Read(ref _activeProcessingLoops); }
    }

    /// <summary>
    /// Ensures the queue-processing loop for this listener is started
    /// exactly once, regardless of how many callers request it.
    /// </summary>
    /// <remarks>
    /// A listener registered in the process-wide <see
    /// cref="System.Diagnostics.Trace.Listeners"/> collection is reused by
    /// every <see cref="TraceSourceLogFactory"/> instance. Starting the loop
    /// once per factory therefore leaked one non-terminating <see
    /// cref="ProcessQueueAsync"/> loop - and its <see cref="Task.Delay"/>
    /// timer, <see cref="CancellationTokenSource"/> and execution context -
    /// per factory (ticket #393684). This guard collapses those to a single
    /// loop per listener. The first caller establishes the loop and owns its
    /// tokens; later callers receive the already-running task and their
    /// tokens are ignored.
    /// <para>
    /// The loop is started using a lock-free <see cref="Interlocked"/> gate,
    /// so <see cref="ProcessQueueAsync"/> (the only extensibility point) is
    /// never invoked while a lock is held, and this method never blocks on
    /// the asynchronous loop. It is therefore safe to call from synchronous
    /// code, including the classic ASP.NET request pipeline.
    /// </para>
    /// </remarks>
    /// <param name="shutdownToken">
    /// A token to monitor for graceful shutdown requests.
    /// </param>
    /// <param name="cancellationToken">
    /// A token to monitor for cancellation requests.
    /// </param>
    /// <param name="started">
    /// When this method returns, contains <c>true</c> if this call started
    /// the loop; otherwise <c>false</c>.
    /// </param>
    /// <returns>The task that represents the single processing loop.</returns>
    internal Task EnsureProcessing(CancellationToken shutdownToken, CancellationToken cancellationToken, out bool started)
    {
      if (Interlocked.CompareExchange(ref _processingStarted, 1, 0) == 0)
      {
        // This call won the race. Start the loop outside of any lock;
        // ProcessQueueAsync returns at its first await, so the loop is not
        // run to completion here. Then publish the task for other callers.
        Task task = ProcessQueueAsync(shutdownToken, cancellationToken);
        Volatile.Write(ref _processingTask, task);
        started = true;
        return task;
      }

      // Another call already started (or is starting) the loop. Wait for the
      // winner to publish the task - a few spins at most - without ever
      // blocking on the loop itself.
      started = false;
      SpinWait spin = new SpinWait();
      Task existing;
      while ((existing = Volatile.Read(ref _processingTask)) == null)
        spin.SpinOnce();
      return existing;
    }
    //< Berend #393684

    /// <summary>
    /// When overridden in a derived class, processes a log entry to be
    /// written.
    /// </summary>
    /// <param name="logEntry">The log entry to be written.</param>
    /// <param name="cancellationToken">
    /// A token to monitor for cancellation requests.
    /// </param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public abstract Task TraceAsync(LogEntry logEntry, CancellationToken cancellationToken);

    /// <summary>
    /// Enqueues trace information and a message to the queue for
    /// asynchronous processing.
    /// </summary>
    /// <param name="e">
    /// A <see cref="TraceListenerBase.TraceEventData"/> object that contains
    /// information about the trace event.
    /// </param>
    /// <param name="message">The message to write.</param>
    protected override void TraceInternal(TraceEventData e, string message)
    {
      var logEntry = new LogEntry
      {
        Level = e.Type.ToLogLevel(),
        Source = e.Source,
        Message = message,
        EventId = e.ID,
        ProcessId = e.Cache.ProcessId,
        ThreadId = e.Cache.ThreadId,
        OsThreadId = TryGetCurrentOsThreadId(), // Berend #393684
        CustomerId = e.CustomerID,
        SessionId = e.SessionID
      };

      RequestQueue.Enqueue(logEntry);
    }

    /// <summary>
    /// Enqueues trace information and data to the queue for
    /// asynchronous processing.
    /// </summary>
    /// <param name="e">
    /// A <see cref="TraceListenerBase.TraceEventData"/> object that contains
    /// information about the trace event.
    /// </param>
    /// <param name="data">The trace data to write.</param>
    protected override void TraceInternal(TraceEventData e, object data)
    {
      if (data != null && data is LogData logData)
      {
        TraceInternal(e, logData);
        return;
      }

      var logEntry = new LogEntry
      {
        Level = e.Type.ToLogLevel(),
        Source = e.Source,
        EventId = e.ID,
        ProcessId = e.Cache.ProcessId,
        ThreadId = e.Cache.ThreadId,
        CustomerId = e.CustomerID,
        SessionId = e.SessionID
      };
      if (data is CustomerLogData)
      {
        var content = CustomerLogData.TryExtractCustomerId(data, out Guid? customerId);
        if (content.GetType() == typeof(String) || content.GetType() == typeof(string))
          logEntry.Message = content.ToString();
        else if (content is LogData dataLogData)
        {
          logEntry.Message = dataLogData.Message;
          logEntry.Data = dataLogData.Data;
        }
        else
          logEntry.Data = content;
      }
      else
      {
        logEntry.Data = data;
      }

      RequestQueue.Enqueue(logEntry);
    }

    /// <summary>
    /// Enqueues trace information and exception data to the queue for
    /// asynchronous processing.
    /// </summary>
    /// <param name="e">
    /// A <see cref="TraceListenerBase.TraceEventData"/> object that contains
    /// information about the trace event.
    /// </param>
    /// <param name="data">The log data to write.</param>
    protected void TraceInternal(TraceEventData e, LogData data)
    {
      var logEntry = new LogEntry
      {
        Level = e.Type.ToLogLevel(),
        Source = e.Source,
        Message = data.Message,
        Data = data.Data,
        EventId = e.ID,
        ProcessId = e.Cache.ProcessId,
        ThreadId = e.Cache.ThreadId,
        CustomerId = e.CustomerID,
        SessionId = e.SessionID
      };

      RequestQueue.Enqueue(logEntry);
    }
  }
}