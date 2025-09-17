using Newtonsoft.Json.Linq;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

public class HassClimateHub : IDisposable
{
    readonly HassWebSocket _ha;
    readonly ConcurrentDictionary<string, ClimateFeedback> _climates =
      new ConcurrentDictionary<string, ClimateFeedback>(StringComparer.OrdinalIgnoreCase);
    readonly ConcurrentDictionary<string, ClimateFeedback> _last =
      new ConcurrentDictionary<string, ClimateFeedback>(StringComparer.OrdinalIgnoreCase);


    public event Action<string, ClimateFeedback> ClimateAdded;     // entity_id, state
    public event Action<string> ClimateRemoved;                     // entity_id
    public event Action<string, ClimateFeedback> ClimateChanged;   // entity_id, state
    public IReadOnlyDictionary<string, ClimateFeedback> Climates => _climates;

    public event Action Connected;
    public event Action Disconnected;
    public event Action<string> Error;

    public HassClimateHub(string wsUrl, string token)
    {
        _ha = new HassWebSocket(wsUrl, token);
        _ha.InitialStates += OnInitialStates;
        _ha.StateChanged += OnStateChanged;

        // forward lifecycle + errors
        _ha.Connected += () => Connected?.Invoke();
        _ha.Disconnected += () => Disconnected?.Invoke();
        _ha.Error += msg => Error?.Invoke(msg);
    }

    public void Start() { _ha.Connect(); }
    public void Stop() { _ha.Disconnect(); }
    public void Dispose() { Stop(); }

    // ===== Commands (CH -> HA) =====
    public void SetMode(string entityId, string mode)
    {
        if (string.IsNullOrWhiteSpace(entityId) || string.IsNullOrWhiteSpace(mode)) return;
        _ha.CallClimateService("set_hvac_mode", entityId, new JObject { ["hvac_mode"] = mode });
    }

    public void SetFanMode(string entityId, string fan)
    {
        if (string.IsNullOrWhiteSpace(entityId) || string.IsNullOrWhiteSpace(fan)) return;
        _ha.CallClimateService("set_fan_mode", entityId, new JObject { ["fan_mode"] = fan });
    }

    public void SetSingleSetpoint(string entityId, double temperatureC)
    {
        if (!_climates.TryGetValue(entityId, out var fb)) return;
        var t = RoundToStep(Clamp(temperatureC, fb.MinTemp, fb.MaxTemp), fb.Step);
        _ha.CallClimateService("set_temperature", entityId,
          new Newtonsoft.Json.Linq.JObject { ["temperature"] = t });
    }

    public void SetHeatCool(string entityId, double heatC, double coolC)
    {
        if (!_climates.TryGetValue(entityId, out var fb)) return;
        var h = RoundToStep(Clamp(heatC, fb.MinTemp, fb.MaxTemp), fb.Step);
        var c = RoundToStep(Clamp(coolC, fb.MinTemp, fb.MaxTemp), fb.Step);
        _ha.CallClimateService("set_temperature", entityId,
          new Newtonsoft.Json.Linq.JObject { ["target_temp_low"] = h, ["target_temp_high"] = c });
    }

    static double Clamp(double v, double? min, double? max)
    {
        if (min.HasValue && v < min.Value) v = min.Value;
        if (max.HasValue && v > max.Value) v = max.Value;
        return v;
    }

    static double RoundToStep(double v, double step)
    {
        if (step <= 0) step = 0.5;
        var n = Math.Round(v / step, MidpointRounding.AwayFromZero);
        return n * step;
    }

    public void TurnOn(string entityId) { if (!string.IsNullOrWhiteSpace(entityId)) _ha.CallClimateService("turn_on", entityId); }
    public void TurnOff(string entityId) { if (!string.IsNullOrWhiteSpace(entityId)) _ha.CallClimateService("turn_off", entityId); }

    // ===== State wiring (HA -> CH) =====
    void OnInitialStates(JArray states)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var s in states)
        {
            var eid = (string)s["entity_id"];
            if (string.IsNullOrEmpty(eid) || !eid.StartsWith("climate.", StringComparison.OrdinalIgnoreCase)) continue;
            seen.Add(eid);
            var fb = ParseClimate((JObject)s);
            var added = _climates.TryAdd(eid, fb);
            if (added) ClimateAdded?.Invoke(eid, fb);
            else
            {
                _climates[eid] = fb;
                ClimateChanged?.Invoke(eid, fb);
            }
        }

        // Optionally prune climates that disappeared (rare at boot but safe)
        foreach (var existing in _climates.Keys)
        {
            if (!seen.Contains(existing))
            {
                if (_climates.TryRemove(existing, out _)) ClimateRemoved?.Invoke(existing);
            }
        }
    }

    void OnStateChanged(JObject ev)
    {
        var data = (JObject)ev["data"];
        var eid = (string)data?["entity_id"];
        if (string.IsNullOrEmpty(eid) || !eid.StartsWith("climate.", StringComparison.OrdinalIgnoreCase)) return;

        var newState = (JObject)data["new_state"];
        if (newState == null)
        {
            if (_climates.TryRemove(eid, out _)) ClimateRemoved?.Invoke(eid);
            _last.TryRemove(eid, out _);
            return;
        }

        var fb = ParseClimate(newState);
        _climates[eid] = fb;

        var changed = HasMeaningfulChange(eid, fb);
        _last[eid] = fb;

        if (!_last.ContainsKey(eid)) ClimateAdded?.Invoke(eid, fb);
        else if (changed) ClimateChanged?.Invoke(eid, fb);
    }

    bool HasMeaningfulChange(string eid, ClimateFeedback fb)
    {
        if (!_last.TryGetValue(eid, out var prev)) return true;

        bool diff =
          !Eq(fb.HvacMode, prev.HvacMode) ||
          !Eq(fb.FanMode, prev.FanMode) ||
          !Eq(fb.Action, prev.Action) ||
          !Eq(fb.CurrentTemperature, prev.CurrentTemperature, 0.01) ||
          !Eq(fb.TargetTemperature, prev.TargetTemperature, 0.01) ||
          !Eq(fb.HeatSetpoint, prev.HeatSetpoint, 0.01) ||
          !Eq(fb.CoolSetpoint, prev.CoolSetpoint, 0.01);

        return diff;
    }

    static bool Eq(string a, string b) => string.Equals(a ?? "", b ?? "", StringComparison.OrdinalIgnoreCase);
    static bool Eq(double? a, double? b, double tol) =>
      a.HasValue == b.HasValue && (!a.HasValue || Math.Abs(a.Value - b.Value) <= tol);


    ClimateFeedback ParseClimate(Newtonsoft.Json.Linq.JObject stateObj)
    {
        var fb = new ClimateFeedback();
        var attrs = (Newtonsoft.Json.Linq.JObject)stateObj["attributes"];

        fb.EntityId = (string)stateObj["entity_id"];
        fb.Name = (string)attrs?["friendly_name"] ?? fb.EntityId;

        fb.HvacMode = (string)stateObj["state"];          // heat|cool|heat_cool|off
        fb.Action = (string)attrs?["hvac_action"];         // heating|cooling|idle|off
        fb.FanMode = (string)attrs?["fan_mode"];

        fb.CurrentTemperature = D(attrs, "current_temperature");

        // Single target (Office-style)
        fb.TargetTemperature = D(attrs, "temperature") ?? D(attrs, "target_temperature");

        // Range targets (Downstairs/Upstairs/Theater AC)
        fb.HeatSetpoint = D(attrs, "target_temp_low") ?? D(attrs, "target_temperature_low");
        fb.CoolSetpoint = D(attrs, "target_temp_high") ?? D(attrs, "target_temperature_high");

        // Limits / step / units
        fb.MinTemp = D(attrs, "min_temp");
        fb.MaxTemp = D(attrs, "max_temp");
        fb.Step = D(attrs, "target_temp_step") ?? D(attrs, "precision") ?? 0.5;
        var unit = (string)attrs?["temperature_unit"] ?? (string)attrs?["unit_of_measurement"];
        if (!string.IsNullOrWhiteSpace(unit)) fb.TemperatureUnit = unit;

        // Capabilities (nice for CH to show only supported controls)
        var modes = attrs?["hvac_modes"] as Newtonsoft.Json.Linq.JArray;
        if (modes != null) fb.SupportedHvacModes = ToStringList(modes);
        var fanModes = attrs?["fan_modes"] as Newtonsoft.Json.Linq.JArray;
        if (fanModes != null) fb.SupportedFanModes = ToStringList(fanModes);

        return fb;
    }

    static double? D(Newtonsoft.Json.Linq.JObject o, string key)
    {
        if (o == null) return null;
        var t = o[key];
        if (t == null || t.Type == Newtonsoft.Json.Linq.JTokenType.Null) return null;
        if (double.TryParse(
              t.ToString(),
              System.Globalization.NumberStyles.Any,
              System.Globalization.CultureInfo.InvariantCulture,
              out var v))
            return v;
        return null;
    }

    static System.Collections.Generic.List<string> ToStringList(Newtonsoft.Json.Linq.JArray arr)
    {
        var list = new System.Collections.Generic.List<string>(arr.Count);
        foreach (var x in arr) list.Add((string)x);
        return list;
    }

}

public class ClimateFeedback
{
    public string EntityId { get; set; }
    public string Name { get; set; }

    public string HvacMode { get; set; }       // off|heat|cool|heat_cool|…
    public string Action { get; set; }         // heating|cooling|idle|off
    public string FanMode { get; set; }

    public double? CurrentTemperature { get; set; }

    // Single-target (heat OR cool modes)
    public double? TargetTemperature { get; set; }

    // Range (heat_cool/auto mode)
    public double? HeatSetpoint { get; set; }
    public double? CoolSetpoint { get; set; }

    // Limits / step / units
    public double? MinTemp { get; set; }
    public double? MaxTemp { get; set; }
    public double Step { get; set; } = 0.5;    // default if HA doesn’t say
    public string TemperatureUnit { get; set; } = "°C";

    // Capabilities
    public List<string> SupportedHvacModes { get; set; }
    public List<string> SupportedFanModes { get; set; }

    // Convenience
    public bool IsRange => HeatSetpoint.HasValue || CoolSetpoint.HasValue;
    public string TargetSummary =>
      TargetTemperature.HasValue
        ? TargetTemperature.Value.ToString("0.0")
        : $"{HeatSetpoint?.ToString("0.0") ?? "-"}–{CoolSetpoint?.ToString("0.0") ?? "-"}";
}
