using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ClickerEngine.Core.Configuration;
using ClickerEngine.Core.Profiles;
using GhostClick.Bridge.Updates;
using Xunit;
using Fixture = GhostClick.Bridge.Tests.UiBridgeTests.Fixture;

namespace GhostClick.Bridge.Tests;

/// <summary>The scenario editor's new blocks and test run, and the update dialog, over the page protocol.</summary>
public class ScenarioEditorTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public void NewBlocks_RoundTripThroughThePage()
    {
        var profile = Script(
            "All",
            new PressBlock { Id = "p", Click = ClickType.Double, ProfilePress = true },
            new HoldBlock { Id = "hold", Input = new InputChord(VirtualKey.W) },
            new HoldForBlock { Id = "h", Input = new InputChord(VirtualKey.E), RandomizeEnabled = true, MinDurationMs = 1000, MaxDurationMs = 2000 },
            new ScrollBlock { Id = "s", Direction = ScrollDirection.Up, Notches = 5, At = new ScreenPoint(3, 4) },
            new DragBlock { Id = "d", Button = MouseButton.Right, From = new ScreenPoint(1, 2), To = new ScreenPoint(30, 40), Speed = DragSpeed.Fast },
            new TypeTextBlock { Id = "t", Text = "/trade", CharDelayMs = 15 },
            new MoveBlock { Id = "m", To = new ScreenPoint(9, 9), Movement = MovementMode.Smooth, DurationMs = 450 },
            new MoveBlock { Id = "old", To = new ScreenPoint(9, 9) },
            new ShiftBlock { Id = "sh", Dx = -10, Dy = 5 },
            new WaitBlock { Id = "w", InSeconds = true, DurationMs = 2500 },
            new WaitKeyBlock { Id = "k", Input = new InputChord(VirtualKey.Space) },
            new RandomBlock { Id = "r", Blocks = [new PressBlock { Id = "a" }, new LabelBlock { Id = "l2", Text = "x" }] },
            new LabelBlock { Id = "l", Text = "— Подготовка —" },
            new ReleaseBlock { Id = "rel", Input = new InputChord(VirtualKey.W) });
        var before = ProfileSerializer.Serialize(profile);

        var json = JsonSerializer.Serialize(ProfileMapper.ToDto(profile), Json);
        ProfileMapper.Apply(JsonSerializer.Deserialize<ProfileDto>(json, Json)!, profile);

        Assert.Equal(before, ProfileSerializer.Serialize(profile));

        var blocks = JsonNode.Parse(json)!["script"]!["blocks"]!.AsArray();
        Assert.Equal("double", blocks[0]!["click"]!.GetValue<string>());
        Assert.Equal("profile", blocks[0]!["dur"]!.GetValue<string>());
        Assert.Equal("up", blocks[3]!["dir"]!.GetValue<string>());
        Assert.Equal(5, blocks[3]!["count"]!.GetValue<int>());
        Assert.Equal("Mouse2", blocks[4]!["input"]!["key"]!.GetValue<string>());
        Assert.Equal("fast", blocks[4]!["speed"]!.GetValue<string>());
        Assert.Equal("smooth", blocks[6]!["how"]!.GetValue<string>());
        Assert.Null(blocks[7]!["how"]);
        Assert.Equal("s", blocks[9]!["unit"]!.GetValue<string>());
    }

    [Fact]
    public void AReleaseOfNothingHeld_IsAnIssueOnThatBlock()
    {
        using var f = new Fixture(StoreWith(Script("Combo", new PressBlock { Id = "a" })));
        f.Ready();

        f.SetProfile(data => Blocks(data).Add(Block("release", "rel", ("input", Key("KeyW")))));

        var issue = Assert.Single(f.Last("issues").GetProperty("issues").EnumerateArray());
        Assert.Equal("rel", issue.GetProperty("block").GetString());
        Assert.Equal("held", issue.GetProperty("field").GetString());
        Assert.Equal("Выше по сценарию ничего не зажато", issue.GetProperty("message").GetString());

        f.SetProfile(data => Blocks(data).Insert(0, Block("hold", "hold", ("input", Key("KeyQ")))));
        issue = Assert.Single(f.Last("issues").GetProperty("issues").EnumerateArray());
        Assert.Equal("W не зажата выше по сценарию", issue.GetProperty("message").GetString());
    }

    [Fact]
    public void TestRun_IsADryRun_ReportsBlocks_AndPutsTheDryRunSwitchBack()
    {
        using var f = new Fixture(StoreWith(Script("Combo", new PressBlock { Id = "a" }, new ScrollBlock { Id = "s" })));
        f.Ready();

        f.Send(new { type = "testRun" });

        Assert.True(f.Last("test").GetProperty("active").GetBoolean());
        f.PumpUntil(() => f.All("test").Any(m => !m.GetProperty("active").GetBoolean()));

        var end = f.All("test").Last();
        Assert.True(end.GetProperty("completed").GetBoolean());
        Assert.Contains(f.All("block"), m => m.GetProperty("block").ValueKind == JsonValueKind.Object);
        Assert.Empty(f.Live.Events);
        Assert.False(f.Injector.DryRun);
        Assert.Equal("Combo", f.Host.Engine.Profile.Name);
        Assert.Null(f.Host.Engine.Profile.Script.StartBlockId);
    }

    [Fact]
    public void TestRun_FromABlock_StartsThere()
    {
        using var f = new Fixture(StoreWith(Script(
            "Combo",
            new PressBlock { Id = "a", Input = new InputChord(VirtualKey.A) },
            new PressBlock { Id = "b", Input = new InputChord(VirtualKey.B) })));
        f.Ready();

        f.Send(new { type = "testRun", from = "b" });
        f.PumpUntil(() => f.All("test").Any(m => !m.GetProperty("active").GetBoolean()));

        Assert.Equal(new[] { "KeyDown B", "KeyUp B" }, f.Injector.Recorder.Events.Select(e => e.ToString()));
    }

    [Fact]
    public void TestRun_OfABrokenScript_NamesTheBlock()
    {
        using var f = new Fixture(StoreWith(Script("Combo", new PressBlock { Id = "a" }, new TypeTextBlock { Id = "t", Text = "" })));
        f.Ready();

        f.Send(new { type = "testRun" });

        var error = f.Last("testError");
        Assert.Equal("t", error.GetProperty("block").GetString());
        Assert.Contains("Пустой текст", error.GetProperty("message").GetString());
        Assert.Empty(f.All("test"));
    }

    [Fact]
    public void Parse_OffersOnlyANewerReleaseWithAChecksum()
    {
        Assert.Equal("1.2.0", Updater.Parse(Release("v1.2.0"), "1.1.0")!.Version);
        Assert.Null(Updater.Parse(Release("v1.1.0"), "1.1.0"));
        Assert.Null(Updater.Parse(Release("v1.0.9"), "1.1.0"));
        Assert.Null(Updater.Parse(Release("v1.2.0", checksum: false), "1.1.0"));
        Assert.Null(Updater.Parse(Release("v1.2.0", host: "evil.example"), "1.1.0"));
        Assert.Null(Updater.Parse(Release("v1.3.0-beta"), "1.1.0"));
    }

    [Fact]
    public void TestBuilds_DoNotCheckOnTheirOwn()
    {
        using var http = new HttpClient(new FakeGitHub());
        Assert.True(new Updater("1.1.0+abc123", http, Temp()).ChecksAutomatically);
        Assert.False(new Updater("1.2.0-dev.41", http, Temp()).ChecksAutomatically);
        Assert.False(new Updater("0.0.0", http, Temp()).ChecksAutomatically);
    }

    [Fact]
    public async Task Download_ChecksTheInstallerAgainstItsChecksum()
    {
        var folder = Temp();
        using var http = new HttpClient(new FakeGitHub());
        var updater = new Updater("1.1.0", http, folder);
        var update = (await updater.CheckAsync())!;

        long reported = 0;
        var path = await updater.DownloadAsync(update, new Progress<long>(n => reported = n));

        Assert.Equal(FakeGitHub.Setup, await File.ReadAllBytesAsync(path));
        Assert.False(File.Exists(path + ".part"));

        using var bad = new HttpClient(new FakeGitHub { Corrupt = true });
        await Assert.ThrowsAsync<UpdateChecksumException>(() => new Updater("1.1.0", bad, folder).DownloadAsync(update));
        Assert.False(File.Exists(path + ".part"));
    }

    [Fact]
    public void TheBridge_OffersDownloadsAndInstallsAnUpdate()
    {
        var host = new FakeUpdateHost();
        using var http = new HttpClient(new FakeGitHub());
        using var f = new Fixture(updater: new Updater("1.1.0", http, Temp()), updateHost: host);
        f.Ready();

        f.PumpUntil(() => f.All("updateAvailable").Any());
        var offer = f.Last("updateAvailable");
        Assert.Equal("1.2.0", offer.GetProperty("version").GetString());

        f.Send(new { type = "updateStart" });
        f.PumpUntil(() => f.All("updateReady").Any());
        Assert.Contains(f.All("updateProgress"), m => m.GetProperty("pct").GetDouble() == 100);

        f.Send(new { type = "updateRestart" });
        Assert.NotNull(host.Setup);
        Assert.True(host.Relaunch);
        Assert.True(host.Exited);
    }

    [Fact]
    public void ASkippedVersion_IsNotOfferedAgain()
    {
        var settings = new InMemoryUiSettingsStore();
        using (var http = new HttpClient(new FakeGitHub()))
        using (var f = new Fixture(settings: settings, updater: new Updater("1.1.0", http, Temp()), updateHost: new FakeUpdateHost()))
        {
            f.Ready();
            f.PumpUntil(() => f.All("updateAvailable").Any());
            f.Send(new { type = "updateSkip", version = "1.2.0" });
        }

        Assert.Equal("1.2.0", settings.Load().SkippedVersion);

        using (var http = new HttpClient(new FakeGitHub()))
        using (var f = new Fixture(settings: settings, updater: new Updater("1.1.0", http, Temp()), updateHost: new FakeUpdateHost()))
        {
            f.Ready();

            // The check runs in the background; give it ample time to answer.
            var until = DateTime.UtcNow.AddMilliseconds(500);
            while (DateTime.UtcNow < until)
            {
                f.Pump();
                Thread.Sleep(5);
            }

            Assert.Empty(f.All("updateAvailable"));
        }
    }

    // ------------------------------------------------------------------ helpers

    private static JsonElement Release(string tag, bool checksum = true, string host = "github.com")
    {
        var version = tag.TrimStart('v');
        var url = $"https://{host}/Sergey5600/GhostClick/releases/download/{tag}/GhostClick-Setup-{version}.exe";
        var assets = new JsonArray(new JsonObject { ["name"] = $"GhostClick-Setup-{version}.exe", ["browser_download_url"] = url, ["size"] = 1234 });
        if (checksum)
        {
            assets.Add(new JsonObject { ["name"] = $"GhostClick-Setup-{version}.exe.sha256", ["browser_download_url"] = url + ".sha256" });
        }

        return JsonDocument.Parse(new JsonObject { ["tag_name"] = tag, ["assets"] = assets }.ToJsonString()).RootElement;
    }

    private static string Temp() => Path.Combine(Path.GetTempPath(), "gc-update-tests", Guid.NewGuid().ToString("N"));

    private static ClickerProfile Script(string name, params ScriptBlock[] blocks)
    {
        var profile = UiDefaults.CreateProfile(name);
        profile.Kind = ProfileKind.Script;
        profile.Script.StepDelayMs = 1;
        profile.Script.Blocks = blocks.ToList();
        profile.Safety.ActiveWindowOnly = false;
        foreach (var input in ScriptSettings.Walk(profile.Script.Blocks).OfType<PressBlock>().Select(b => b.Input))
        {
            if (input is not null)
            {
                input.PressMs = 1;
            }
        }

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

    private static JsonObject Key(string code) => new() { ["mods"] = new JsonArray(), ["key"] = code };

    private static JsonObject Block(string type, string id, params (string Name, JsonNode? Value)[] fields)
    {
        var block = new JsonObject { ["type"] = type, ["id"] = id, ["enabled"] = true, ["label"] = null };
        foreach (var (name, value) in fields)
        {
            block[name] = value;
        }

        return block;
    }

    /// <summary>GitHub with release 1.2.0: the API, the installer and its checksum.</summary>
    private sealed class FakeGitHub : HttpMessageHandler
    {
        public static readonly byte[] Setup = Encoding.ASCII.GetBytes(new string('x', 300_000));

        public bool Corrupt { get; init; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.AbsoluteUri;
            HttpContent content;
            if (url == Updater.LatestReleaseUri.AbsoluteUri)
            {
                Assert.NotEmpty(request.Headers.UserAgent);
                content = new StringContent(Release("v1.2.0").GetRawText().Replace("1234", Setup.Length.ToString()));
            }
            else if (url.EndsWith(".exe.sha256", StringComparison.Ordinal))
            {
                content = new StringContent($"{Convert.ToHexString(SHA256.HashData(Setup)).ToLowerInvariant()}  GhostClick-Setup-1.2.0.exe\n");
            }
            else if (url.EndsWith(".exe", StringComparison.Ordinal))
            {
                var body = Corrupt ? Setup.Select(b => (byte)(b + 1)).ToArray() : Setup;
                content = new ByteArrayContent(body);
            }
            else
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }
    }

    private sealed class FakeUpdateHost : IUpdateHost
    {
        public string? Setup { get; private set; }

        public bool Relaunch { get; private set; }

        public bool Exited { get; private set; }

        public void InstallOnExit(string setupPath, bool relaunch)
        {
            Setup = setupPath;
            Relaunch = relaunch;
        }

        public void Exit() => Exited = true;
    }
}
