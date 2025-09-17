using System;
using System.Collections.Generic;

public class Platform_Paramount_HassClimate_IP
{
    HassClimateHub _hub;
    readonly Dictionary<string, IThermostatCap> _thermos =
      new Dictionary<string, IThermostatCap>(StringComparer.OrdinalIgnoreCase);
    Settings _settings = new Settings
    {
        HaWsUrl = "ws://HOMEASSISTANT_IP:8123/api/websocket", // change in CH settings after install
        HaToken = "PASTE_TOKEN",
        ExposeAll = true
    };

    // Called by Crestron loader at startup
    public void Initialize()
    {
        if (string.IsNullOrWhiteSpace(_settings.HaWsUrl) || string.IsNullOrWhiteSpace(_settings.HaToken))
        {
            Log("Missing HA URL or token.");
            return;
        }

        _hub = new HassClimateHub(_settings.HaWsUrl, _settings.HaToken);
        _hub.Connected += () => Log("HA connected");
        _hub.Disconnected += () => Log("HA disconnected");
        _hub.Error += msg => Log("HA error: " + msg);

        _hub.ClimateAdded += OnClimateAdded;
        _hub.ClimateChanged += OnClimateChanged;
        _hub.ClimateRemoved += OnClimateRemoved;

        _hub.Start();
    }

    public void Deinitialize()
    {
        try { _hub?.Stop(); } catch { }
        _thermos.Clear();
    }

    void OnClimateAdded(string entityId, ClimateFeedback fb)
    {
        if (!ShouldExpose(entityId, fb.Name)) return;
        if (_thermos.ContainsKey(entityId)) return;

        var cap = CreateThermostatCapability(entityId, fb);
        _thermos[entityId] = cap;
        PushFeedback(cap, fb);
        Log($"[ADD] {entityId} '{fb.Name}' tgt={fb.TargetSummary}");
    }

    void OnClimateChanged(string entityId, ClimateFeedback fb)
    {
        if (_thermos.TryGetValue(entityId, out var cap))
            PushFeedback(cap, fb);
    }

    void OnClimateRemoved(string entityId)
    {
        if (_thermos.Remove(entityId))
            Log($"[DEL] {entityId}");
    }

    IThermostatCap CreateThermostatCapability(string entityId, ClimateFeedback fb)
    {
        // TODO: swap this stub for your DevKit thermostat capability
        var cap = new ThermostatCapStub(entityId);
        cap.Name = fb.Name ?? entityId;

        cap.OnModeSet += m => _hub.SetMode(entityId, MapModeToHa(m));
        cap.OnFanModeSet += f => _hub.SetFanMode(entityId, f);
        cap.OnSingleSetpointSet += t => _hub.SetSingleSetpoint(entityId, t);
        cap.OnRangeSet += (h, c) => _hub.SetHeatCool(entityId, h, c);
        return cap;
    }

    void PushFeedback(IThermostatCap cap, ClimateFeedback fb)
    {
        cap.SetHvacMode(MapModeToCh(fb.HvacMode));
        cap.SetAction(fb.Action ?? "idle");
        if (fb.CurrentTemperature.HasValue) cap.SetCurrentTemperature(fb.CurrentTemperature.Value);

        if (fb.TargetTemperature.HasValue)
        {
            cap.EnableRange(false);
            cap.SetTargetTemperature(fb.TargetTemperature.Value);
        }
        else
        {
            cap.EnableRange(true);
            if (fb.HeatSetpoint.HasValue) cap.SetHeatSetpoint(fb.HeatSetpoint.Value);
            if (fb.CoolSetpoint.HasValue) cap.SetCoolSetpoint(fb.CoolSetpoint.Value);
        }

        cap.SetMinMax(fb.MinTemp ?? 5, fb.MaxTemp ?? 35);
        cap.SetStep(fb.Step > 0 ? fb.Step : 0.5);
        if (!string.IsNullOrEmpty(fb.FanMode)) cap.SetFanMode(fb.FanMode);
        cap.SetSupportedHvacModes(fb.SupportedHvacModes);
        cap.SetSupportedFanModes(fb.SupportedFanModes);
        cap.SetUnits(fb.TemperatureUnit ?? "°C");
    }

    bool ShouldExpose(string entityId, string friendly)
    {
        if (_settings.ExposeAll) return true;
        if (string.IsNullOrWhiteSpace(_settings.IncludeFilterCsv)) return false;
        foreach (var raw in _settings.IncludeFilterCsv.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var s = raw.Trim();
            if (s.Equals(entityId, StringComparison.OrdinalIgnoreCase)) return true;
            if (!string.IsNullOrEmpty(friendly) && s.Equals(friendly, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    string MapModeToCh(string ha)
    {
        if (string.IsNullOrEmpty(ha)) return "off";
        var m = ha.ToLowerInvariant();
        if (m == "heat_cool") return "auto";
        return m;
    }

    string MapModeToHa(string ch)
    {
        if (string.IsNullOrEmpty(ch)) return "off";
        var m = ch.ToLowerInvariant();
        if (m == "auto") return "heat_cool";
        return m;
    }

    void Log(string s) => System.Diagnostics.Debug.WriteLine("[HassClimate] " + s);
}

// ---- temporary stubs so this compiles before you wire real DevKit capability ----
public interface IThermostatCap
{
    string Name { get; set; }
    event Action<string> OnModeSet;
    event Action<string> OnFanModeSet;
    event Action<double> OnSingleSetpointSet;
    event Action<double, double> OnRangeSet;

    void SetHvacMode(string mode);
    void SetAction(string action);
    void SetCurrentTemperature(double c);
    void SetTargetTemperature(double c);
    void SetHeatSetpoint(double c);
    void SetCoolSetpoint(double c);
    void EnableRange(bool enable);
    void SetMinMax(double min, double max);
    void SetStep(double step);
    void SetFanMode(string mode);
    void SetSupportedHvacModes(System.Collections.Generic.List<string> modes);
    void SetSupportedFanModes(System.Collections.Generic.List<string> modes);
    void SetUnits(string units);
}

public class ThermostatCapStub : IThermostatCap
{
    public string Name { get; set; }
    public event Action<string> OnModeSet;
    public event Action<string> OnFanModeSet;
    public event Action<double> OnSingleSetpointSet;
    public event Action<double, double> OnRangeSet;
    public ThermostatCapStub(string id) { Name = id; }
    public void SetHvacMode(string mode) { }
    public void SetAction(string action) { }
    public void SetCurrentTemperature(double c) { }
    public void SetTargetTemperature(double c) { }
    public void SetHeatSetpoint(double c) { }
    public void SetCoolSetpoint(double c) { }
    public void EnableRange(bool enable) { }
    public void SetMinMax(double min, double max) { }
    public void SetStep(double step) { }
    public void SetFanMode(string mode) { }
    public void SetSupportedHvacModes(System.Collections.Generic.List<string> modes) { }
    public void SetSupportedFanModes(System.Collections.Generic.List<string> modes) { }
    public void SetUnits(string units) { }
}
