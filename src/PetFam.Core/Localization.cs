namespace PetFam.Core;

/// <summary>UI and pet languages. English is the default; Korean is an option.</summary>
public enum Language
{
    English,
    Korean,
}

/// <summary>
/// The current language and a tiny helper for texts written in both languages at the call site:
/// <c>L.S("Feed", "밥 주기")</c>. Data with texts (breeds, voice lines) uses <see cref="Loc"/>.
/// </summary>
public static class L
{
    public static Language Current { get; private set; } = Language.English;

    public static bool Ko => Current == Language.Korean;

    /// <summary>Raised after the language changes (menus and windows rebuild their texts).</summary>
    public static event Action? Changed;

    public static void Set(Language language)
    {
        if (Current == language) return;
        Current = language;
        Changed?.Invoke();
    }

    /// <summary>The text in the current language.</summary>
    public static string S(string en, string ko) => Ko ? ko : en;

    public static string Code(Language l) => l == Language.Korean ? "ko" : "en";

    public static Language Parse(string? code) =>
        code is not null && code.StartsWith("ko", StringComparison.OrdinalIgnoreCase) ? Language.Korean : Language.English;
}

/// <summary>A text in English and Korean; shows as the current language.</summary>
public readonly record struct Loc(string En, string Ko)
{
    public string Text => L.Ko ? Ko : En;

    public override string ToString() => Text;

    public static implicit operator string(Loc l) => l.Text;
}
