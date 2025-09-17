public class Settings
{
    public string HaWsUrl { get; set; }      // e.g. ws://ha:8123/api/websocket or wss://...
    public string HaToken { get; set; }      // long-lived token
    public bool ExposeAll { get; set; } = true;
    public string IncludeFilterCsv { get; set; } // optional: "climate.office, climate.upstairs"
}
