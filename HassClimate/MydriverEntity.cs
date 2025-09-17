// two-space indents
using System;
using System.Collections.Generic;
using System.Globalization;
using Crestron.DeviceDrivers.EntityModel;
using Crestron.DeviceDrivers.EntityModel.Data;
using Crestron.DeviceDrivers.EntityModel.Logging;
using Crestron.DeviceDrivers.SDK;
using Crestron.DeviceDrivers.SDK.EntityModel;

public class MyDriverEntity : ReflectedAttributeDriverEntity, IDisposable
{
    readonly DriverControllerCreationArgs _args;
    readonly string _cid = DriverController.RootControllerId;
    HassClimateHub _hub;

    internal DataDrivenConfigurationController ConfigurationController { get; private set; }

    public MyDriverEntity(DriverControllerCreationArgs args, DriverImplementationResources resources)
      : base(DriverController.RootControllerId)
    {
        _args = args;
        var cfgArgs = DataDrivenConfigurationControllerArgs.FromResources(args, resources, _cid);
        ConfigurationController = new DelegateDataDrivenConfigurationController(cfgArgs, ApplyConfigurationItems, null);
    }

    private ConfigurationItemErrors ApplyConfigurationItems(
      DataDrivenConfigurationController.ApplyConfigurationAction action,
      string stepId,
      IDictionary<string, DriverEntityValue?> values)
    {
        // Log the apply attempt so we know the delegate ran
        _args.Logger.Log(_cid, LogEntryLevel.Info, $"Config Apply: action={action} step={stepId ?? "(null)"} values={(values == null ? "null" : values.Count.ToString())}");

        if (action == DataDrivenConfigurationController.ApplyConfigurationAction.ClearValues)
        {
            try { _hub?.Stop(); } catch { }
            return null;
        }

        if (values == null)
            return new ConfigurationItemErrors(null, "No configuration values were provided.");

        // Pull values safely by the JSON IDs above
        string host = GetStr(values, "_Host_");
        int? port = GetInt(values, "_Port_");
        bool secure = GetBool(values, "_Secure_") ?? false;
        string path = GetStr(values, "_Path_") ?? "/api/websocket";
        string token = GetStr(values, "_Token_");

        var fieldErrors = new Dictionary<string, string>();
        if (string.IsNullOrWhiteSpace(host)) fieldErrors["_Host_"] = "Required";
        if (port == null || port < 1 || port > 65535) fieldErrors["_Port_"] = "1–65535";
        if (string.IsNullOrWhiteSpace(path)) fieldErrors["_Path_"] = "Required";
        if (string.IsNullOrWhiteSpace(token)) fieldErrors["_Token_"] = "Required";
        if (fieldErrors.Count > 0)
            return new ConfigurationItemErrors(fieldErrors, null);

        if (!path.StartsWith("/")) path = "/" + path;
        string scheme = secure ? "wss" : "ws";
        string wsUrl = $"{scheme}://{host}:{port}{path}";

        try
        {
            _args.Logger.Log(_cid, LogEntryLevel.Info, $"Connecting to HA: {wsUrl}");
            _hub?.Stop();
            _hub = new HassClimateHub(wsUrl, token);
            _hub.Connected += () => _args.Logger.Log(_cid, LogEntryLevel.Info, "HA connected");
            _hub.Error += msg => _args.Logger.Log(_cid, LogEntryLevel.Warning, "HA error: " + msg);
            _hub.ClimateAdded += (eid, fb) => _args.Logger.Log(_cid, LogEntryLevel.Info, $"ADD {eid} {fb.TargetSummary}");
            _hub.ClimateChanged += (eid, fb) => _args.Logger.Log(_cid, LogEntryLevel.Info, $"CHG {eid} {fb.TargetSummary}");
            _hub.Start();
            return null; // success
        }
        catch (Exception ex)
        {
            return new ConfigurationItemErrors(null, "Failed to start HA: " + ex.Message);
        }
    }

    static string GetStr(IDictionary<string, DriverEntityValue?> vals, string id)
      => vals.TryGetValue(id, out var v) && v.HasValue ? v.Value.GetValue<string>() : null;

    static int? GetInt(IDictionary<string, DriverEntityValue?> vals, string id)
    {
        if (!vals.TryGetValue(id, out var raw) || !raw.HasValue)
            return null;

        var value = raw.Value;

        int? TryConvert<T>()
        {
            try
            {
                var v = value.GetValue<T>();
                if (v == null) return null;

                if (v is string s)
                {
                    return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var si)
                      ? si
                      : (int?)null;
                }

                return Convert.ToInt32(v, CultureInfo.InvariantCulture);
            }
            catch
            {
                return null;
            }
        }

        return
            TryConvert<long>() ??
            TryConvert<int>() ??
            TryConvert<double>() ??
            TryConvert<decimal>() ??
            TryConvert<string>();
    }

    static bool? GetBool(IDictionary<string, DriverEntityValue?> vals, string id)
    {
        if (!vals.TryGetValue(id, out var raw) || !raw.HasValue)
            return null;

        var value = raw.Value;

        try { return value.GetValue<bool>(); }
        catch
        {
            try
            {
                var s = value.GetValue<string>();
                if (bool.TryParse(s, out var b)) return b;
            }
            catch { }
            return null;
        }
    }

    public new void Dispose()
    {
        try { _hub?.Stop(); } catch { }
    }
}
