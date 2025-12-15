using FFMpegCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AutoSub.Tests;

internal class ManualTests
{
    [Explicit]
    [Test]
    public async Task Test1()
    {
        var file =
            @"G:\TV series\Stargate SG-1\Stargate.SG-1.S08.1080p.BluRay.x264-BORDURE[rartv]\Stargate.SG-1.S08E20.1080p.BluRay.x264-BORDURE.mkv";
        var result = FileHelper.HasSubtitleFile(file);
        var mediaInfo = await FFProbe.AnalyseAsync(file, cancellationToken: CancellationToken.None);
        Assert.That(result, Is.EqualTo(true));
    }
}