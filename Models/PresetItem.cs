namespace MicCompressor.Models;

public class PresetItem
{
    public string Id { get; }
    public string DisplayName { get; }
    public bool IsCustom { get; }

    public PresetItem(string id, string displayName, bool isCustom = false)
    {
        Id = id;
        DisplayName = displayName;
        IsCustom = isCustom;
    }

    public override string ToString() => DisplayName;
}
