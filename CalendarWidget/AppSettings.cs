using System.Text.Json;

namespace CalendarWidget;

public class AppSettings
{
    public int Transparency { get; set; } = 220;  // 80..255
    public int Dim { get; set; } = 0;             // 0..90 (%)

    // which screen corner hosts the hover panel: BottomRight, BottomLeft, TopRight, TopLeft
    public string PanelCorner { get; set; } = "BottomRight";

    // saved window bounds (borderless state); Width == 0 means "not set yet"
    public int WinX { get; set; }
    public int WinY { get; set; }
    public int WinW { get; set; }
    public int WinH { get; set; }

    public static string DataFolder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CalendarWidget");

    public static string WebViewDataFolder => Path.Combine(DataFolder, "WebView2Data");

    private static string SettingsFile => Path.Combine(DataFolder, "settings.json");

    /// <summary>True when no settings file existed yet (used to start in interactive mode for sign-in).</summary>
    public static bool IsFirstRun => !File.Exists(SettingsFile);

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsFile))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsFile)) ?? new AppSettings();
        }
        catch
        {
            // corrupt settings fall back to defaults
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(DataFolder);
            File.WriteAllText(SettingsFile, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // settings persistence is best-effort; never crash the widget over it
        }
    }
}
