using System.Text.Json;
using System.Text.Json.Nodes;
using ClickerEngine.Core.Configuration;
using ClickerEngine.Core.Profiles;
using Xunit;

namespace GhostClick.Bridge.Tests;

public class ScriptMappingTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public void ToDto_WritesEachBlockInThePagesVocabulary()
    {
        var profile = Script(
            new PressBlock { Id = "p", Label = "Клик", Input = new InputChord(VirtualKey.E, KeyModifiers.Control | KeyModifiers.Shift), At = new ScreenPoint(10, 20) },
            new HoldBlock { Id = "h", Input = InputChord.Mouse(MouseButton.XButton1) },
            new MoveBlock { Id = "m", To = new ScreenPoint(640, 480) },
            new WaitBlock { Id = "w", DurationMs = 300, RandomizeEnabled = true, MinDurationMs = 100, MaxDurationMs = 900 },
            new RepeatBlock { Id = "r", Mode = RepeatMode.Infinite, Blocks = [new StopBlock { Id = "s", Enabled = false }] });
        profile.Script.Runs = ScriptRunMode.FixedCount;
        profile.Script.RunCount = 3;
        profile.Script.StepDelayMs = 75;

        var data = JsonSerializer.SerializeToNode(ProfileMapper.ToDto(profile), Json)!;
        var script = data["script"]!;
        var blocks = script["blocks"]!.AsArray();

        Assert.Equal("script", data["kind"]!.GetValue<string>());
        Assert.Equal("fixed", script["repeat"]!.GetValue<string>());
        Assert.Equal(3, script["count"]!.GetValue<int>());
        Assert.Equal(75, script["stepDelay"]!.GetValue<int>());

        Assert.Equal(
            """{"type":"press","id":"p","enabled":true,"label":"Клик","input":{"mods":["Ctrl","Shift"],"key":"KeyE"},"press":30,"click":"single","dur":"own","at":{"x":10,"y":20}}""",
            Compact(blocks[0]));
        Assert.Equal(
            """{"type":"hold","id":"h","enabled":true,"label":null,"input":{"mods":[],"key":"Mouse4"}}""",
            Compact(blocks[1]));
        Assert.Equal(
            """{"type":"move","id":"m","enabled":true,"label":null,"to":{"x":640,"y":480},"ms":300}""",
            Compact(blocks[2]));
        Assert.Equal(
            """{"type":"wait","id":"w","enabled":true,"label":null,"unit":"ms","ms":300,"rand":true,"minMs":100,"maxMs":900}""",
            Compact(blocks[3]));
        Assert.Equal(
            """{"type":"repeat","id":"r","enabled":true,"label":null,"mode":"inf","count":2,"blocks":[{"type":"stop","id":"s","enabled":false,"label":null}]}""",
            Compact(blocks[4]));
    }

    [Fact]
    public void AClicker_IsSentWithItsKindAndAnEmptyScript()
    {
        var data = JsonSerializer.SerializeToNode(ProfileMapper.ToDto(ClickerProfile.CreateDefault()), Json)!;

        Assert.Equal("clicker", data["kind"]!.GetValue<string>());
        Assert.Equal("once", data["script"]!["repeat"]!.GetValue<string>());
        Assert.Empty(data["script"]!["blocks"]!.AsArray());
    }

    [Fact]
    public void RoundTrip_ThroughThePageLosesNothing()
    {
        var profile = Script(
            new PressBlock { Id = "p", Label = "Удочка", Input = InputChord.Mouse(MouseButton.Right), At = new ScreenPoint(-20, 5) },
            new PressBlock { Id = "raw", Input = new InputChord(VirtualKey.Sleep) },
            new HoldBlock { Id = "h", Input = new InputChord(VirtualKey.LeftShift) },
            new ReleaseBlock { Id = "r", Input = null },
            new MoveBlock { Id = "m", To = new ScreenPoint(1, 2), Enabled = false },
            new WaitBlock { Id = "w", DurationMs = 0 },
            new RepeatBlock { Id = "loop", Count = 7, Blocks = [new RepeatBlock { Id = "in", Mode = RepeatMode.Infinite, Blocks = [new StopBlock { Id = "s" }] }] },
            new UnknownBlock("waitPixel", new JsonObject { ["color"] = "#00ff00" }) { Id = "px", Label = "Поклёвка" });
        profile.Script.Runs = ScriptRunMode.Infinite;
        profile.Script.StepDelayMs = 0;
        var before = ProfileSerializer.Serialize(profile);

        var json = JsonSerializer.Serialize(ProfileMapper.ToDto(profile), Json);
        ProfileMapper.Apply(JsonSerializer.Deserialize<ProfileDto>(json, Json)!, profile);

        Assert.Equal(before, ProfileSerializer.Serialize(profile));
    }

    [Fact]
    public void Apply_KeepsTheDataOfABlockFromANewerVersion_AndTakesItsSwitchAndLabel()
    {
        var profile = Script(new UnknownBlock("waitPixel", new JsonObject { ["color"] = "#00ff00" }) { Id = "px" });

        var dto = ProfileMapper.ToDto(profile);
        dto.Script!.Blocks[0].Enabled = false;
        dto.Script.Blocks[0].Label = "Поклёвка";
        ProfileMapper.Apply(dto, profile);

        var block = Assert.IsType<UnknownBlock>(Assert.Single(profile.Script.Blocks));
        Assert.False(block.Enabled);
        Assert.Equal("Поклёвка", block.Label);
        Assert.Equal("#00ff00", block.Data["color"]!.GetValue<string>());
    }

    [Fact]
    public void Apply_ANewBlockTakesTheDefaultsOfItsType()
    {
        var profile = Script();
        var data = JsonNode.Parse("""
            {
              "kind": "script",
              "script": {
                "repeat": "once", "count": 1, "stepDelay": 100,
                "blocks": [
                  { "type": "wait", "id": "w" },
                  { "type": "repeat", "id": "r" },
                  { "type": "press", "id": "p" },
                  { "type": "press", "id": "bad", "input": { "key": "NoSuchKey", "mods": [] } },
                  { "type": "fromTheFuture", "id": "f" }
                ]
              }
            }
            """)!;

        ProfileMapper.Apply(data.Deserialize<ProfileDto>(Json)!, profile);

        var blocks = profile.Script.Blocks;
        Assert.Equal(new WaitBlock().DurationMs, Assert.IsType<WaitBlock>(blocks[0]).DurationMs);
        var repeat = Assert.IsType<RepeatBlock>(blocks[1]);
        Assert.Equal(RepeatMode.FixedCount, repeat.Mode);
        Assert.Empty(repeat.Blocks);

        // No input means no input: the block is shown as needing one, not quietly made a left click.
        Assert.Null(Assert.IsType<PressBlock>(blocks[2]).Input);
        Assert.Null(Assert.IsType<PressBlock>(blocks[3]).Input);
        Assert.Equal("fromTheFuture", Assert.IsType<UnknownBlock>(blocks[4]).Type);
    }

    [Fact]
    public void APageThatPredatesScripts_LeavesKindAndScriptAlone()
    {
        var profile = Script(new PressBlock { Id = "a" });
        var data = JsonSerializer.SerializeToNode(ProfileMapper.ToDto(profile), Json)!.AsObject();
        data.Remove("kind");
        data.Remove("script");
        data["delay"] = 333;

        ProfileMapper.Apply(data.Deserialize<ProfileDto>(Json)!, profile);

        Assert.Equal(ProfileKind.Script, profile.Kind);
        Assert.Equal("a", Assert.Single(profile.Script.Blocks).Id);
        Assert.Equal(333, profile.Interval.DelayMs);
    }

    [Fact]
    public void SwitchingTheKind_KeepsTheScript()
    {
        var profile = Script(new PressBlock { Id = "a" });

        var dto = ProfileMapper.ToDto(profile);
        dto.Kind = "clicker";
        ProfileMapper.Apply(dto, profile);

        Assert.Equal(ProfileKind.Clicker, profile.Kind);
        Assert.Equal("a", Assert.Single(profile.Script.Blocks).Id);
    }

    [Theory]
    [InlineData(ScriptRunMode.Infinite, RepeatMode.FixedCount, 0)]
    [InlineData(ScriptRunMode.Once, RepeatMode.Infinite, 0)]
    [InlineData(ScriptRunMode.FixedCount, RepeatMode.FixedCount, 600)]
    public void ToEngineProfile_LiftsTheTimeLimitOnlyForAScriptThatRunsUntilStopped(ScriptRunMode runs, RepeatMode inner, int expectedLimit)
    {
        var profile = Script(new RepeatBlock { Id = "r", Mode = inner, Blocks = [new PressBlock { Id = "a" }] });
        profile.Script.Runs = runs;
        profile.Safety.MaxRunDurationSeconds = 600;

        // The clicker's own repeat setting has no say over a script.
        profile.Repeat.Mode = RepeatMode.Infinite;

        Assert.Equal(expectedLimit, ProfileMapper.ToEngineProfile(profile).Safety.MaxRunDurationSeconds);
        Assert.Equal(600, profile.Safety.MaxRunDurationSeconds);
    }

    [Fact]
    public void Issues_PointAtTheBlockAndTheFieldInThePagesWords()
    {
        var profile = Script(
            new PressBlock { Id = "a" },
            new RepeatBlock
            {
                Id = "loop",
                Label = "Фарм",
                Blocks = [new WaitBlock { Id = "w", RandomizeEnabled = true, MinDurationMs = 900, MaxDurationMs = 100 }],
            });
        profile.Script.Runs = ScriptRunMode.FixedCount;
        profile.Script.RunCount = 0;

        var issues = ProfileValidator.Validate(profile)
            .Select(e => ScriptTexts.Issue(e, ScriptTexts.Message(e, profile.Script)!))
            .ToArray();

        Assert.Equal(2, issues.Length);

        Assert.Null(issues[0].Block);
        Assert.Null(issues[0].Path);
        Assert.Equal("count", issues[0].Field);
        Assert.Equal("Количество прогонов должно быть не меньше 1", issues[0].Message);

        Assert.Equal("w", issues[1].Block);
        Assert.Equal(new[] { 1, 0 }, issues[1].Path);
        Assert.Equal("maxMs", issues[1].Field);
        Assert.Equal("Максимальное время ожидания меньше минимального", issues[1].Message);
    }

    [Theory]
    [InlineData("press", "input", "Выберите клавишу или кнопку мыши")]
    [InlineData("repeat", "count", "Количество повторов должно быть не меньше 1")]
    [InlineData("emptyRepeat", "blocks", "Внутри повтора нет включённых блоков")]
    [InlineData("wait", "ms", "Время ожидания не может быть отрицательным")]
    [InlineData("unknown", "type", "Этот блок не поддерживается этой версией GhostClick")]
    public void EveryBlockProblem_HasAMessageInRussian(string kind, string field, string message)
    {
        ScriptBlock block = kind switch
        {
            "press" => new PressBlock { Id = "x", Input = null },
            "repeat" => new RepeatBlock { Id = "x", Count = 0, Blocks = [new PressBlock { Id = "in" }] },
            "emptyRepeat" => new RepeatBlock { Id = "x" },
            "wait" => new WaitBlock { Id = "x", DurationMs = -1 },
            _ => new UnknownBlock("later") { Id = "x" },
        };
        var profile = Script(block);

        var error = Assert.Single(ProfileValidator.Validate(profile));
        var issue = ScriptTexts.Issue(error, ScriptTexts.Message(error, profile.Script)!);

        Assert.Equal("x", issue.Block);
        Assert.Equal(new[] { 0 }, issue.Path);
        Assert.Equal(field, issue.Field);
        Assert.Equal(message, issue.Message);
    }

    [Fact]
    public void Describe_NamesTheBlockByItsLabelOrItsType()
    {
        var profile = Script(
            new RepeatBlock { Id = "r", Count = 0, Blocks = [new PressBlock { Id = "a" }] },
            new PressBlock { Id = "p", Label = " Закинуть ", Input = null });

        var errors = ProfileValidator.Validate(profile);

        Assert.Equal("«Повторить»: Количество повторов должно быть не меньше 1", ScriptTexts.Describe(errors[0], profile.Script));
        Assert.Equal("«Закинуть»: Выберите клавишу или кнопку мыши", ScriptTexts.Describe(errors[1], profile.Script));
        Assert.Equal(
            "«Повторить»: Количество повторов должно быть не меньше 1",
            UiBridge.Describe(new ProfileValidationException(errors), UiText.Russian, profile.Script));
    }

    [Fact]
    public void ScriptWideProblems_AreNotPinnedToABlock()
    {
        var empty = Script();
        var off = Script(new PressBlock { Id = "a", Enabled = false });

        Assert.Equal("Добавьте в сценарий хотя бы один блок", ScriptTexts.Describe(ProfileValidator.Validate(empty)[0], empty.Script));
        Assert.Equal("Все блоки сценария выключены", ScriptTexts.Describe(ProfileValidator.Validate(off)[0], off.Script));
        Assert.Equal("blocks", ScriptTexts.Issue(ProfileValidator.Validate(off)[0], "x").Field);
    }

    [Fact]
    public void ProblemsOutsideTheScript_AreLeftToTheProfilesOwnTexts()
    {
        var error = new ValidationError("Interval.MaxDelayMs", "Maximum delay is below minimum.");

        Assert.Null(ScriptTexts.Message(error, null));
        Assert.Null(ScriptTexts.Describe(error, null));

        var issue = ScriptTexts.Issue(error, "Максимальный интервал меньше минимального");
        Assert.Null(issue.Block);
        Assert.Null(issue.Path);
        Assert.Null(issue.Field);
    }

    [Fact]
    public void ANewScriptFromTheInterface_IsValidAndSafe()
    {
        var profile = UiDefaults.CreateScript("Сценарий 1");

        Assert.Equal(ProfileKind.Script, profile.Kind);
        Assert.Equal(VirtualKey.LeftButton, Assert.IsType<PressBlock>(Assert.Single(profile.Script.Blocks)).Input!.Key);
        Assert.True(profile.Safety.ActiveWindowOnly);
        Assert.Empty(ProfileValidator.Validate(profile));
    }

    private static ClickerProfile Script(params ScriptBlock[] blocks)
    {
        var profile = ClickerProfile.CreateDefault("Script");
        profile.Kind = ProfileKind.Script;
        profile.Script.Blocks = blocks.ToList();
        return profile;
    }

    private static string Compact(JsonNode? node) => node!.ToJsonString(new JsonSerializerOptions
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    });
}
