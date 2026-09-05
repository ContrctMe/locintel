using System.Buffers.Binary;
using LocIntel.Integrations.ClamAV;
using LocIntel.Platform.Storage;

namespace LocIntel.IntegrationTests;

/// <summary>
/// The clamd INSTREAM wire format as pure logic (no daemon): framing and the
/// verdict parse. Here rather than the unit project because unit projects
/// may not reference integrations (UnitTestPurityTests); no fixture needed.
/// ClamAvScannerTests separately exercises the socket and quarantine pipeline
/// against a real clamd container in the integration suite.
/// </summary>
public class ClamAvProtocolTests
{
    [Fact]
    public void A_chunk_is_prefixed_with_its_big_endian_length()
    {
        var framed = ClamAvProtocol.Frame([1, 2, 3]);

        Assert.Equal(3u, BinaryPrimitives.ReadUInt32BigEndian(framed));
        Assert.Equal([1, 2, 3], framed[4..]);
        Assert.Equal(new byte[4], ClamAvProtocol.End.ToArray()); // zero length ends the stream
        Assert.Equal("zINSTREAM\0"u8.ToArray(), ClamAvProtocol.Command.ToArray());
    }

    [Theory]
    [InlineData("stream: OK\0", ScanVerdict.Clean)]
    [InlineData("stream: Eicar-Test-Signature FOUND\0", ScanVerdict.Infected)]
    [InlineData("stream: Win.Test.EICAR_HDB-1 FOUND\n", ScanVerdict.Infected)]
    public void A_clear_reply_is_a_verdict(string reply, ScanVerdict expected)
    {
        Assert.Equal(expected, ClamAvProtocol.ParseVerdict(reply));
    }

    [Theory]
    [InlineData("INSTREAM size limit exceeded. ERROR\0")]
    [InlineData("")]
    [InlineData("stream: OKAY\0")] // not the token; a lookalike must not read as clean
    public void Anything_else_is_an_error_never_clean(string reply)
    {
        // a scanner that cannot answer keeps the object quarantined
        Assert.Throws<InvalidOperationException>(() => ClamAvProtocol.ParseVerdict(reply));
    }
}
