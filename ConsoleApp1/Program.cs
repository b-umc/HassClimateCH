using System;
using System.Globalization;

class Program
{
    static HassClimateHub _hub;

    static void Main()
    {
        // 🔧 Hard-code your HA details here
        var wsUrl = "ws://192.168.255.128:8123/api/websocket"; // or wss:// if SSL
        var token = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiI3YTdiYmMxOWQ1ZDI0MDViYWY5NWNlNDFmYzVkYzczNCIsImlhdCI6MTc1Nzg3MjY5NCwiZXhwIjoyMDczMjMyNjk0fQ.OX20L5TMSvBPWME9DVB9Qcg3qzIVVUVhf5-BEZTPat4";
        // System.Net.ServicePointManager.ServerCertificateValidationCallback += (s, cert, chain, err) => true;

        // If using self-signed HTTPS, uncomment for testing only:
        // System.Net.ServicePointManager.ServerCertificateValidationCallback += (s, cert, chain, err) => true;

        _hub = new HassClimateHub(wsUrl, token);

        _hub.Error += msg => Console.WriteLine("[ERR] " + msg);
        _hub.Connected += () => Console.WriteLine("[OK] Authenticated with Home Assistant.");
        _hub.Disconnected += () => Console.WriteLine("[INF] Disconnected.");

        _hub.ClimateAdded += (eid, fb) =>
        {
            Console.WriteLine(
              $"[ADD] {eid}  name='{fb.Name}'  mode={fb.HvacMode} act={fb.Action} " +
              $"cur={fb.CurrentTemperature:0.0} tgt={fb.TargetSummary}");
        };

        _hub.ClimateChanged += (eid, fb) =>
        {
            Console.WriteLine(
              $"[CHG] {eid}  mode={fb.HvacMode} act={fb.Action} " +
              $"cur={fb.CurrentTemperature:0.0} tgt={fb.TargetSummary}");
        };

        _hub.ClimateRemoved += eid =>
          Console.WriteLine($"[DEL] {eid}");

        _hub.Start();

        Console.WriteLine("Waiting for climates… type 'list' or 'exit'.");
        while (true)
        {
            Console.Write("> ");
            var line = Console.ReadLine();
            if (line == null) break;
            var parts = line.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) continue;

            var cmd = parts[0].ToLowerInvariant();
            try
            {
                if (cmd == "exit") break;
                if (cmd == "list")
                {
                    foreach (var kv in _hub.Climates)
                    {
                        var fb = kv.Value;
                        Console.WriteLine($"{fb.EntityId} '{fb.Name}'  mode={fb.HvacMode} act={fb.Action} cur={fb.CurrentTemperature:0.0}  tgt={fb.TargetSummary}  step={fb.Step:0.##} {fb.TemperatureUnit}");
                    }
                }

                else if (cmd == "mode" && parts.Length >= 3)
                {
                    _hub.SetMode(parts[1], parts[2]); // off|heat|cool|auto|…
                }
                else if (cmd == "temp" && parts.Length >= 3)
                {
                    _hub.SetSingleSetpoint(parts[1], double.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture));
                }
                else if (cmd == "range" && parts.Length >= 4)
                {
                    var heat = double.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture);
                    var cool = double.Parse(parts[3], System.Globalization.CultureInfo.InvariantCulture);
                    _hub.SetHeatCool(parts[1], heat, cool);
                }
                else if (cmd == "fan" && parts.Length >= 3)
                {
                    _hub.SetFanMode(parts[1], parts[2]); // auto|on|low|med|high (varies)
                }
                else
                {
                    Console.WriteLine("Commands: list | mode <eid> <mode> | temp <eid> <C> | range <eid> <heatC> <coolC> | fan <eid> <mode> | exit");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error: " + ex.Message);
            }
        }


        _hub.Stop();
    }
}