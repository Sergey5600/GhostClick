using ClickerEngine.Core.Configuration;
using ClickerEngine.Core.Profiles;
using Xunit;

namespace ClickerEngine.Core.Tests;

public class ProfileTests
{
    [Fact]
    public void Serialization_RoundTripsEverySetting()
    {
        var profile = ClickerProfile.CreateDefault("Round trip");
        profile.Mode = ClickMode.Hold;
        profile.Action.Kind = ActionKind.Keyboard;
        profile.Action.Key = VirtualKey.F4;
        profile.Action.KeyModifiers = KeyModifiers.Alt | KeyModifiers.Shift;
        profile.Action.ClickType = ClickType.Double;
        profile.Action.HoldDurationMs = 42;
        profile.Interval.RandomizeEnabled = true;
        profile.Interval.MinDelayMs = 15;
        profile.Interval.MaxDelayMs = 95;
        profile.Repeat.Mode = RepeatMode.FixedCount;
        profile.Repeat.Count = 250;
        profile.Target.Mode = TargetMode.MacroRoute;
        profile.Target.Route =
        [
            new ClickPoint(11, 22, 33, "first"),
            new ClickPoint(44, 55) { Enabled = false },
        ];
        profile.Movement.Mode = MovementMode.Smooth;
        profile.Movement.Easing = EasingMode.EaseOut;
        profile.Movement.DurationMs = 120;
        profile.Jitter.PositionJitterEnabled = true;
        profile.Jitter.TimingJitterPercent = 17.5;
        profile.Directional.AngleDegrees = 137.5;
        profile.Directional.OnBounds = BoundsBehavior.Wrap;
        profile.Hotkeys.StartStop = new HotkeyBinding(VirtualKey.XButton2, KeyModifiers.Control);
        profile.Safety.MaxActionsPerRun = 9000;

        var restored = ProfileSerializer.Deserialize(ProfileSerializer.Serialize(profile));

        Assert.Equal(profile.Name, restored.Name);
        Assert.Equal(ClickMode.Hold, restored.Mode);
        Assert.Equal(VirtualKey.F4, restored.Action.Key);
        Assert.Equal(KeyModifiers.Alt | KeyModifiers.Shift, restored.Action.KeyModifiers);
        Assert.Equal(ClickType.Double, restored.Action.ClickType);
        Assert.Equal(42, restored.Action.HoldDurationMs);
        Assert.True(restored.Interval.RandomizeEnabled);
        Assert.Equal(15, restored.Interval.MinDelayMs);
        Assert.Equal(95, restored.Interval.MaxDelayMs);
        Assert.Equal(250, restored.Repeat.Count);
        Assert.Equal(2, restored.Target.Route.Count);
        Assert.Equal("first", restored.Target.Route[0].Label);
        Assert.Equal(33, restored.Target.Route[0].DelayMs);
        Assert.False(restored.Target.Route[1].Enabled);
        Assert.Equal(EasingMode.EaseOut, restored.Movement.Easing);
        Assert.Equal(17.5, restored.Jitter.TimingJitterPercent);
        Assert.Equal(137.5, restored.Directional.AngleDegrees);
        Assert.Equal(BoundsBehavior.Wrap, restored.Directional.OnBounds);
        Assert.Equal(VirtualKey.XButton2, restored.Hotkeys.StartStop.Key);
        Assert.Equal(KeyModifiers.Control, restored.Hotkeys.StartStop.Modifiers);
        Assert.Equal(9000, restored.Safety.MaxActionsPerRun);
    }

    [Fact]
    public void Serialization_WritesEnumsAsNamesSoFilesStayReadable()
    {
        var profile = ClickerProfile.CreateDefault();
        profile.Target.Mode = TargetMode.MacroRoute;

        var json = ProfileSerializer.Serialize(profile);

        Assert.Contains("\"macroRoute\"", json);
        Assert.DoesNotContain("\"mode\": 2", json);
    }

    [Fact]
    public void Deserialization_RejectsGarbage()
    {
        Assert.Throws<ProfileFormatException>(() => ProfileSerializer.Deserialize("{ not json"));
    }

    [Fact]
    public void Clone_IsDeep()
    {
        var profile = ClickerProfile.CreateDefault();
        profile.Target.Route = [new ClickPoint(1, 2)];

        var clone = profile.Clone();
        clone.Target.Route[0].X = 999;
        clone.Interval.DelayMs = 999;
        clone.Hotkeys.StartStop.Key = VirtualKey.F12;

        Assert.Equal(1, profile.Target.Route[0].X);
        Assert.Equal(100, profile.Interval.DelayMs);
        Assert.Equal(VirtualKey.F6, profile.Hotkeys.StartStop.Key);
    }

    [Fact]
    public void JsonStore_SavesLoadsListsAndDeletes()
    {
        using var temp = new TempDirectory();
        var store = new JsonProfileStore(temp.Path);

        Assert.Empty(store.List());

        var profile = ClickerProfile.CreateDefault("Fishing");
        profile.Interval.DelayMs = 750;
        store.Save(profile);

        Assert.True(store.Exists("Fishing"));
        Assert.Equal(new[] { "Fishing" }, store.List());
        Assert.Equal(750, store.Load("Fishing").Interval.DelayMs);

        store.Save(ClickerProfile.CreateDefault("Mining"));
        Assert.Equal(new[] { "Fishing", "Mining" }, store.List());

        Assert.True(store.Delete("Fishing"));
        Assert.False(store.Delete("Fishing"));
        Assert.Equal(new[] { "Mining" }, store.List());
    }

    [Fact]
    public void JsonStore_RemembersTheLastUsedProfileAcrossInstances()
    {
        using var temp = new TempDirectory();

        var first = new JsonProfileStore(temp.Path);
        first.Save(ClickerProfile.CreateDefault("Alpha"));
        first.LastUsedProfileName = "Alpha";

        var second = new JsonProfileStore(temp.Path);
        Assert.Equal("Alpha", second.LastUsedProfileName);
    }

    [Fact]
    public void JsonStore_ForgetsTheLastUsedProfileWhenItIsDeleted()
    {
        using var temp = new TempDirectory();
        var store = new JsonProfileStore(temp.Path);

        store.Save(ClickerProfile.CreateDefault("Alpha"));
        store.LastUsedProfileName = "Alpha";
        store.Delete("Alpha");

        Assert.Null(store.LastUsedProfileName);
    }

    [Fact]
    public void JsonStore_HandlesNamesThatAreNotValidFileNames()
    {
        using var temp = new TempDirectory();
        var store = new JsonProfileStore(temp.Path);

        var profile = ClickerProfile.CreateDefault("raid: boss/phase 2");
        store.Save(profile);

        Assert.True(store.Exists("raid: boss/phase 2"));
        Assert.Equal("raid: boss/phase 2", store.Load("raid: boss/phase 2").Name);
        Assert.Equal(new[] { "raid: boss/phase 2" }, store.List());
    }

    [Fact]
    public void JsonStore_LeavesTheSettingsFileOutOfTheProfileList()
    {
        using var temp = new TempDirectory();
        var store = new JsonProfileStore(temp.Path);

        store.Save(ClickerProfile.CreateDefault("Only"));
        store.LastUsedProfileName = "Only";

        Assert.Equal(new[] { "Only" }, store.List());
        Assert.True(File.Exists(store.SettingsPath));
    }

    [Fact]
    public void JsonStore_ThrowsForAMissingProfile()
    {
        using var temp = new TempDirectory();
        var store = new JsonProfileStore(temp.Path);

        var error = Assert.Throws<ProfileNotFoundException>(() => store.Load("nope"));
        Assert.Equal("nope", error.ProfileName);
    }

    [Fact]
    public void JsonStore_WritesAtomicallyAndLeavesNoTempFilesBehind()
    {
        using var temp = new TempDirectory();
        var store = new JsonProfileStore(temp.Path);

        store.Save(ClickerProfile.CreateDefault("Keep"));
        var before = File.ReadAllText(Path.Combine(store.ProfilesDirectory, "Keep.json"));

        store.Save(ClickerProfile.CreateDefault("Keep"));
        Assert.Equal(before, File.ReadAllText(Path.Combine(store.ProfilesDirectory, "Keep.json")));
        Assert.Empty(Directory.GetFiles(store.ProfilesDirectory, "*.tmp"));
    }

    [Fact]
    public void Manager_AutoLoadsTheLastUsedProfile()
    {
        var store = new InMemoryProfileStore();
        store.Save(ClickerProfile.CreateDefault("Alpha"));

        var beta = ClickerProfile.CreateDefault("Beta");
        beta.Interval.DelayMs = 33;
        store.Save(beta);
        store.LastUsedProfileName = "Beta";

        var manager = new ProfileManager(store);
        var loaded = manager.LoadLastUsedOrDefault();

        Assert.Equal("Beta", loaded.Name);
        Assert.Equal(33, loaded.Interval.DelayMs);
    }

    [Fact]
    public void Manager_FallsBackToTheFirstProfileWhenTheLastUsedOneIsGone()
    {
        var store = new InMemoryProfileStore();
        store.Save(ClickerProfile.CreateDefault("Alpha"));
        store.LastUsedProfileName = "Deleted";

        var manager = new ProfileManager(store);

        Assert.Equal("Alpha", manager.LoadLastUsedOrDefault().Name);
    }

    [Fact]
    public void Manager_CreatesAndPersistsADefaultWhenTheStoreIsEmpty()
    {
        var store = new InMemoryProfileStore();
        var manager = new ProfileManager(store);

        var profile = manager.LoadLastUsedOrDefault();

        Assert.Equal("Default", profile.Name);
        Assert.Equal(new[] { "Default" }, store.List());
        Assert.Equal("Default", store.LastUsedProfileName);
    }

    [Fact]
    public void Manager_SaveAsCopiesUnderANewNameAndSwitchesToIt()
    {
        var store = new InMemoryProfileStore();
        var manager = new ProfileManager(store);
        manager.Current.Interval.DelayMs = 61;

        manager.SaveAs("Copy");

        Assert.Equal("Copy", manager.Current.Name);
        Assert.Equal(61, store.Load("Copy").Interval.DelayMs);
        Assert.Equal("Copy", store.LastUsedProfileName);
    }

    [Fact]
    public void Manager_RaisesAnEventWhenTheCurrentProfileChanges()
    {
        var store = new InMemoryProfileStore();
        store.Save(ClickerProfile.CreateDefault("Target"));

        var manager = new ProfileManager(store);
        ClickerProfile? seen = null;
        manager.CurrentProfileChanged += (_, e) => seen = e.Profile;

        manager.Load("Target");

        Assert.NotNull(seen);
        Assert.Equal("Target", seen!.Name);
    }

    [Fact]
    public void Manager_RefusesToSaveAnInvalidProfile()
    {
        var store = new InMemoryProfileStore();
        var manager = new ProfileManager(store);
        manager.Current.Interval.DelayMs = -5;

        Assert.Throws<ProfileValidationException>(() => manager.Save());
        Assert.Empty(store.List());
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ghostclick-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
                // A leftover temp directory is not worth failing a test over.
            }
        }
    }
}
