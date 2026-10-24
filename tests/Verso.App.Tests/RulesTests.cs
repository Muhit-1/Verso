using Verso.App.Hosting;
using Xunit;

namespace Verso.App.Tests;

public class LaunchTargetTests
{
    [Theory]
    [InlineData(@"C:\Program Files\App\app.exe")]
    [InlineData(@"D:\Games\game.lnk")]
    [InlineData(@"\\server\share\tool.exe")]
    [InlineData(@"shell:AppsFolder\Microsoft.WindowsCalculator_8wekyb3d8bbwe!App")]
    [InlineData(@"SHELL:APPSFOLDER\Some.App")]
    public void Allows_program_paths_and_store_apps(string target)
        => Assert.True(LauncherLibrary.IsAllowedTarget(target));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("calc.exe")]
    [InlineData(@"..\..\tool.exe")]
    [InlineData("https://example.com/run")]
    [InlineData("ms-settings:taskbar")]
    [InlineData("shell:startup")]
    [InlineData(@"shell:::{20D04FE0-3AEA-1069-A2D8-08002B30309D}")]
    [InlineData("file:///C:/Windows/notepad.exe")]
    public void Refuses_everything_else(string? target)
        => Assert.False(LauncherLibrary.IsAllowedTarget(target));
}

public class WebViewGuardTests
{
    [Theory]
    [InlineData("https://verso.example/outpost/index.html?wa=0,0,1920,1040&mo=0,0")]
    [InlineData("https://VERSO.example/dashboard/index.html")]
    public void The_top_frame_may_show_verso_pages(string uri)
        => Assert.True(WebViewGuard.IsAllowedTopLevel(uri));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("https://example.com/")]
    [InlineData("https://verso.example.evil.com/")]
    [InlineData("https://evil.com/verso.example")]
    [InlineData("http://verso.example/")]
    [InlineData("https://verso.bg.example/wallpaper.html")]      // a wallpaper is a frame, never the whole window
    [InlineData("https://verso.user-widgets.example/x/index.html")]
    [InlineData("file:///C:/Windows/win.ini")]
    [InlineData("about:blank")]
    [InlineData("javascript:alert(1)")]
    public void The_top_frame_may_show_nothing_else(string? uri)
        => Assert.False(WebViewGuard.IsAllowedTopLevel(uri));

    [Theory]
    [InlineData("https://verso.example/widgets/clock/index.html")]
    [InlineData("https://verso.bg.example/wallpaper/index.html")]
    [InlineData("https://verso.user-widgets.example/mine/index.html")]
    [InlineData("about:blank")]
    [InlineData("about:srcdoc")]
    public void Frames_may_load_verso_hosts(string uri)
        => Assert.True(WebViewGuard.IsAllowedFrame(uri));

    [Theory]
    [InlineData(null)]
    [InlineData("https://example.com/")]
    [InlineData("https://verso.example.evil.com/")]
    [InlineData("http://verso.example/")]
    [InlineData("file:///C:/Windows/win.ini")]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,<script>alert(1)</script>")]
    public void Frames_may_not_reach_the_internet_or_the_disk(string? uri)
        => Assert.False(WebViewGuard.IsAllowedFrame(uri));
}

public class LogTests
{
    [Fact]
    public void Formats_a_line_with_time_level_and_message()
    {
        var line = Log.Format(new DateTime(2026, 10, 24, 9, 5, 7, 42), "WARN ", "something odd", null);
        Assert.Equal("2026-10-24 09:05:07.042 WARN  something odd", line);
    }

    [Fact]
    public void Includes_the_exception_and_its_inner_exceptions()
    {
        var ex = new InvalidOperationException("outer", new IOException("inner"));
        var line = Log.Format(DateTime.Now, "ERROR", "failed", ex);

        Assert.Contains("InvalidOperationException: outer", line);
        Assert.Contains("<- IOException: inner", line);
    }

    [Fact]
    public void Writing_never_throws()
    {
        Log.Info("test entry");
        Log.Warn("test warning", new Exception("boom"));
        Log.Error("test error");
    }
}
