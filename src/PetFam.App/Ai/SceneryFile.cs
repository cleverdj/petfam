using System.Text.Json;

namespace PetFam.Desktop.Ai;

/// <summary>
/// scenery-ai.json: the AI's few words about each wallpaper it has read (by wallpaper and screen size, so it is asked
/// only once), and what each screen showed last, so a wallpaper changed while PetFam was closed is still noticed.
/// </summary>
public sealed class SceneryFile
{
    public sealed class Seen
    {
        public string Key { get; set; } = "";
        public string Text { get; set; } = "";
    }

    public Dictionary<string, string> Scenes { get; set; } = new();
    public Dictionary<string, Seen> Last { get; set; } = new();

    public static SceneryFile Read(string path)
    {
        try
        {
            if (File.Exists(path)) return JsonSerializer.Deserialize(File.ReadAllText(path), AiJsonContext.Default.SceneryFile) ?? new();
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            AppLog.Write("Scenery file unreadable: " + ex.Message);
        }
        return new();
    }

    public void Write(string path)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(this, AiJsonContext.Default.SceneryFile));
        }
        catch (IOException ex)
        {
            AppLog.Write("Scenery not saved: " + ex.Message);
        }
    }
}
