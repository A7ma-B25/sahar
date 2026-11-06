using Sahar.Core.Brain;
using Sahar.Engine.Diagnostics;
using Windows.Networking.Connectivity;
using Windows.System;

namespace Sahar.Engine.Senses;

/// <summary>Internet on/off, from Windows' own connectivity status (no traffic of ours).</summary>
internal sealed class Network
{
    private readonly Action<EventKind> _post;
    private readonly DispatcherQueue _ui;
    private bool? _online;

    public Network(DispatcherQueue ui, Action<EventKind> post)
    {
        _ui = ui;
        _post = post;
        try
        {
            _online = IsOnline();
            NetworkInformation.NetworkStatusChanged += _ => _ui.TryEnqueue(Check);
        }
        catch (Exception e)
        {
            Log.Write($"network status unavailable: {e.Message}");
        }
    }

    private void Check()
    {
        bool online = IsOnline();
        if (online == _online) return;
        _online = online;
        _post(online ? EventKind.NetOnline : EventKind.NetOffline);
    }

    private static bool IsOnline() =>
        NetworkInformation.GetInternetConnectionProfile()?.GetNetworkConnectivityLevel() == NetworkConnectivityLevel.InternetAccess;
}
