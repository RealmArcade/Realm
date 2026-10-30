using System;
using System.Diagnostics;

namespace Realm.ListenServerHost;

public class ParentProcessWatchdog
{
    public const double CheckIntervalSeconds = 1.0;

    private readonly int _parentProcessId;
    private Process? _parent;
    private double _nextCheckSeconds;
    private bool _parentGone;

    public ParentProcessWatchdog(int parentProcessId)
    {
        _parentProcessId = parentProcessId;
    }

    public bool IsWatching => _parentProcessId > 0 && !_parentGone;

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
