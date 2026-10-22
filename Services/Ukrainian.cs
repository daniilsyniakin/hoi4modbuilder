namespace HoI4ModBuilder.Services;

/// <summary>Helpers for Ukrainian UI text.</summary>
public static class Ukrainian
{
    /// <summary>Plural form: 1 помилка, 2 помилки, 5 помилок, 21 помилка, 11 помилок.</summary>
    public static string Plural(int n, string one, string few, string many) =>
        (n % 10, n % 100) switch
        {
            (1, not 11) => one,
            (2 or 3 or 4, not (12 or 13 or 14)) => few,
            _ => many,
        };

    /// <summary>"3 файли", "11 файлів"…</summary>
    public static string Count(int n, string one, string few, string many) => $"{n} {Plural(n, one, few, many)}";
}
