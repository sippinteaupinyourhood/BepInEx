using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Threading;
using BepInEx.Logging;
using UnityEngine;
using Logger = BepInEx.Logging.Logger;

namespace BepInEx.Unity.Mono;




public sealed class ThreadingHelper : MonoBehaviour, ISynchronizeInvoke
{
    private readonly object _invokeLock = new();
    private Action _invokeList;
    private Thread _mainThread;

    
    
    
    public static ThreadingHelper Instance { get; private set; }

    
    
    
    
    
    
    public static ISynchronizeInvoke SynchronizingObject => Instance;

    private void Update()
    {
        
        if (_mainThread == null)
            _mainThread = Thread.CurrentThread;

        
        if (_invokeList == null) return;

        Action toRun;
        lock (_invokeLock)
        {
            toRun = _invokeList;
            _invokeList = null;
        }

        
        
        foreach (var action in toRun.GetInvocationList().Cast<Action>())
            try
            {
                action();
            }
            catch (Exception ex)
            {
                LogInvocationException(ex);
            }
    }

    internal static void Initialize()
    {
        var go = new GameObject("BepInEx_ThreadingHelper") { hideFlags = HideFlags.HideAndDontSave };
        DontDestroyOnLoad(go);
        Instance = go.AddComponent<ThreadingHelper>();
    }

    
    
    
    public void StartSyncInvoke(Action action)
    {
        if (action == null) throw new ArgumentNullException(nameof(action));

        lock (_invokeLock)
        {
            _invokeList += action;
        }
    }

    
    
    
    
    
    
    
    
    public void StartAsyncInvoke(Func<Action> action)
    {
        void DoWork(object _)
        {
            try
            {
                var result = action();

                if (result != null)
                    StartSyncInvoke(result);
            }
            catch (Exception ex)
            {
                LogInvocationException(ex);
            }
        }

        if (!ThreadPool.QueueUserWorkItem(DoWork))
            throw new NotSupportedException("Failed to queue the action on ThreadPool");
    }

    private static void LogInvocationException(Exception ex)
    {
        Logger.Log(LogLevel.Error, ex);
        if (ex.InnerException != null) Logger.Log(LogLevel.Error, $"INNER: {ex.InnerException}");
    }

    #region ISynchronizeInvoke

    IAsyncResult ISynchronizeInvoke.BeginInvoke(Delegate method, object[] args)
    {
        var result = new InvokeResult();

        object Invoke()
        {
            try
            {
                return method.DynamicInvoke(args);
            }
            catch (Exception ex)
            {
                result.ExceptionThrown = true;
                return ex;
            }
        }

        if (!InvokeRequired)
            result.Finish(Invoke(), true);
        else
            StartSyncInvoke(() => result.Finish(Invoke(), false));

        return result;
    }

    object ISynchronizeInvoke.EndInvoke(IAsyncResult result)
    {
        var invokeResult = (InvokeResult) result;
        invokeResult.AsyncWaitHandle.WaitOne();

        if (invokeResult.ExceptionThrown)
            throw (Exception) invokeResult.AsyncState;
        return invokeResult.AsyncState;
    }

    object ISynchronizeInvoke.Invoke(Delegate method, object[] args)
    {
        var invokeResult = ((ISynchronizeInvoke) this).BeginInvoke(method, args);
        return ((ISynchronizeInvoke) this).EndInvoke(invokeResult);
    }

    
    
    
    
    
    public bool InvokeRequired => _mainThread == null || _mainThread != Thread.CurrentThread;

    private sealed class InvokeResult : IAsyncResult
    {
        internal bool ExceptionThrown;

        public InvokeResult()
        {
            AsyncWaitHandle = new EventWaitHandle(false, EventResetMode.ManualReset);
        }

        public bool IsCompleted { get; private set; }
        public WaitHandle AsyncWaitHandle { get; }
        public object AsyncState { get; private set; }
        public bool CompletedSynchronously { get; private set; }

        public void Finish(object result, bool completedSynchronously)
        {
            AsyncState = result;
            CompletedSynchronously = completedSynchronously;
            IsCompleted = true;
            ((EventWaitHandle) AsyncWaitHandle).Set();
        }
    }

    #endregion
}




public static class ThreadingExtensions
{
    
    public static IEnumerable<TOut> RunParallel<TIn, TOut>(this IEnumerable<TIn> data,
                                                           Func<TIn, TOut> work,
                                                           int workerCount = -1)
    {
        foreach (var result in RunParallel(data.ToList(), work))
            yield return result;
    }

    
    
    
    
    
    
    
    
    
    
    
    
    
    
    public static IEnumerable<TOut> RunParallel<TIn, TOut>(this IList<TIn> data,
                                                           Func<TIn, TOut> work,
                                                           int workerCount = -1)
    {
        if (workerCount < 0)
            workerCount = Mathf.Max(2, Environment.ProcessorCount);
        else if (workerCount == 0)
            throw new ArgumentException("Need at least 1 worker", nameof(workerCount));

        var perThreadCount = Mathf.CeilToInt(data.Count / (float) workerCount);
        var doneCount = 0;

        var lockObj = new object();
        var are = new ManualResetEvent(false);
        IEnumerable<TOut> doneItems = null;
        Exception exceptionThrown = null;

        
        for (var i = 0; i < workerCount; i++)
        {
            var first = i * perThreadCount;
            var last = Mathf.Min(first + perThreadCount, data.Count);
            ThreadPool.QueueUserWorkItem(
                                         _ =>
                                         {
                                             var results = new List<TOut>(perThreadCount);

                                             try
                                             {
                                                 for (var dataIndex = first; dataIndex < last; dataIndex++)
                                                 {
                                                     if (exceptionThrown != null) break;
                                                     results.Add(work(data[dataIndex]));
                                                 }
                                             }
                                             catch (Exception ex)
                                             {
                                                 exceptionThrown = ex;
                                             }

                                             lock (lockObj)
                                             {
                                                 doneItems = doneItems == null
                                                                 ? results
                                                                 : results.Concat(doneItems);
                                                 doneCount++;
                                                 are.Set();
                                             }
                                         });
        }

        
        while (true)
        {
            are.WaitOne();

            IEnumerable<TOut> toOutput;
            bool isDone;
            lock (lockObj)
            {
                toOutput = doneItems;
                doneItems = null;
                isDone = doneCount == workerCount;
            }

            if (toOutput != null)
                foreach (var doneItem in toOutput)
                    yield return doneItem;

            if (isDone)
                break;
        }

        if (exceptionThrown != null)
            throw new TargetInvocationException("An exception was thrown inside one of the threads",
                                                exceptionThrown);
    }

    
    
    
    
    
    
    
    
    
    
    public static void ForEachParallel<T>(this IList<T> data, Action<T> work, int workerCount = -1)
    {
        if (workerCount < 0)
            workerCount = Mathf.Max(2, Environment.ProcessorCount);
        else if (workerCount == 0)
            throw new ArgumentException("Need at least 1 worker", nameof(workerCount));

        var currentIndex = data.Count;

        var are = new ManualResetEvent(false);
        var runningCount = workerCount;
        Exception exceptionThrown = null;

        void DoWork(object _)
        {
            try
            {
                while (true)
                {
                    if (exceptionThrown != null)
                        return;

                    var decrementedIndex = Interlocked.Decrement(ref currentIndex);
                    if (decrementedIndex < 0)
                        return;

                    work(data[decrementedIndex]);
                }
            }
            catch (Exception ex)
            {
                exceptionThrown = ex;
            }
            finally
            {
                var decCount = Interlocked.Decrement(ref runningCount);
                if (decCount <= 0)
                    are.Set();
            }
        }

        
        for (var i = 0; i < workerCount - 1; i++)
            ThreadPool.QueueUserWorkItem(DoWork);

        DoWork(null);

        are.WaitOne();

        if (exceptionThrown != null)
            throw new TargetInvocationException("An exception was thrown inside one of the threads", exceptionThrown);
    }
}
