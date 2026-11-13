using Sahar.Core.Activities;
using Sahar.Engine.Diagnostics;
using Sahar.Engine.Senses;
using Windows.System;

namespace Sahar.Engine.Activities;

/// <summary>
/// The PC (spec 0.13 §2 System, an approved extra): the volume (a 1.5 s moment when it changes, never at start),
/// the battery (low and charging moments, the time to full from Windows' battery report) and who uses the camera or
/// the microphone (the app's name is shown, never logged). The volume is read every half second through Core Audio's
/// IAudioEndpointVolume (a plain call; no callback object to keep alive).
/// </summary>
internal sealed unsafe class SystemSource
{
    private static readonly Guid ClsidEnumerator = new("BCDE0395-E52F-467C-8E3D-C4579291692E");
    private static readonly Guid IidEnumerator = new("A95664D2-9614-4F35-A746-DE8DB63617E6");
    private static readonly Guid IidEndpointVolume = new("5CDF2C82-841E-4546-9722-0CF74078229A");

    private readonly ActivityHub _hub;
    private readonly CameraMic _cameraMic;
    private nint _volume;
    private int? _level;
    private bool? _muted;
    private int _failures;
    private int? _battery;
    private bool _charging;

    public SystemSource(DispatcherQueue ui, ActivityHub hub, CameraMic cameraMic)
    {
        _hub = hub;
        _cameraMic = cameraMic;
        var t = ui.CreateTimer();
        t.Interval = TimeSpan.FromMilliseconds(500);
        t.Tick += (_, _) => Safe(ReadVolume);
        t.Start();
        cameraMic.Changed += _ => Safe(ShowCameraMic);
    }

    public int? Volume => _level;

    private void ReadVolume()
    {
        if (_volume == 0 && !Acquire()) return;
        float v;
        int muted;
        if (((delegate* unmanaged[Stdcall]<nint, float*, int>)Com.Slot(_volume, 9))(_volume, &v) < 0
            || ((delegate* unmanaged[Stdcall]<nint, int*, int>)Com.Slot(_volume, 15))(_volume, &muted) < 0)
        {
            Com.Release(ref _volume);   // the default device changed: take the new one next time
            return;
        }
        int level = (int)Math.Round(v * 100);
        bool mute = muted != 0;
        if (_level is { } old && (old != level || _muted != mute))
        {
            _hub.Moment(new Activity("volume", ActivityKind.Volume, ActivityText.Volume, mute ? "٠" : ActivityText.N(level), "speaker", mute ? 0 : level / 100f), Card);
        }
        _level = level;
        _muted = mute;
    }

    private bool Acquire()
    {
        if (_failures > 20) return false;
        nint enumerator = 0, device = 0, volume = 0;
        try
        {
            Guid clsid = ClsidEnumerator, iid = IidEnumerator, iidVol = IidEndpointVolume;
            if (ActCom.CoCreateInstance(&clsid, 0, 0x17, &iid, &enumerator) < 0) { _failures++; return false; }
            // IMMDeviceEnumerator::GetDefaultAudioEndpoint(eRender, eMultimedia, IMMDevice**)
            if (((delegate* unmanaged[Stdcall]<nint, int, int, nint*, int>)Com.Slot(enumerator, 4))(enumerator, 0, 1, &device) < 0) { _failures++; return false; }
            // IMMDevice::Activate(REFIID, CLSCTX_ALL, PROPVARIANT* = null, void**)
            if (((delegate* unmanaged[Stdcall]<nint, Guid*, uint, nint, nint*, int>)Com.Slot(device, 3))(device, &iidVol, 0x17, 0, &volume) < 0) { _failures++; return false; }
            _volume = volume;
            volume = 0;
            return true;
        }
        finally
        {
            Com.Release(ref device);
            Com.Release(ref enumerator);
            Com.Release(ref volume);
        }
    }

    /// <summary>The battery from <see cref="Power"/>: a low moment, and a charging moment when the charger goes in.</summary>
    public void Battery(int percent, bool charging, bool low, bool plugged)
    {
        _battery = percent;
        _charging = charging;
        if (low) _hub.Moment(new Activity("battery", ActivityKind.BatteryLow, $"البطارية {ActivityText.Percent(percent)}", null, "battery"), Card);
        if (plugged) _hub.Moment(new Activity("charging", ActivityKind.Charging, ActivityText.Charging, ActivityText.Percent(percent), "bolt"), Card);
    }

    private void ShowCameraMic()
    {
        if (!_cameraMic.InUse) { _hub.Remove("cameramic"); return; }
        var text = _cameraMic.CameraApp is not null ? ActivityText.Camera : ActivityText.Mic;
        _hub.Set(new Activity("cameramic", ActivityKind.CameraMic, text, null, _cameraMic.CameraApp is not null ? "camera" : "mic"), Card);
    }

    private CardData? Card()
    {
        TimeSpan? toFull = null;
        try
        {
            var r = Windows.Devices.Power.Battery.AggregateBattery.GetReport();
            if (r.RemainingCapacityInMilliwattHours is int rem && r.FullChargeCapacityInMilliwattHours is int full && r.ChargeRateInMilliwatts is int rate)
                toFull = SystemNow.ToFull(rem, full, rate);
        }
        catch (Exception e) { Log.Write($"system: battery report failed ({e.GetType().Name})"); }
        return new CardData(CardKind.System, System: new SystemCard(_muted == true ? 0 : _level, _battery, _charging, _charging ? toFull : null,
            _cameraMic.MicApp, _cameraMic.CameraApp));
    }

    private static void Safe(Action a)
    {
        try { a(); }
        catch (Exception e) { Log.Write($"system source error: {e.GetType().Name}"); }
    }
}
