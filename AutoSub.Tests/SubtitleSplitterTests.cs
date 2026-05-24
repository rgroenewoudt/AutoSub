namespace AutoSub.Tests;

[TestFixture]
public class SubtitleSplitterTests
{
    [Test]
    public void ShortSentence_NoSplit()
    {
        var input = "Hello world.";
        var result = SubtitleSplitter.SmartSplit(input);

        Assert.That(result.Count, Is.EqualTo(1));
        Assert.That(result[0], Is.EqualTo("Hello world."));
    }

    [Test]
    public void LongSentence_SplitsOnPeriod()
    {
        var input = "This is the first sentence. This is the second one.";
        var result = SubtitleSplitter.SmartSplit(input, 30);

        Assert.That(result.Count, Is.EqualTo(2));
        Assert.That(result[0], Is.EqualTo("This is the first sentence."));
        Assert.That(result[1], Is.EqualTo("This is the second one."));
    }

    [Test]
    public void LongSentence_SplitsOnComma()
    {
        var input = "This is a long line, which should be split at the comma.";
        var result = SubtitleSplitter.SmartSplit(input, 30);

        Assert.That(result.Count, Is.EqualTo(3));
        Assert.That(result[0], Is.EqualTo("This is a long line,"));
        Assert.That(result[1], Is.EqualTo("which should be split at the"));
        Assert.That(result[2], Is.EqualTo("comma."));
    }

    [Test]
    public void NoDelimiter_FallsBackToWordWrap()
    {
        var input = "This is a very long sentence without natural breaks";
        var result = SubtitleSplitter.SmartSplit(input, 25);

        Assert.That(result.Count, Is.EqualTo(3));
        Assert.That(result[0], Is.EqualTo("This is a very long"));
        Assert.That(result[1], Is.EqualTo("sentence without natural"));
        Assert.That(result[2], Is.EqualTo("breaks"));
    }

    [Test]
    public void EmptyInput_ReturnsEmptyList()
    {
        var result = SubtitleSplitter.SmartSplit("", 40);

        Assert.That(result, Is.Empty);
    }

    [Test]
    public void OneLongWord_NoBreaks()
    {
        var input = "Supercalifragilisticexpialidocious";
        var result = SubtitleSplitter.SmartSplit(input, 10);

        Assert.That(result.Count, Is.EqualTo(1));
        Assert.That(result[0], Is.EqualTo(input));
    }
}