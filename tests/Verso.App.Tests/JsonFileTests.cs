using System.IO;
using Verso.App.Storage;
using Xunit;

namespace Verso.App.Tests;

public class JsonFileTests
{
    private sealed class Sample
    {
        public string Name { get; set; } = "";
        public int Count { get; set; }
    }

    [Fact]
    public void Round_trips()
    {
        using var dir = new TempDir();
        JsonFile.Write(dir.File("a.json"), new Sample { Name = "x", Count = 3 });

        var read = JsonFile.Read(dir.File("a.json"), new Sample());

        Assert.Equal("x", read.Name);
        Assert.Equal(3, read.Count);
    }

    [Fact]
    public void Missing_file_gives_the_fallback()
    {
        using var dir = new TempDir();
        Assert.Equal("fallback", JsonFile.Read(dir.File("none.json"), new Sample { Name = "fallback" }).Name);
    }

    [Fact]
    public void Deleting_the_file_resets_it_even_when_a_backup_exists()
    {
        using var dir = new TempDir();
        var path = dir.File("a.json");
        JsonFile.Write(path, new Sample { Name = "one" });
        JsonFile.Write(path, new Sample { Name = "two" });
        File.Delete(path);

        Assert.Equal("fallback", JsonFile.Read(path, new Sample { Name = "fallback" }).Name);
    }

    [Fact]
    public void The_second_write_keeps_the_first_as_a_backup()
    {
        using var dir = new TempDir();
        var path = dir.File("a.json");
        JsonFile.Write(path, new Sample { Name = "one" });
        JsonFile.Write(path, new Sample { Name = "two" });

        Assert.Equal("two", JsonFile.Read(path, new Sample()).Name);
        Assert.Equal("one", JsonFile.Read(path + JsonFile.BackupSuffix, new Sample()).Name);
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void A_truncated_file_is_set_aside_and_the_backup_is_used()
    {
        using var dir = new TempDir();
        var path = dir.File("a.json");
        JsonFile.Write(path, new Sample { Name = "one" });
        JsonFile.Write(path, new Sample { Name = "two" });
        File.WriteAllText(path, "{ \"name\": \"tw");   // power cut mid-write

        var read = JsonFile.Read(path, new Sample { Name = "fallback" });

        Assert.Equal("one", read.Name);
        Assert.True(File.Exists(path + JsonFile.CorruptSuffix));
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void A_truncated_file_without_a_backup_gives_the_fallback()
    {
        using var dir = new TempDir();
        var path = dir.File("a.json");
        File.WriteAllText(path, "not json at all");

        Assert.Equal("fallback", JsonFile.Read(path, new Sample { Name = "fallback" }).Name);
        Assert.True(File.Exists(path + JsonFile.CorruptSuffix));
    }

    [Fact]
    public void A_corrupt_file_never_overwrites_the_good_backup()
    {
        using var dir = new TempDir();
        var path = dir.File("a.json");
        JsonFile.Write(path, new Sample { Name = "good" });
        JsonFile.Write(path, new Sample { Name = "better" });
        File.WriteAllText(path, "{{{");

        JsonFile.Read(path, new Sample());
        JsonFile.Write(path, new Sample { Name = "fresh" });

        Assert.Equal("good", JsonFile.Read(path + JsonFile.BackupSuffix, new Sample()).Name);
    }

    [Theory]
    [InlineData("{}", true)]
    [InlineData("[1,2,3]", true)]
    [InlineData("{ \"a\": 1, }", true)]
    [InlineData("{ \"a\": ", false)]
    [InlineData("", false)]
    public void Knows_a_complete_document_from_a_cut_off_one(string text, bool valid)
        => Assert.Equal(valid, JsonFile.IsValidJson(text));
}

public class FileLayoutStoreTests
{
    [Fact]
    public void Nothing_saved_gives_an_empty_object()
    {
        using var dir = new TempDir();
        Assert.Equal("{}", new FileLayoutStore(dir.File("layout.json")).Load());
    }

    [Fact]
    public void Round_trips_the_text_untouched()
    {
        using var dir = new TempDir();
        var store = new FileLayoutStore(dir.File("layout.json"));
        store.Save("{\"clock\":{\"x\":10,\"y\":20}}");

        Assert.Equal("{\"clock\":{\"x\":10,\"y\":20}}", store.Load());
    }

    [Fact]
    public void A_cut_off_layout_falls_back_to_the_last_good_one()
    {
        using var dir = new TempDir();
        var path = dir.File("layout.json");
        var store = new FileLayoutStore(path);
        store.Save("{\"v\":1}");
        store.Save("{\"v\":2}");
        File.WriteAllText(path, "{\"v\":");

        Assert.Equal("{\"v\":1}", store.Load());
        Assert.True(File.Exists(path + JsonFile.CorruptSuffix));
    }

    [Fact]
    public void Reset_forgets_the_layout()
    {
        using var dir = new TempDir();
        var store = new FileLayoutStore(dir.File("layout.json"));
        store.Save("{\"v\":1}");
        store.Reset();

        Assert.Equal("{}", store.Load());
    }
}
