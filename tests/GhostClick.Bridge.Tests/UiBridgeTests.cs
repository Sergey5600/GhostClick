using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using ClickerEngine.Core;
using ClickerEngine.Core.Configuration;
using ClickerEngine.Core.Engine;
using ClickerEngine.Core.Hotkeys;
using ClickerEngine.Core.Input;
using ClickerEngine.Core.Motion;
using ClickerEngine.Core.Profiles;
using ClickerEngine.Core.Timing;
using Xunit;

namespace GhostClick.Bridge.Tests;

public class UiBridgeTests
{
    [Fact]
    public void Ready_SendsTheProfileThenTheLogThatWaitedForThePage()
    {
        using var f = new Fixture();

        f.Ready();

        var init = f.Channel.Messages[0];
        Assert.Equal("init", Type(init));
        Assert.Equal("Default", init.GetProperty("profile").GetProperty("name").GetString());
        Assert.False(init.GetProperty("profile").GetProperty("dirty").GetBoolean());
        Assert.Equal(new[] { "Default" }, init.GetProperty("profiles").EnumerateArray().Select(p => p.GetString()));
        Assert.True(init.GetProperty("hotkeys").GetBoolean());
        Assert.False(init.GetProperty("dryRun").GetBoolean());
        Assert.Equal("stopped", init.GetProperty("runtime").GetProperty("state").GetString());

        var data = init.GetProperty("profile").GetProperty("data");
        Assert.Equal("Mouse1", data.GetProperty("actions")[0].GetProperty("key").GetString());
        Assert.Equal("F12", data.GetProperty("bind").GetProperty("panic").GetProperty("key").GetString());

        // The first run creates the profile through the interface defaults.
        Assert.True(data.GetProperty("flags").GetProperty("jitter").GetBoolean());
        Assert.True(data.GetProperty("flags").GetProperty("activeWindowOnly").GetBoolean());

        var log = f.Channel.Messages[1];
        Assert.Equal("log", Type(log));
        Assert.Equal("12:34:56", log.GetProperty("time").GetString());
        Assert.Equal("Профиль «Default» загружен", log.GetProperty("message").GetString());
    }

    [Fact]
    public void NothingIsSentBeforeThePageSaysItIsReady()
    {
        using var f = new Fixture();

        f.Send(new { type = "start" });
        f.PumpUntil(() => f.Host.Engine.State == EngineState.Running);
        f.Send(new { type = "stop" });
        f.Pump();

        Assert.Empty(f.Channel.Messages);
    }

    [Fact]
    public void Edits_MarkTheProfileDirtyAndSaveClearsIt()
    {
        using var f = new Fixture();
        f.Ready();

        f.SetProfile(data => data["delay"] = 250);

        Assert.True(f.Last("dirty").GetProperty("value").GetBoolean());
        Assert.Equal(250, f.Host.Engine.Profile.Interval.DelayMs);
        Assert.Equal(100, f.Store.Load("Default").Interval.DelayMs);

        f.Send(new { type = "saveProfile" });

        Assert.False(f.Last("profile").GetProperty("profile").GetProperty("dirty").GetBoolean());
        Assert.Equal("Профиль «Default» сохранён", f.Last("toast").GetProperty("message").GetString());
        Assert.Equal(250, f.Store.Load("Default").Interval.DelayMs);
    }

    [Fact]
    public void EditingBackToTheSavedValueIsNotDirty()
    {
        using var f = new Fixture();
        f.Ready();

        f.SetProfile(data => data["delay"] = 250);
        f.SetProfile(data => data["delay"] = 100);

        Assert.False(f.Last("dirty").GetProperty("value").GetBoolean());
    }

    [Fact]
    public void AnInfiniteProfileSavedWithATimeLimitRunsWithoutItAndDoesNotLookEdited()
    {
        // Profiles from before the interface carry the engine's ten-minute limit.
        var store = new InMemoryProfileStore();
        store.Save(ClickerProfile.CreateDefault("Default"));
        store.LastUsedProfileName = "Default";
        Assert.Equal(600, store.Load("Default").Safety.MaxRunDurationSeconds);

        using var f = new Fixture(store);
        f.Ready();

        Assert.Equal(0, f.Host.Engine.Profile.Safety.MaxRunDurationSeconds);
        Assert.False(f.Last("init").GetProperty("profile").GetProperty("dirty").GetBoolean());

        // The file catches up on the next save.
        f.Send(new { type = "saveProfile" });
        Assert.Equal(0, store.Load("Default").Safety.MaxRunDurationSeconds);
    }

    [Fact]
    public void LoadingAnInfiniteProfileLiftsItsTimeLimit()
    {
        var store = new InMemoryProfileStore();
        store.Save(UiDefaults.CreateProfile("Default"));
        var farm = ClickerProfile.CreateDefault("Farm");
        farm.Safety.MaxRunDurationSeconds = 600;
        store.Save(farm);
        store.LastUsedProfileName = "Default";

        using var f = new Fixture(store);
        f.Ready();
        f.Send(new { type = "loadProfile", name = "Farm" });

        Assert.Equal("Farm", f.Host.Engine.Profile.Name);
        Assert.Equal(0, f.Host.Engine.Profile.Safety.MaxRunDurationSeconds);
        Assert.False(f.Last("profile").GetProperty("profile").GetProperty("dirty").GetBoolean());
    }

    [Fact]
    public void ChoosingInfiniteLiftsTheTimeLimitAndFixedKeepsIt()
    {
        var store = new InMemoryProfileStore();
        var timed = ClickerProfile.CreateDefault("Default");
        timed.Repeat.Mode = RepeatMode.FixedCount;
        timed.Safety.MaxRunDurationSeconds = 600;
        store.Save(timed);
        store.LastUsedProfileName = "Default";

        using var f = new Fixture(store);
        f.Ready();
        Assert.Equal(600, f.Host.Engine.Profile.Safety.MaxRunDurationSeconds);

        f.SetProfile(data => data["repeat"] = "inf");

        Assert.Equal(0, f.Host.Engine.Profile.Safety.MaxRunDurationSeconds);
        Assert.True(f.Last("dirty").GetProperty("value").GetBoolean());
    }

    [Fact]
    public void AnInfiniteRunOutlivesTheTimeLimitItWasSavedWith()
    {
        var store = new InMemoryProfileStore();
        var profile = ClickerProfile.CreateDefault("Default");
        profile.Interval.DelayMs = 5;
        profile.Safety.MaxRunDurationSeconds = 1;
        store.Save(profile);
        store.LastUsedProfileName = "Default";

        using var f = new Fixture(store);
        f.Ready();
        f.Send(new { type = "start" });
        f.PumpUntil(() => f.Host.Engine.State == EngineState.Running);

        Thread.Sleep(1500);
        f.Pump();

        Assert.Equal(EngineState.Running, f.Host.Engine.State);
        Assert.DoesNotContain(f.Logs(), l => l.StartsWith("Лимит времени", StringComparison.Ordinal));

        f.Send(new { type = "stop" });
        f.PumpUntil(() => f.Logs().Any(l => l.StartsWith("Остановлено", StringComparison.Ordinal)));
    }

    [Fact]
    public void Revert_RestoresTheSavedProfile()
    {
        using var f = new Fixture();
        f.Ready();

        f.SetProfile(data => data["actions"] = new JsonArray(Chord("KeyE")));
        Assert.Equal(VirtualKey.E, f.Host.Engine.Profile.Action.Inputs[0].Key);

        f.Send(new { type = "revertProfile" });

        var profile = f.Last("profile").GetProperty("profile");
        Assert.False(profile.GetProperty("dirty").GetBoolean());
        Assert.Equal("Mouse1", profile.GetProperty("data").GetProperty("actions")[0].GetProperty("key").GetString());
        Assert.Equal(VirtualKey.LeftButton, f.Host.Engine.Profile.Action.Inputs[0].Key);
    }

    [Fact]
    public void SavingAnInvalidProfileReportsWhyInRussian()
    {
        using var f = new Fixture();
        f.Ready();

        f.SetProfile(data => data["target"] = "points");
        f.Send(new { type = "saveProfile" });

        var toast = f.Last("toast");
        Assert.True(toast.GetProperty("error").GetBoolean());
        Assert.Equal("Добавьте хотя бы одну точку маршрута", toast.GetProperty("message").GetString());
    }

    [Fact]
    public void Listening_SilencesTheHotkeysButKeepsTheEmergencyStop()
    {
        using var f = new Fixture();
        f.Ready();

        f.Send(new { type = "listen", value = true });

        Assert.False(f.Hotkeys.Settings.StartStop.IsBound);
        Assert.False(f.Hotkeys.Settings.Pause.IsBound);
        Assert.False(f.Hotkeys.Settings.RecordPoint.IsBound);
        Assert.Equal(VirtualKey.F12, f.Hotkeys.Settings.EmergencyStop.Key);

        // An assignment made while listening republishes the profile; it must stay quiet.
        f.SetProfile(data => data["bind"]!["pause"] = Chord("KeyP"));
        Assert.False(f.Hotkeys.Settings.Pause.IsBound);

        f.Send(new { type = "listen", value = false });

        Assert.Equal(VirtualKey.F6, f.Hotkeys.Settings.StartStop.Key);
        Assert.Equal(VirtualKey.P, f.Hotkeys.Settings.Pause.Key);
    }

    [Fact]
    public void AReloadedPageIsNoLongerListening()
    {
        using var f = new Fixture();
        f.Ready();
        f.Send(new { type = "listen", value = true });

        f.Ready();

        Assert.Equal(VirtualKey.F6, f.Hotkeys.Settings.StartStop.Key);
    }

    [Fact]
    public void Start_RunsTheEngineAndReportsTheRun()
    {
        using var f = new Fixture();
        f.Ready();
        f.SetProfile(data =>
        {
            data["repeat"] = "fixed";
            data["count"] = 3;
            data["delay"] = 1;
        });

        f.Send(new { type = "start" });
        f.PumpUntil(() => f.Logs().Any(l => l.StartsWith("Выполнено", StringComparison.Ordinal)));

        Assert.Contains("Старт · ЛКМ", f.Logs());
        Assert.Contains("Выполнено · 3 клика", f.Logs());

        var state = f.Last("state");
        Assert.Equal("stopped", state.GetProperty("state").GetString());
        Assert.Equal(3, state.GetProperty("clicks").GetInt64());
        Assert.True(state.GetProperty("elapsedMs").GetInt64() >= 0);
        Assert.Equal(3, f.Live.Events.Count(e => e.Kind == InjectedEventKind.MouseDown));
    }

    [Fact]
    public void Start_WithAProfileThatCannotRunShowsAnError()
    {
        using var f = new Fixture();
        f.Ready();
        f.SetProfile(data => data["actions"] = new JsonArray());

        f.Send(new { type = "start" });

        var toast = f.Last("toast");
        Assert.True(toast.GetProperty("error").GetBoolean());
        Assert.Equal("Выберите хотя бы одно действие", toast.GetProperty("message").GetString());
        Assert.Equal(EngineState.Idle, f.Host.Engine.State);
    }

    [Fact]
    public void Tick_StreamsCountersOnlyWhileARunIsActive()
    {
        using var f = new Fixture();
        f.Ready();

        f.Bridge.Tick();
        Assert.Empty(f.All("state"));

        f.SetProfile(data => data["delay"] = 1);
        f.Send(new { type = "start" });
        f.PumpUntil(() => f.Host.Engine.CurrentRunActions >= 2);
        f.Bridge.Tick();

        Assert.Equal("running", f.Last("state").GetProperty("state").GetString());

        f.Send(new { type = "stop" });
        f.Pump();
    }

    [Fact]
    public void PauseAndResume_AreLogged()
    {
        using var f = new Fixture();
        f.Ready();
        f.SetProfile(data => data["delay"] = 5);

        f.Send(new { type = "start" });
        f.PumpUntil(() => f.Host.Engine.State == EngineState.Running);
        f.Send(new { type = "pause" });
        f.PumpUntil(() => f.Logs().Contains("Пауза"));
        Assert.Equal("paused", f.Last("state").GetProperty("state").GetString());

        f.Send(new { type = "resume" });
        f.PumpUntil(() => f.Logs().Contains("Продолжено"));

        f.Send(new { type = "panic" });
        f.PumpUntil(() => f.Logs().Any(l => l.StartsWith("Аварийная остановка", StringComparison.Ordinal)));
    }

    [Fact]
    public void RecordHotkey_SendsThePointToThePage()
    {
        using var f = new Fixture();
        f.Ready();

        f.Hotkeys.Trigger(HotkeyAction.RecordPoint);
        f.Pump();

        var point = f.Last("pointAdded");
        Assert.Equal(300, point.GetProperty("x").GetInt32());
        Assert.Equal(200, point.GetProperty("y").GetInt32());

        var hotkeys = f.All("hotkey").ToArray();
        Assert.Equal(2, hotkeys.Length);
        Assert.All(hotkeys, h => Assert.Equal("point", h.GetProperty("slot").GetString()));
        Assert.True(hotkeys[0].GetProperty("down").GetBoolean());
        Assert.False(hotkeys[1].GetProperty("down").GetBoolean());
    }

    [Fact]
    public void StartHotkey_WithAProfileThatCannotRunShowsAnError()
    {
        using var f = new Fixture();
        f.Ready();
        f.SetProfile(data => data["target"] = "points");

        f.Hotkeys.Trigger(HotkeyAction.StartStop);
        f.Pump();

        Assert.Equal("Добавьте хотя бы одну точку маршрута", f.Last("toast").GetProperty("message").GetString());
    }

    [Fact]
    public void NewAndDuplicate_CreateStoreAndSwitchToProfiles()
    {
        using var f = new Fixture();
        f.Ready();

        f.Send(new { type = "newProfile" });

        var created = f.Last("profile");
        Assert.Equal("Профиль 2", created.GetProperty("profile").GetProperty("name").GetString());
        Assert.Equal(new[] { "Default", "Профиль 2" }, Names(created));
        Assert.True(f.Store.Exists("Профиль 2"));

        f.SetProfile(data => data["delay"] = 333);
        f.Send(new { type = "duplicateProfile" });

        var copy = f.Last("profile");
        Assert.Equal("Профиль 2 (копия)", copy.GetProperty("profile").GetProperty("name").GetString());
        Assert.False(copy.GetProperty("profile").GetProperty("dirty").GetBoolean());
        Assert.Equal(333, f.Store.Load("Профиль 2 (копия)").Interval.DelayMs);
        Assert.Equal("Профиль 2 (копия)", f.Host.Engine.Profile.Name);
    }

    [Fact]
    public void DuplicatingAnInvalidProfileLeavesTheCurrentOneInPlace()
    {
        using var f = new Fixture();
        f.Ready();
        f.SetProfile(data => data["target"] = "points");

        f.Send(new { type = "duplicateProfile" });

        Assert.True(f.Last("toast").GetProperty("error").GetBoolean());
        Assert.Equal("Default", f.Host.Profiles.Current.Name);
        Assert.Equal(new[] { "Default" }, f.Store.List());
    }

    [Fact]
    public void LoadProfile_StopsTheRunAndSwitches()
    {
        var store = new InMemoryProfileStore();
        var fishing = UiDefaults.CreateProfile("Fishing");
        fishing.Interval.DelayMs = 900;
        store.Save(fishing);
        store.Save(UiDefaults.CreateProfile("Default"));
        store.LastUsedProfileName = "Default";

        using var f = new Fixture(store);
        f.Ready();
        f.SetProfile(data => data["delay"] = 5);
        f.Send(new { type = "start" });
        f.PumpUntil(() => f.Host.Engine.State == EngineState.Running);

        f.Send(new { type = "loadProfile", name = "Fishing" });

        Assert.Equal(EngineState.Idle, f.Host.Engine.State);
        Assert.Equal("Fishing", f.Last("profile").GetProperty("profile").GetProperty("name").GetString());
        Assert.Equal(900, f.Host.Engine.Profile.Interval.DelayMs);
        Assert.Equal("Fishing", store.LastUsedProfileName);
    }

    [Fact]
    public void LoadingAMissingProfileShowsAnError()
    {
        using var f = new Fixture();
        f.Ready();

        f.Send(new { type = "loadProfile", name = "Nope" });

        Assert.True(f.Last("toast").GetProperty("error").GetBoolean());
        Assert.Equal("Default", f.Host.Profiles.Current.Name);
    }

    [Fact]
    public void DryRun_StopsARunningClickerBeforeSwitching()
    {
        using var f = new Fixture();
        f.Ready();
        f.SetProfile(data => data["delay"] = 5);
        f.Send(new { type = "start" });
        f.PumpUntil(() => f.Host.Engine.State == EngineState.Running);

        f.Send(new { type = "setDryRun", value = true });

        Assert.Equal(EngineState.Idle, f.Host.Engine.State);
        Assert.True(f.Injector.DryRun);

        // The dry-run recorder does not grow without bound on a long run.
        Assert.Equal(UiBridge.DryRunEventCapacity, f.Injector.Recorder.Capacity);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("""{ "type": 5 }""")]
    [InlineData("""{ "type": "setProfile", "data": "nonsense" }""")]
    [InlineData("""{ "type": "setProfile" }""")]
    [InlineData("""{ "type": "teleport" }""")]
    public void MalformedMessagesAreIgnored(string json)
    {
        using var f = new Fixture();
        f.Ready();
        int before = f.Channel.Messages.Count;

        f.Bridge.Receive(json);

        Assert.Equal(before, f.Channel.Messages.Count);
    }

    private static JsonObject Chord(string key, params string[] mods) =>
        new() { ["mods"] = new JsonArray(mods.Select(m => (JsonNode)m).ToArray()), ["key"] = key };

    private static string? Type(JsonElement message) => message.GetProperty("type").GetString();

    private static string?[] Names(JsonElement profileMessage) =>
        profileMessage.GetProperty("profiles").EnumerateArray().Select(p => p.GetString()).ToArray();

    /// <summary>A host wired to recorders, an in-memory store and a hand-driven hotkey service.</summary>
    private sealed class Fixture : IDisposable
    {
        public Fixture(InMemoryProfileStore? store = null)
        {
            Live = new RecordingInputInjector(new ScreenPoint(300, 200));
            Injector = new SwitchableInputInjector(Live, new RecordingInputInjector());
            Hotkeys = new ManualHotkeyService();
            Store = store ?? new InMemoryProfileStore();
            Host = new ClickerHost(
                Injector,
                Store,
                Hotkeys,
                new SystemClock(),
                new FixedScreenGeometry(1920, 1080),
                NeutralRandomSource.Instance);
            Channel = new FakeChannel();
            Bridge = new UiBridge(Host, Injector, Channel, () => new DateTime(2026, 10, 16, 12, 34, 56));
            Bridge.Start();
        }

        public RecordingInputInjector Live { get; }

        public SwitchableInputInjector Injector { get; }

        public ManualHotkeyService Hotkeys { get; }

        public InMemoryProfileStore Store { get; }

        public ClickerHost Host { get; }

        public FakeChannel Channel { get; }

        public UiBridge Bridge { get; }

        public void Ready() => Send(new { type = "ready" });

        public void Send(object message)
        {
            Bridge.Receive(JsonSerializer.Serialize(message));
            Pump();
        }

        /// <summary>
        /// Edits the profile the way the page does: start from the newest data it holds (its
        /// own last edit, or what the host sent since), change it, send the whole thing back.
        /// </summary>
        public void SetProfile(Action<JsonObject> edit)
        {
            int received = Channel.Messages.FindLastIndex(m => Type(m) is "init" or "profile");
            if (_edited is null || received > _editedAt)
            {
                _edited = Channel.Messages[received].GetProperty("profile").GetProperty("data").GetRawText();
            }

            var data = JsonNode.Parse(_edited)!.AsObject();
            edit(data);
            _edited = data.ToJsonString();
            _editedAt = Channel.Messages.Count;
            Send(new { type = "setProfile", data });
        }

        private string? _edited;
        private int _editedAt = -1;

        public void Pump() => Channel.Pump();

        public void PumpUntil(Func<bool> condition)
        {
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
            while (!condition())
            {
                Assert.True(DateTime.UtcNow < deadline, "Timed out waiting for the bridge.");
                Channel.Pump();
                Thread.Sleep(2);
            }

            Channel.Pump();
        }

        public JsonElement Last(string type) => Channel.Messages.Last(m => Type(m) == type);

        public IEnumerable<JsonElement> All(string type) => Channel.Messages.Where(m => Type(m) == type);

        public List<string> Logs() => All("log").Select(l => l.GetProperty("message").GetString()!).ToList();

        public void Dispose()
        {
            Bridge.Dispose();
            Host.Dispose();
        }
    }

    /// <summary>
    /// Stands in for the WebView: records what the bridge sends and runs posted work only
    /// when the test pumps, the way a dispatcher would.
    /// </summary>
    private sealed class FakeChannel : IUiChannel
    {
        private readonly ConcurrentQueue<Action> _posted = new();

        public List<JsonElement> Messages { get; } = new();

        public void Send(string json) => Messages.Add(JsonDocument.Parse(json).RootElement.Clone());

        public void Post(Action action) => _posted.Enqueue(action);

        public void Pump()
        {
            while (_posted.TryDequeue(out var action))
            {
                action();
            }
        }
    }
}
