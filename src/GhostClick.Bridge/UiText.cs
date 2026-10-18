namespace GhostClick.Bridge;

/// <summary>
/// The bridge's own words in each interface language: log lines, notices and the names it
/// gives new profiles. The page's strings live in <c>wwwroot/i18n.js</c>; where the two say
/// the same thing (a profile loaded, a run started) they say it the same way.
/// </summary>
public sealed class UiText
{
    /// <summary>The language codes the interface speaks, the first being the default.</summary>
    public static readonly IReadOnlyList<string> Languages = new[] { "ru", "en" };

    public static readonly UiText Russian = new()
    {
        Language = "ru",
        HotkeysUnavailable = "Горячие клавиши недоступны: ",
        ProfileLoaded = n => $"Профиль «{n}» загружен",
        ProfileSaved = n => $"Профиль «{n}» сохранён",
        ProfileCreated = n => $"Профиль «{n}» создан",
        ProfileCopied = (n, source) => $"Профиль «{n}» создан из «{source}»",
        NewProfileName = i => $"Профиль {i}",
        NewScriptName = i => $"Сценарий {i}",
        CopyName = (source, i) => i == 1 ? $"{source} (копия)" : $"{source} (копия {i})",
        SaveFailed = "Не удалось сохранить профиль: ",
        LoadFailed = (n, error) => $"Не удалось загрузить профиль «{n}»: {error}",
        CreateFailed = "Не удалось создать профиль: ",
        CopyFailed = "Не удалось сохранить копию: ",
        LanguageNotSaved = "Не удалось запомнить язык: ",
        Started = "Старт",
        NoActions = "нет действий",
        ScriptSummary = n => $"сценарий, {n} {Plural(n, "блок", "блока", "блоков")}",
        Paused = "Пауза",
        Resumed = "Продолжено",
        Clicks = n => Plural(n, "клик", "клика", "кликов"),
        StoppedByError = "Кликер остановлен из-за ошибки",
        StopUser = "Остановлено",
        StopCompleted = "Выполнено",
        StopHoldReleased = "Клавиша отпущена",
        StopFailsafe = "Аварийная остановка",
        StopTimeLimit = d => $"Лимит времени {d}",
        StopActionLimit = "Лимит действий",
        StopCursorMoved = "Остановлено: мышь сдвинута",
        StopError = e => $"Ошибка: {e}",
        Unknown = "неизвестно",
        Minutes = m => $"{m} мин",
        Seconds = s => $"{s} с",
        ProfileHasErrors = "Профиль содержит ошибки",
        NeedsPoint = "Добавьте хотя бы одну точку маршрута",
        NeedsAction = "Выберите хотя бы одно действие",
        NegativeDelay = "Интервал не может быть отрицательным",
        NegativeMinDelay = "Минимальный интервал не может быть отрицательным",
        MaxBelowMin = "Максимальный интервал меньше минимального",
        CountBelowOne = "Количество повторов должно быть не меньше 1",
        NegativePress = "Длительность нажатия не может быть отрицательной",
        HoldTooShort = "Время зажатия должно быть больше нуля",
        HoldNeedsStartKey = "В режиме удержания нужна клавиша «Старт / Стоп»",
        NeedsName = "У профиля должно быть имя",
        UpdateFound = v => $"Доступна версия {v}",
        UpdateCheckFailed = "Не удалось проверить обновления: ",
        UpdateFailed = "Не удалось загрузить обновление: ",
        UpdateBadChecksum = "файл повреждён (контрольная сумма не совпала)",
        UpdateScheduled = v => $"Версия {v} установится при выходе",
        TestNeedsScript = "Добавьте в сценарий хотя бы один включённый блок",
    };

    public static readonly UiText English = new()
    {
        Language = "en",
        HotkeysUnavailable = "Hotkeys are unavailable: ",
        ProfileLoaded = n => $"Profile “{n}” loaded",
        ProfileSaved = n => $"Profile “{n}” saved",
        ProfileCreated = n => $"Profile “{n}” created",
        ProfileCopied = (n, source) => $"Profile “{n}” created from “{source}”",
        NewProfileName = i => $"Profile {i}",
        NewScriptName = i => $"Script {i}",
        CopyName = (source, i) => i == 1 ? $"{source} (copy)" : $"{source} (copy {i})",
        SaveFailed = "Couldn’t save the profile: ",
        LoadFailed = (n, error) => $"Couldn’t load profile “{n}”: {error}",
        CreateFailed = "Couldn’t create the profile: ",
        CopyFailed = "Couldn’t save the copy: ",
        LanguageNotSaved = "Couldn’t remember the language: ",
        Started = "Start",
        NoActions = "no actions",
        ScriptSummary = n => $"script, {n} {(n == 1 ? "block" : "blocks")}",
        Paused = "Pause",
        Resumed = "Resumed",
        Clicks = n => n == 1 ? "click" : "clicks",
        StoppedByError = "The clicker stopped because of an error",
        StopUser = "Stopped",
        StopCompleted = "Completed",
        StopHoldReleased = "Key released",
        StopFailsafe = "Emergency stop",
        StopTimeLimit = d => $"Time limit {d}",
        StopActionLimit = "Action limit",
        StopCursorMoved = "Stopped: the mouse moved",
        StopError = e => $"Error: {e}",
        Unknown = "unknown",
        Minutes = m => $"{m} min",
        Seconds = s => $"{s} s",
        ProfileHasErrors = "The profile has errors",
        NeedsPoint = "Add at least one route point",
        NeedsAction = "Choose at least one action",
        NegativeDelay = "The interval can’t be negative",
        NegativeMinDelay = "The minimum interval can’t be negative",
        MaxBelowMin = "The maximum interval is less than the minimum",
        CountBelowOne = "The repeat count must be at least 1",
        NegativePress = "The press duration can’t be negative",
        HoldTooShort = "The hold time must be more than zero",
        HoldNeedsStartKey = "Hold mode needs a “Start / Stop” key",
        NeedsName = "The profile needs a name",
        UpdateFound = v => $"Version {v} is available",
        UpdateCheckFailed = "Couldn’t check for updates: ",
        UpdateFailed = "Couldn’t download the update: ",
        UpdateBadChecksum = "the file is damaged (its checksum does not match)",
        UpdateScheduled = v => $"Version {v} will install on exit",
        TestNeedsScript = "Add at least one enabled block to the scenario",
    };

    private UiText()
    {
    }

    /// <summary>The text for a language code; Russian for anything unknown.</summary>
    public static UiText For(string? language) => language == English.Language ? English : Russian;

    /// <summary>True for a language code the interface speaks.</summary>
    public static bool IsLanguage(string? language) => language is not null && Languages.Contains(language);

    /// <summary>The language code: <c>ru</c> or <c>en</c>.</summary>
    public required string Language { get; init; }

    public required string HotkeysUnavailable { get; init; }

    public required Func<string, string> ProfileLoaded { get; init; }

    public required Func<string, string> ProfileSaved { get; init; }

    public required Func<string, string> ProfileCreated { get; init; }

    public required Func<string, string, string> ProfileCopied { get; init; }

    /// <summary>The name of a new profile, numbered from the count of profiles.</summary>
    public required Func<int, string> NewProfileName { get; init; }

    /// <summary>The name of a new script, numbered from the count of profiles.</summary>
    public required Func<int, string> NewScriptName { get; init; }

    /// <summary>The name of the <c>i</c>-th copy of a profile.</summary>
    public required Func<string, int, string> CopyName { get; init; }

    public required string SaveFailed { get; init; }

    public required Func<string, string, string> LoadFailed { get; init; }

    public required string CreateFailed { get; init; }

    public required string CopyFailed { get; init; }

    public required string LanguageNotSaved { get; init; }

    public required string Started { get; init; }

    public required string NoActions { get; init; }

    /// <summary>What a script run presses, for the start line of the log: its number of blocks.</summary>
    public required Func<int, string> ScriptSummary { get; init; }

    public required string Paused { get; init; }

    public required string Resumed { get; init; }

    /// <summary>The word for <c>n</c> clicks, without the number.</summary>
    public required Func<long, string> Clicks { get; init; }

    public required string StoppedByError { get; init; }

    public required string StopUser { get; init; }

    public required string StopCompleted { get; init; }

    public required string StopHoldReleased { get; init; }

    public required string StopFailsafe { get; init; }

    public required Func<string, string> StopTimeLimit { get; init; }

    public required string StopActionLimit { get; init; }

    public required string StopCursorMoved { get; init; }

    public required Func<string, string> StopError { get; init; }

    public required string Unknown { get; init; }

    public required Func<int, string> Minutes { get; init; }

    public required Func<int, string> Seconds { get; init; }

    public required string ProfileHasErrors { get; init; }

    public required string NeedsPoint { get; init; }

    public required string NeedsAction { get; init; }

    public required string NegativeDelay { get; init; }

    public required string NegativeMinDelay { get; init; }

    public required string MaxBelowMin { get; init; }

    public required string CountBelowOne { get; init; }

    public required string NegativePress { get; init; }

    public required string HoldTooShort { get; init; }

    public required string HoldNeedsStartKey { get; init; }

    public required string NeedsName { get; init; }

    /// <summary>The log line when a newer version is found.</summary>
    public required Func<string, string> UpdateFound { get; init; }

    public required string UpdateCheckFailed { get; init; }

    public required string UpdateFailed { get; init; }

    public required string UpdateBadChecksum { get; init; }

    public required Func<string, string> UpdateScheduled { get; init; }

    public required string TestNeedsScript { get; init; }

    /// <summary>A run length the way the log writes it: whole minutes, otherwise seconds.</summary>
    public string Duration(int seconds) => seconds % 60 == 0 ? Minutes(seconds / 60) : Seconds(seconds);

    private static string Plural(long n, string one, string few, string many)
    {
        long mod100 = n % 100;
        long mod10 = n % 10;

        if (mod10 == 1 && mod100 != 11)
        {
            return one;
        }

        return mod10 is >= 2 and <= 4 && mod100 is < 12 or > 14 ? few : many;
    }

    public override string ToString() => Language;
}
