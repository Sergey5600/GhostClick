using System.Text.Json.Nodes;
using ClickerEngine.Core.Configuration;
using ClickerEngine.Core.Profiles;
using Xunit;

namespace ClickerEngine.Core.Tests;

/// <summary>Script profiles on disk and in the validator.</summary>
public class ScriptProfileTests
{
    [Fact]
    public void RoundTrip_PreservesEveryKindOfBlock()
    {
        var profile = ScriptPlannerTests.Script(
            new PressBlock { Id = "p", Label = "Закинуть", Input = new InputChord(VirtualKey.E, KeyModifiers.Control), At = new ScreenPoint(10, 20) },
            new HoldBlock { Id = "h", Input = InputChord.Mouse(MouseButton.Right) },
            new ReleaseBlock { Id = "r", Input = InputChord.Mouse(MouseButton.Right), At = new ScreenPoint(-5, 7) },
            new MoveBlock { Id = "m", To = new ScreenPoint(640, 480) },
            new WaitBlock { Id = "w", RandomizeEnabled = true, MinDurationMs = 100, MaxDurationMs = 900 },
            new RepeatBlock { Id = "loop", Mode = RepeatMode.Infinite, Enabled = false, Blocks = [new StopBlock { Id = "s" }] });
        profile.Script.Runs = ScriptRunMode.FixedCount;
        profile.Script.RunCount = 7;
        profile.Script.StepDelayMs = 40;
        ((PressBlock)profile.Script.Blocks[0]).Input!.PressMs = 75;

        var json = ProfileSerializer.Serialize(profile);
        var copy = ProfileSerializer.Deserialize(json);

        Assert.Equal(json, ProfileSerializer.Serialize(copy));
        Assert.Equal(ProfileKind.Script, copy.Kind);
        Assert.Equal(ScriptRunMode.FixedCount, copy.Script.Runs);
        Assert.Equal(7, copy.Script.RunCount);
        Assert.Equal(40, copy.Script.StepDelayMs);

        var press = Assert.IsType<PressBlock>(copy.Script.Blocks[0]);
        Assert.Equal("Закинуть", press.Label);
        Assert.Equal(new InputChord(VirtualKey.E, KeyModifiers.Control) { PressMs = 75 }, press.Input);
        Assert.Equal(new ScreenPoint(10, 20), press.At);
        Assert.IsType<HoldBlock>(copy.Script.Blocks[1]);
        Assert.Equal(new ScreenPoint(-5, 7), Assert.IsType<ReleaseBlock>(copy.Script.Blocks[2]).At);
        Assert.Equal(new ScreenPoint(640, 480), Assert.IsType<MoveBlock>(copy.Script.Blocks[3]).To);
        Assert.Equal(900, Assert.IsType<WaitBlock>(copy.Script.Blocks[4]).MaxDurationMs);

        var loop = Assert.IsType<RepeatBlock>(copy.Script.Blocks[5]);
        Assert.False(loop.Enabled);
        Assert.Equal(RepeatMode.Infinite, loop.Mode);
        Assert.Equal("s", Assert.IsType<StopBlock>(Assert.Single(loop.Blocks)).Id);
    }

    [Fact]
    public void Blocks_AreWrittenTaggedByTypeFirst()
    {
        var json = ProfileSerializer.Serialize(ScriptPlannerTests.Script(new WaitBlock { Id = "w", DurationMs = 250 }));

        var block = JsonNode.Parse(json)!["script"]!["blocks"]![0]!.AsObject();

        Assert.Equal(new[] { "type", "id", "enabled", "label" }, block.Select(p => p.Key).Take(4));
        Assert.Equal("wait", block["type"]!.GetValue<string>());
        Assert.Equal(250, block["durationMs"]!.GetValue<int>());
        Assert.Single(block, p => p.Key == "type");
    }

    [Fact]
    public void TheTypeTagMayComeAnywhereInAHandEditedBlock()
    {
        var profile = ProfileSerializer.Deserialize("""
            {
              "kind": "script",
              "script": { "blocks": [ { "id": "w", "durationMs": 300, "type": "Wait" } ] }
            }
            """);

        Assert.Equal(300, Assert.IsType<WaitBlock>(Assert.Single(profile.Script.Blocks)).DurationMs);
    }

    [Fact]
    public void AnOldProfile_LoadsAsAClickerExactlyAsBefore()
    {
        const string versionTwo = """
            {
              "version": 2,
              "name": "Fishing",
              "mode": "hold",
              "action": { "inputs": [ { "key": "rightButton", "modifiers": "none" } ], "holdDurationMs": 25 },
              "interval": { "delayMs": 1200 },
              "repeat": { "mode": "fixedCount", "count": 30 }
            }
            """;

        var profile = ProfileSerializer.Deserialize(versionTwo);

        Assert.Equal(ProfileKind.Clicker, profile.Kind);
        Assert.Empty(profile.Script.Blocks);
        Assert.Equal(ClickerProfile.CurrentVersion, profile.Version);
        Assert.Equal("Fishing", profile.Name);
        Assert.Equal(ClickMode.Hold, profile.Mode);
        Assert.Equal(new InputChord(VirtualKey.RightButton) { PressMs = 25 }, Assert.Single(profile.Action.Inputs));
        Assert.Equal(1200, profile.Interval.DelayMs);
        Assert.Equal(30, profile.Repeat.Count);

        // A stale or empty script never stops a clicker from running.
        Assert.Empty(ProfileValidator.Validate(profile));
    }

    [Fact]
    public void AnUnknownBlock_KeepsItsDataAndFailsValidationOnThatBlock()
    {
        const string fromNewerVersion = """
            {
              "kind": "script",
              "script": {
                "blocks": [
                  { "type": "press", "id": "a" },
                  { "type": "waitPixel", "id": "px", "label": "Поклёвка", "enabled": true,
                    "at": { "x": 5, "y": 6 }, "color": "#ff0000", "timeoutMs": 30000 }
                ]
              }
            }
            """;

        var profile = ProfileSerializer.Deserialize(fromNewerVersion);

        var unknown = Assert.IsType<UnknownBlock>(profile.Script.Blocks[1]);
        Assert.Equal("waitPixel", unknown.Type);
        Assert.Equal("px", unknown.Id);
        Assert.Equal("Поклёвка", unknown.Label);
        Assert.Equal("#ff0000", unknown.Data["color"]!.GetValue<string>());

        // Saved again, nothing is lost.
        var written = JsonNode.Parse(ProfileSerializer.Serialize(profile))!["script"]!["blocks"]![1]!.AsObject();
        Assert.Equal("waitPixel", written["type"]!.GetValue<string>());
        Assert.Equal(30000, written["timeoutMs"]!.GetValue<int>());
        Assert.Equal(6, written["at"]!["y"]!.GetValue<int>());
        Assert.Equal("Поклёвка", written["label"]!.GetValue<string>());

        var error = Assert.Single(ProfileValidator.Validate(profile));
        Assert.Equal("px", error.BlockId);
        Assert.Equal("Script.Blocks[1].Type", error.Path);
        Assert.Contains("waitPixel", error.Message);
    }

    [Fact]
    public void Clone_IsDeep()
    {
        var profile = ScriptPlannerTests.Script(
            new RepeatBlock { Id = "r", Blocks = [new PressBlock { Id = "a", Input = new InputChord(VirtualKey.A) }] },
            new UnknownBlock("future", new JsonObject { ["x"] = 1 }) { Id = "u" });

        var clone = profile.Clone();
        var inner = (PressBlock)((RepeatBlock)clone.Script.Blocks[0]).Blocks[0];
        inner.Input!.Key = VirtualKey.B;
        inner.Label = "changed";
        ((UnknownBlock)clone.Script.Blocks[1]).Data["x"] = 2;
        clone.Script.Blocks.Clear();

        var original = (PressBlock)((RepeatBlock)profile.Script.Blocks[0]).Blocks[0];
        Assert.Equal(VirtualKey.A, original.Input!.Key);
        Assert.Null(original.Label);
        Assert.Equal(1, ((UnknownBlock)profile.Script.Blocks[1]).Data["x"]!.GetValue<int>());
        Assert.Equal(2, profile.Script.Blocks.Count);
    }

    [Fact]
    public void Clone_DropsNullBlocksFromAHandEditedFile()
    {
        var profile = ProfileSerializer.Deserialize("""{ "kind": "script", "script": { "blocks": [ null, { "type": "stop", "id": "s" } ] } }""");

        Assert.Equal("s", Assert.Single(profile.Clone().Script.Blocks).Id);
        Assert.Contains(ProfileValidator.Validate(profile), e => e.Path == "Script.Blocks[0]");
    }

    [Fact]
    public void AValidScript_PassesWithoutTheClickersActionList()
    {
        var profile = ScriptPlannerTests.Script(new PressBlock { Id = "a" });
        profile.Action.Inputs = [];
        profile.Repeat.Count = 0;
        profile.Repeat.Mode = RepeatMode.FixedCount;
        profile.Target.Mode = TargetMode.MacroRoute;

        Assert.Empty(ProfileValidator.Validate(profile));
    }

    [Fact]
    public void AVersionThreeScript_GivesItsPressesTheProfilesOldPressLength()
    {
        // Version 3 held every «Нажать» for the action's holdDurationMs; version 4 keeps a
        // press length on each input, so the old one moves onto every press, nested or not.
        const string versionThree = """
            {
              "version": 3,
              "kind": "script",
              "action": { "inputs": [ { "key": "leftButton" } ], "holdDurationMs": 60 },
              "script": { "blocks": [
                { "type": "press", "id": "a", "input": { "key": "e" } },
                { "type": "hold", "id": "b", "input": { "key": "w" } },
                { "type": "repeat", "id": "r", "count": 2, "blocks": [ { "type": "press", "id": "c", "input": { "key": "leftButton" } } ] }
              ] }
            }
            """;

        var profile = ProfileSerializer.Deserialize(versionThree);

        Assert.Equal(ClickerProfile.CurrentVersion, profile.Version);
        Assert.Equal(60, Assert.Single(profile.Action.Inputs).PressMs);
        Assert.Equal(60, Assert.IsType<PressBlock>(profile.Script.Find("a")).Input!.PressMs);
        Assert.Equal(60, Assert.IsType<PressBlock>(profile.Script.Find("c")).Input!.PressMs);
        Assert.Equal(InputChord.DefaultPressMs, Assert.IsType<HoldBlock>(profile.Script.Find("b")).Input!.PressMs);
        Assert.DoesNotContain("holdDurationMs", ProfileSerializer.Serialize(profile));
    }

    [Fact]
    public void ScriptsStillCheckWhatTheyShareWithClickers()
    {
        var profile = ScriptPlannerTests.Script(new PressBlock { Id = "a" });
        profile.Action.RandomizeHoldDuration = true;
        profile.Action.PressSpreadPercent = 150;
        profile.Mode = ClickMode.Hold;
        profile.Hotkeys.StartStop = new HotkeyBinding();

        var paths = ProfileValidator.Validate(profile).Select(e => e.Path).ToArray();

        Assert.Contains("Action.PressSpreadPercent", paths);
        Assert.Contains("Hotkeys.StartStop", paths);
    }

    [Fact]
    public void AnEmptyScript_IsRefused()
    {
        var error = Assert.Single(ProfileValidator.Validate(ScriptPlannerTests.Script()));

        Assert.Equal("Script.Blocks", error.Path);
        Assert.Null(error.BlockId);
    }

    [Fact]
    public void AScriptWithEveryBlockSwitchedOff_IsRefused()
    {
        var profile = ScriptPlannerTests.Script(new PressBlock { Id = "a", Enabled = false, Input = null });

        var error = Assert.Single(ProfileValidator.Validate(profile));

        Assert.Equal("Script.Blocks", error.Path);
        Assert.Contains("switched off", error.Message);
    }

    [Fact]
    public void BlockErrors_NameTheBlockAndTheField()
    {
        var profile = ScriptPlannerTests.Script(
            new PressBlock { Id = "ok" },
            new RepeatBlock
            {
                Id = "loop",
                Count = 0,
                Blocks =
                [
                    new PressBlock { Id = "nokey", Input = null },
                    new WaitBlock { Id = "range", RandomizeEnabled = true, MinDurationMs = 500, MaxDurationMs = 100 },
                    new WaitBlock { Id = "negative", DurationMs = -1 },
                    new HoldBlock { Id = "none", Input = new InputChord(VirtualKey.None) },
                    new RepeatBlock { Id = "empty", Blocks = [new ReleaseBlock { Id = "off", Enabled = false }] },
                ],
            });

        var errors = ProfileValidator.Validate(profile);

        Assert.Equal(
            new (string?, string)[]
            {
                ("loop", "Script.Blocks[1].Count"),
                ("nokey", "Script.Blocks[1].Blocks[0].Input"),
                ("range", "Script.Blocks[1].Blocks[1].MaxDurationMs"),
                ("negative", "Script.Blocks[1].Blocks[2].DurationMs"),
                ("none", "Script.Blocks[1].Blocks[3].Input"),
                ("empty", "Script.Blocks[1].Blocks[4].Blocks"),
            },
            errors.Select(e => (e.BlockId, e.Path)));
    }

    [Fact]
    public void SwitchedOffBlocks_AreNotChecked()
    {
        var profile = ScriptPlannerTests.Script(
            new PressBlock { Id = "a" },
            new PressBlock { Id = "draft", Enabled = false, Input = null },
            new RepeatBlock { Id = "parked", Enabled = false, Count = -3, Blocks = [new WaitBlock { Id = "w", DurationMs = -5 }] });

        Assert.Empty(ProfileValidator.Validate(profile));
    }

    [Fact]
    public void BlockIds_MustBePresentAndUnique_EvenInSwitchedOffBlocks()
    {
        var profile = ScriptPlannerTests.Script(
            new PressBlock { Id = "same" },
            new RepeatBlock { Id = "r", Enabled = false, Blocks = [new PressBlock { Id = "same" }] },
            new PressBlock { Id = "" });

        var errors = ProfileValidator.Validate(profile);

        Assert.Equal(
            new[] { ("same", "Script.Blocks[1].Blocks[0].Id"), ((string?)null, "Script.Blocks[2].Id") },
            errors.Select(e => (e.BlockId, e.Path)));
    }

    [Fact]
    public void ScriptSettings_AreChecked()
    {
        var profile = ScriptPlannerTests.Script(new PressBlock { Id = "a" });
        profile.Script.Runs = ScriptRunMode.FixedCount;
        profile.Script.RunCount = 0;
        profile.Script.StepDelayMs = -1;

        Assert.Equal(
            new[] { "Script.RunCount", "Script.StepDelayMs" },
            ProfileValidator.Validate(profile).Select(e => e.Path));
    }

    [Fact]
    public void AClicker_IgnoresItsScript()
    {
        var profile = ClickerProfile.CreateDefault();
        profile.Script.Blocks = [new PressBlock { Id = "broken", Input = null }];

        Assert.Empty(ProfileValidator.Validate(profile));
    }

    [Fact]
    public void IsEndless_WhenTheScriptOrAnEnabledRepeatInsideItNeverEnds()
    {
        var script = new ScriptSettings { Blocks = [new PressBlock(), new RepeatBlock { Mode = RepeatMode.Infinite, Enabled = false }] };
        Assert.False(script.IsEndless);

        script.Runs = ScriptRunMode.Infinite;
        Assert.True(script.IsEndless);

        script.Runs = ScriptRunMode.Once;
        script.Blocks[1].Enabled = true;
        Assert.True(script.IsEndless);
    }

    [Fact]
    public void Find_LooksInsideRepeatsAndSwitchedOffBlocks()
    {
        var script = new ScriptSettings
        {
            Blocks = [new RepeatBlock { Id = "r", Enabled = false, Blocks = [new MoveBlock { Id = "deep" }] }],
        };

        Assert.IsType<MoveBlock>(script.Find("deep"));
        Assert.Null(script.Find("missing"));
        Assert.Null(script.Find(null));
    }

    [Fact]
    public void NewBlocks_GetDistinctIds()
    {
        var ids = Enumerable.Range(0, 200).Select(_ => new PressBlock().Id).ToArray();

        Assert.All(ids, id => Assert.Equal(8, id.Length));
        Assert.Equal(ids.Length, ids.Distinct().Count());
    }
}
