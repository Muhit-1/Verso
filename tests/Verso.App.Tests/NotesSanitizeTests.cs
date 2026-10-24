using System.Text.RegularExpressions;
using Verso.App.Hosting;
using Xunit;

namespace Verso.App.Tests;

public class NotesSanitizeTests
{
    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("plain text", "plain text")]
    [InlineData("<b>bold</b> and <i>italic</i>", "<b>bold</b> and <i>italic</i>")]
    [InlineData("<ul><li>one</li><li>two</li></ul>", "<ul><li>one</li><li>two</li></ul>")]
    [InlineData("line<br>break", "line<br>break")]
    [InlineData("line<br/>break", "line<br>break")]
    [InlineData("<B>shouting</B>", "<b>shouting</b>")]
    public void Keeps_the_formatting_tags_it_allows(string? input, string expected)
        => Assert.Equal(expected, NotesLibrary.Sanitize(input));

    [Theory]
    [InlineData("<script>alert(1)</script>after", "after")]
    [InlineData("<SCRIPT type=text/javascript>alert(1)</SCRIPT >after", "after")]
    [InlineData("<style>body{display:none}</style>after", "after")]
    [InlineData("<img src=x onerror=alert(1)>after", "after")]
    [InlineData("<iframe src=\"https://example.com\"></iframe>after", "after")]
    [InlineData("<a href=\"javascript:alert(1)\">link</a>", "link")]
    [InlineData("<svg onload=alert(1)>x</svg>", "x")]
    public void Drops_everything_else_but_keeps_the_text(string input, string expected)
        => Assert.Equal(expected, NotesLibrary.Sanitize(input));

    [Fact]
    public void Strips_attributes_from_allowed_tags()
        => Assert.Equal("<b>hi</b>", NotesLibrary.Sanitize("<b onclick=\"alert(1)\" style=\"x\">hi</b>"));

    [Fact]
    public void A_stray_angle_bracket_becomes_text()
        => Assert.Equal("1 &lt; 2 and 3 &gt; 2", NotesLibrary.Sanitize("1 < 2 and 3 > 2"));

    [Fact]
    public void Caps_the_length()
        => Assert.True(NotesLibrary.Sanitize(new string('a', 100_000)).Length <= 20_000);

    [Theory]
    [InlineData("<b<script>alert(1)</script>")]
    [InlineData("<<script>script>alert(1)<</script>/script>")]
    [InlineData("<img src=\"x\" title=\"><script>alert(1)</script>\">")]
    [InlineData("<scr<script>ipt>alert(1)</scr</script>ipt>")]
    [InlineData("<div onmouseover=alert(1)>hover</div>")]
    [InlineData("<math><mi xlink:href=\"javascript:alert(1)\">x</mi></math>")]
    [InlineData("<p/onclick=alert(1)>x</p>")]
    [InlineData("<b\nonclick=alert(1)>x</b>")]
    public void Nothing_that_survives_can_run_script(string hostile)
    {
        var clean = NotesLibrary.Sanitize(hostile);

        // Every "<" left must start one of the allowed tags, with no attribute after the name.
        var stray = Regex.Match(clean, @"<(?!/?(?:b|i|u|s|strike|ul|ol|li|br|div|p)>)");
        Assert.False(stray.Success, $"unexpected markup in: {clean}");
    }
}
