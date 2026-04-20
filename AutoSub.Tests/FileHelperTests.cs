namespace AutoSub.Tests;

[TestFixture]
public class FileHelperTests
{
    [TestCase("dvd_subtitle")]
    [TestCase("dvdsub")]
    [TestCase("hdmv_pgs_subtitle")]
    [TestCase("pgssub")]
    [TestCase("xsub")]
    [TestCase("DVD_SUBTITLE")]   // case-insensitive
    [TestCase("HDMV_PGS_SUBTITLE")]
    public void IsImageBasedSubtitleCodec_ImageCodecs_ReturnsTrue(string codecName)
    {
        Assert.That(FileHelper.IsImageBasedSubtitleCodec(codecName), Is.True);
    }

    [TestCase("subrip")]
    [TestCase("ass")]
    [TestCase("ssa")]
    [TestCase("webvtt")]
    [TestCase("mov_text")]
    [TestCase("srt")]
    [TestCase("text")]
    public void IsImageBasedSubtitleCodec_TextCodecs_ReturnsFalse(string codecName)
    {
        Assert.That(FileHelper.IsImageBasedSubtitleCodec(codecName), Is.False);
    }

    [Test]
    public void IsImageBasedSubtitleCodec_NullCodec_ReturnsFalse()
    {
        Assert.That(FileHelper.IsImageBasedSubtitleCodec(null), Is.False);
    }
}
