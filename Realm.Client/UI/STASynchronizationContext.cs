using System;
using System.Collections.Concurrent;
using System.Threading;

namespace Realm.Client.UI;

public class STASynchronizationContext : SynchronizationContext
{
	private readonly ConcurrentQueue<Action> _queue;
	private readonly IntPtr _hwnd;
	private readonly int _threadId;

	public STASynchronizationContext(ConcurrentQueue<Action> queue, IntPtr hwnd, int threadId)
	{
		_queue = queue;
		_hwnd = hwnd;
		_threadId = threadId;
	}

	public override void Post(SendOrPostCallback d, object? state)
	{
		_queue.Enqueue(() => d(state));
		if (_hwnd != IntPtr.Zero)
		{
			Realm.Client.Utils.WindowsNativeInterop.PostMessage(_hwnd, WasmConsoleWindow.WM_WAKEUP, IntPtr.Zero, IntPtr.Zero);
		}
	}

	public override void Send(SendOrPostCallback d, object? state)
	{
		if (Environment.CurrentManagedThreadId == _threadId)
		{
			d(state);
		}
		else
		{
			using var evt = new ManualResetEventSlim(false);
			_queue.Enqueue(() =>
			{
				try
				{
					d(state);
				}
				finally
				{
					evt.Set();
				}
			});
			if (_hwnd != IntPtr.Zero)
			{
				Realm.Client.Utils.WindowsNativeInterop.PostMessage(_hwnd, WasmConsoleWindow.WM_WAKEUP, IntPtr.Zero, IntPtr.Zero);
			}

			evt.Wait();
		}
	}
}