using System;
using System.Diagnostics;

namespace Realm.ListenServerHost;

/// <summary>
///     Watches the process that started this authority, so a match ends with the screen that was playing it. The
///     authority is reached on a port and keeps simulating whether or not anyone is connected, so a client that crashed,
///     was killed, or ended without sending its shutdown frame used to leave behind a process that held the port and
///     advanced a world no one could see until the machine was restarted.
///     The handle is opened once and kept: a released parent is still the process that was watched, while looking the id
///     up again could find a different process that has since reused it.
/// </summary>
public class ParentProcessWatchdog
{
    /// <summary>How often the parent is looked at. A match outlives a missed check by a second at most.</summary>
    public const double CheckIntervalSeconds = 1.0;

    private readonly int _parentProcessId;
    private Process? _parent;
    private double _nextCheckSeconds;
    private bool _parentGone;

    public ParentProcessWatchdog(int parentProcessId)
    {
        _parentProcessId = parentProcessId;
    }

    /// <summary>Whether this authority was started by a process that is being watched at all.</summary>
    public bool IsWatching => _parentProcessId > 0 && !_parentGone;

    /// <summary>
    ///     Answers whether the parent is gone, looking at most once per <see cref="CheckIntervalSeconds" />. An authority
    ///     started without a parent is never reported gone, which is what a dedicated or exported authority is.
    /// </summary>
    public bool Update(double nowSeconds)
    {
        if (_parentProcessId <= 0) return false;
        if (_parentGone) return true;
        if (nowSeconds < _nextCheckSeconds) return false;

        _nextCheckSeconds = nowSeconds + CheckIntervalSeconds;

        if (_parent == null && !TryOpen(out _parent))
        {
            _parentGone = true;
            return true;
        }

        try
        {
            if (!_parent!.HasExited) return false;
        }
        catch (InvalidOperationException)
        {
        }
        catch (System.ComponentModel.Win32Exception)
        {
        }

        _parentGone = true;
        return true;
    }

    private bool TryOpen(out Process? parent)
    {
        parent = null;

        try
        {
            parent = Process.GetProcessById(_parentProcessId);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    public void Dispose()
    {
        _parent?.Dispose();
        _parent = null;
    }
}
