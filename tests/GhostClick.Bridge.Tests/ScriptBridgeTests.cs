using System.Text.Json;
using System.Text.Json.Nodes;
using ClickerEngine.Core.Configuration;
using ClickerEngine.Core.Engine;
using ClickerEngine.Core.Input;
using ClickerEngine.Core.Profiles;
using Xunit;
using Fixture = GhostClick.Bridge.Tests.UiBridgeTests.Fixture;

namespace GhostClick.Bridge.Tests;

/// <summary>Scripts over the page protocol: editing, issues, running and the running block.</summary>
public class ScriptBridgeTests
{
    [Fact]
    public void Init_CarriesTheKindTheScriptAndTheProblems()
    {
        using var f = new Fixture();
        f.Ready();

        var profile = f.Last("init").GetProperty("profile");
        Assert.Equal("clicker", profile.GetProperty("data").GetProperty("kind").GetString());
        Assert.Equal(JsonValueKind.Array, profile.GetProperty("data").GetProperty("script").GetProperty("blocks").ValueKind);
        Assert.Empty(profile.GetProperty("issues").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, f.Last("init").GetProperty("runtime").GetProperty("block").ValueKind);
    }

    [Fact]
    public void NewProfile_OfKindScript_CreatesAndSavesAScript()
    {
        using var f = new Fixture();
        f.Ready();

        f.Send(new { type = "newProfile", kind = "script" });

        var profile = f.Last("profile").GetProperty("profile");
        Assert.Equal("Сценарий 2", profile.GetProperty("name").GetString());
        Assert.Equal("script", profile.GetProperty("data").GetProperty("kind").GetString());
        Assert.False(profile.GetProperty("dirty").GetBoolean());

        var block = Assert.Single(profile.GetProperty("data").GetProperty("script").GetProperty("blocks").EnumerateArray());
        Assert.Equal("press", block.GetProperty("type").GetString());
        Assert.Equal("Mouse1", block.GetProperty("input").GetProperty("key").GetString());

        Assert.Equal(ProfileKind.Script, f.Store.Load("Сценарий 2").Kind);
        Assert.Equal(ProfileKind.Script, f.Host.Engine.Profile.Kind);
        Assert.Contains("Профиль «Сценарий 2» создан", f.Logs());
    }

    [Fact]
    public void NewProfile_WithoutAKind_IsStillAClicker()
    {
        using var f = new Fixture();
        f.Ready();

        f.Send(new { type = "newProfile" });

        Assert.Equal("clicker", f.Last("profile").GetProperty("profile").GetProperty("data").GetProperty("kind").GetString());
        Assert.Equal("Профиль 2", f.Last("profile").GetProperty("profile").GetProperty("name").GetString());
    }

    [Fact]
    public void ScriptEdits_ReachTheEngineAndMarkTheProfileDirty()
    {
        using var f = new Fixture(StoreWith(Script("Combo", new PressBlock { Id = "a" })));
        f.Ready();

        f.SetProfile(data => Blocks(data).Add(Block("wait", "w", ("ms", 250))));

        Assert.True(f.Last("dirty").GetProperty("value").GetBoolean());
        var wait = Assert.IsType<WaitBlock>(f.Host.Engine.Profile.Script.Blocks[1]);
        Assert.Equal(250, wait.DurationMs);
        Assert.Single(f.Store.Load("Combo").Script.Blocks);

        f.Send(new { type = "saveProfile" });
        Assert.Equal(2, f.Store.Load("Combo").Script.Blocks.Count);
    }

    [Fact]
    public void AScriptSentBackUnchanged_IsNotDirty()
    {
        var script = Script(
            "Combo",
            new PressBlock { Id = "a", Label = "Клик", At = new ScreenPoint(5, 6) },
            new RepeatBlock { Id = "r", Mode = RepeatMode.Infinite, Blocks = [new WaitBlock { Id = "w", RandomizeEnabled = true }] });
        using var f = new Fixture(StoreWith(script));
        f.Ready();

        f.SetProfile(_ => { });

        Assert.False(f.Last("dirty").GetProperty("value").GetBoolean());
    }

    [Fact]
    public void APageWithoutScripts_KeepsTheScriptOfTheProfileItEdits()
    {
        using var f = new Fixture(StoreWith(Script("Combo", new PressBlock { Id = "a" })));
        f.Ready();

        // The interface of today knows nothing of scripts: it sends its own fields only.
        f.SetProfile(data =>
        {
            data.Remove("kind");
            data.Remove("script");
            data["delay"] = 42;
        });

        Assert.Equal(ProfileKind.Script, f.Host.Engine.Profile.Kind);
        Assert.Equal("a", Assert.Single(f.Host.Engine.Profile.Script.Blocks).Id);
    }

    [Fact]
    public void Issues_ArriveWhenAnEditBreaksABlock_AndOnlyWhenTheyChange()
    {
        using var f = new Fixture(StoreWith(Script("Combo", new PressBlock { Id = "a" })));
        f.Ready();
        Assert.Empty(f.Last("init").GetProperty("profile").GetProperty("issues").EnumerateArray());
        Assert.Empty(f.All("issues"));

        f.SetProfile(data => Blocks(data).Add(Block("repeat", "loop", ("count", 0), ("blocks", new JsonArray(Click("in"))))));

        var issue = Assert.Single(f.Last("issues").GetProperty("issues").EnumerateArray());
        Assert.Equal("loop", issue.GetProperty("block").GetString());
        Assert.Equal(new[] { 1 }, issue.GetProperty("path").EnumerateArray().Select(i => i.GetInt32()));
        Assert.Equal("count", issue.GetProperty("field").GetString());
        Assert.Equal("Количество повторов должно быть не меньше 1", issue.GetProperty("message").GetString());

        // Another edit that leaves the problem as it was says nothing new.
        f.SetProfile(data => data["delay"] = 150);
        Assert.Single(f.All("issues"));

        f.SetProfile(data => Blocks(data)[1]!["count"] = 3);
        Assert.Empty(f.Last("issues").GetProperty("issues").EnumerateArray());
    }

    [Fact]
    public void Start_RunsTheScriptAndStreamsTheBlockItIsOn()
    {
        var script = Script(
            "Combo",
            new PressBlock { Id = "a" },
            new RepeatBlock { Id = "r", Count = 2, Blocks = [new PressBlock { Id = "b", Input = new InputChord(VirtualKey.E) }] });
        script.Script.StepDelayMs = 30;
        using var f = new Fixture(StoreWith(script));
        f.Ready();

        f.Send(new { type = "start" });
        f.PumpUntil(() => f.Logs().Any(l => l.StartsWith("Выполнено", StringComparison.Ordinal)));

        Assert.Contains("Старт · сценарий, 3 блока", f.Logs());
        Assert.Contains("Выполнено · 3 клика", f.Logs());
        Assert.Equal(1, f.Live.Events.Count(e => e.Kind == InjectedEventKind.MouseDown));
        Assert.Equal(2, f.Live.Events.Count(e => e.Kind == InjectedEventKind.KeyDown));

        // Each block the page heard of, in order, then nothing once the run is over.
        var blocks = f.All("block").Select(m => m.GetProperty("block")).ToList();
        Assert.Equal(JsonValueKind.Null, blocks[^1].ValueKind);
        var ids = blocks.Where(b => b.ValueKind != JsonValueKind.Null).Select(b => b.GetProperty("id").GetString()).ToList();
        Assert.Equal("a", ids[0]);
        Assert.Equal(ids.OrderBy(id => id).ToList(), ids);

        var inner = blocks.Last(b => b.ValueKind != JsonValueKind.Null);
        Assert.Equal("b", inner.GetProperty("id").GetString());
        Assert.Equal(1, inner.GetProperty("pass").GetInt64());
        Assert.Equal(1, inner.GetProperty("passes").GetInt64());
        var loop = Assert.Single(inner.GetProperty("loops").EnumerateArray());
        Assert.Equal("r", loop.GetProperty("id").GetString());
        Assert.Equal(2, loop.GetProperty("iteration").GetInt64());
        Assert.Equal(2, loop.GetProperty("count").GetInt64());

        Assert.Equal(JsonValueKind.Null, f.Last("state").GetProperty("block").ValueKind);
        Assert.Equal(-1, f.Last("state").GetProperty("pointIndex").GetInt32());
    }

    [Fact]
    public void State_CarriesTheRunningBlockWhileARunIsActive()
    {
        var script = Script("Combo", new RepeatBlock { Id = "r", Mode = RepeatMode.Infinite, Blocks = [new WaitBlock { Id = "nap", DurationMs = 10_000 }] });
        using var f = new Fixture(StoreWith(script));
        f.Ready();

        f.Send(new { type = "start" });
        f.PumpUntil(() => f.Host.Engine.CurrentBlock?.BlockId == "nap");
        f.Bridge.Tick();

        var block = f.Last("state").GetProperty("block");
        Assert.Equal("nap", block.GetProperty("id").GetString());
        Assert.Equal(1, block.GetProperty("passes").GetInt64());
        Assert.Equal("r", block.GetProperty("loops")[0].GetProperty("id").GetString());
        Assert.Equal(JsonValueKind.Null, block.GetProperty("loops")[0].GetProperty("count").ValueKind);

        f.Send(new { type = "panic" });
        f.PumpUntil(() => f.Host.Engine.State == EngineState.Idle);
        Assert.Contains(f.Logs(), l => l.StartsWith("Аварийная остановка", StringComparison.Ordinal));
        Assert.Equal(JsonValueKind.Null, f.Last("block").GetProperty("block").ValueKind);
    }

    [Fact]
    public void Start_WithABrokenBlock_SaysWhichBlock()
    {
        using var f = new Fixture(StoreWith(Script("Combo", new PressBlock { Id = "a" })));
        f.Ready();

        f.SetProfile(data => Blocks(data).Add(Block("repeat", "loop", ("label", "Фарм"), ("count", 0), ("blocks", new JsonArray(Click("b"))))));

        f.Send(new { type = "start" });

        var toast = f.Last("toast");
        Assert.True(toast.GetProperty("error").GetBoolean());
        Assert.Equal("«Фарм»: Количество повторов должно быть не меньше 1", toast.GetProperty("message").GetString());
        Assert.Equal(EngineState.Idle, f.Host.Engine.State);

        // Saving refuses it the same way, and the stored script stays as it was.
        f.Send(new { type = "saveProfile" });
        Assert.Equal("«Фарм»: Количество повторов должно быть не меньше 1", f.Last("toast").GetProperty("message").GetString());
        Assert.Single(f.Store.Load("Combo").Script.Blocks);
    }

    [Fact]
    public void AnEndlessScript_RunsWithoutTheTimeLimitItWasSavedWith()
    {
        var script = Script("Combo", new RepeatBlock { Id = "r", Mode = RepeatMode.Infinite, Blocks = [new PressBlock { Id = "a" }] });
        script.Safety.MaxRunDurationSeconds = 600;
        using var f = new Fixture(StoreWith(script));
        f.Ready();

        Assert.Equal(0, f.Host.Engine.Profile.Safety.MaxRunDurationSeconds);
        Assert.False(f.Last("init").GetProperty("profile").GetProperty("dirty").GetBoolean());

        f.SetProfile(data => Blocks(data)[0]!["mode"] = "fixed");
        Assert.Equal(600, f.Host.Engine.Profile.Safety.MaxRunDurationSeconds);
    }

    private static ClickerProfile Script(string name, params ScriptBlock[] blocks)
    {
        var profile = UiDefaults.CreateProfile(name);
        profile.Kind = ProfileKind.Script;
        profile.Script.StepDelayMs = 1;
        profile.Script.Blocks = blocks.ToList();
        profile.Safety.ActiveWindowOnly = false;
        return profile;
    }

    private static InMemoryProfileStore StoreWith(ClickerProfile profile)
    {
        var store = new InMemoryProfileStore();
        store.Save(profile);
        store.LastUsedProfileName = profile.Name;
        return store;
    }

    private static JsonArray Blocks(JsonObject data) => data["script"]!["blocks"]!.AsArray();

    /// <summary>A «Нажать» left click, the way the page writes one.</summary>
    private static JsonObject Click(string id) =>
        Block("press", id, ("input", new JsonObject { ["mods"] = new JsonArray(), ["key"] = "Mouse1" }));

    private static JsonObject Block(string type, string id, params (string Name, JsonNode? Value)[] fields)
    {
        var block = new JsonObject { ["type"] = type, ["id"] = id, ["enabled"] = true, ["label"] = null };
        foreach (var (name, value) in fields)
        {
            block[name] = value;
        }

        return block;
    }
}
