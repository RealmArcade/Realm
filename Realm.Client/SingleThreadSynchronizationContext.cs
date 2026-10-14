using System;
using System.Collections.Concurrent;
using System.Threading;
using Godot;

namespace Realm.Client;

public class SingleThreadSynchronizationContext : SynchronizationContext
{
	private readonly ConcurrentQueue<Action> _queue = new ConcurrentQueue<Action>();
	private IntPtr _targetHwnd = IntPtr.Zero;

	public void SetTargetHwnd(IntPtr hwnd)
	{
		_targetHwnd = hwnd;
	}

	public override void Post(SendOrPostCallback d, object? state)
	{
		_queue.Enqueue(() => d(state));
		if (_targetHwnd != IntPtr.Zero)
		{
			Realm.Client.Utils.WindowsNativeInterop.PostMessage(_targetHwnd, VSCodeManager.WM_WAKEUP, IntPtr.Zero, IntPtr.Zero);
		}
	}

	public override void Send(SendOrPostCallback d, object? state)
	{
		throw new NotSupportedException();
	}

	public void RunPending()
	{
		while (_queue.TryDequeue(out var action))
		{
			try
			{
				action();
			}
			catch (Exception ex)
			{
				GD.PrintErr("Error in SingleThreadSynchronizationContext: " + ex.Message);
			}
		}
	}
}