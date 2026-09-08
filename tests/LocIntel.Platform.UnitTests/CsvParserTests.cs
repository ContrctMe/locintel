using LocIntel.Platform.Text;

namespace LocIntel.Platform.UnitTests;

public class CsvParserTests
{
    private static readonly CsvLimits Limits = new(1024, 2, 2, 20);

    [Fact]
    public void Quoted_fields_escaped_quotes_and_embedded_newlines_survive()
    {
        var rows = CsvParser.Parse(
            " Name ,note\r\n\"a,b\",\"first\r\nsecond\"\r\nc,\"say \"\"hi\"\"\"",
            Limits
        );
        Assert.Equal("a,b", rows[0]["name"]);
        Assert.Equal("first\r\nsecond", rows[0]["note"]);
        Assert.Equal("say \"hi\"", rows[1]["note"]);
    }

    [Theory]
    [InlineData("name,NAME\na,b")]
    [InlineData("name,\na,b")]
    [InlineData("name\n\"unfinished")]
    [InlineData("a,b,c\n1,2,3")]
    [InlineData("name\na\nb\nc")]
    [InlineData("name\na\nb\nc\n")]
    public void Invalid_structure_or_limits_are_rejected(string csv) =>
        Assert.Throws<InvalidDataException>(() => CsvParser.Parse(csv, Limits));

    [Fact]
    public void Field_and_utf8_byte_limits_are_enforced_at_eof_and_before_newline()
    {
        foreach (var ending in new[] { "", "\n" })
        {
            Assert.Single(CsvParser.Parse("name\n" + new string('x', 20) + ending, Limits));
            Assert.Throws<InvalidDataException>(() =>
                CsvParser.Parse("name\n" + new string('x', 21) + ending, Limits)
            );
        }
        Assert.Throws<InvalidDataException>(() => CsvParser.Parse("n\néé", new(5, 2, 2, 20)));
    }
}
