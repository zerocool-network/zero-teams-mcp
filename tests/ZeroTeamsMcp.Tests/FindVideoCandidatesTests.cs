using Microsoft.Graph.Models;
using ZeroTeamsMcp.Server.Tools;
using Xunit;

namespace ZeroTeamsMcp.Tests;

public class FindVideoCandidatesTests
{
    // Shape REAL verificado 2026-10-06 (tenant Optima, chat 19:9b04bb1…@thread.v2, msgs 1791278477365 /
    // 1791278337771, enviados por Fran Ullod): attachments vacío, vídeo solo en body.content como
    // <video src="…/hostedContents/<id>/$value" data-duration="PT1M0S" style="width:540px; height:960px">.
    public const string RealBodyVideo =
        "<div><video src=\"https://graph.microsoft.com/v1.0/chats/19:9b04bb1002bb4a83918cbae9d7cebb9c@thread.v2/messages/1791278477365/hostedContents/aWQ9LHR5cGU9Mix1cmw9aHR0cHM6Ly9ldS1wcm9kLmFzeW5jZ3cudGVhbXMubWljcm9zb2Z0LmNvbS92MS9vYmplY3RzLzAtZnJjYS1kNi0xZGYxYzkwOWZlNWZlODRhNmFhNWI1ZDEyN2U4YTk0Ni92aWV3cy92aWRlbw==/$value\" data-duration=\"PT1M0S\" style=\"width:540px; height:960px\">\n</video></div>";

    [Fact]
    public void ShapeReal_BodyVideo_EncuentraSrc()
    {
        var candidates = TeamsVideoTools.FindVideoCandidates([], null, RealBodyVideo);

        var c = Assert.Single(candidates);
        Assert.Equal("body<video src>", c.Source);
        Assert.Contains("/hostedContents/", c.Url);
        Assert.EndsWith("/$value", c.Url);
        Assert.Null(c.HostedContentId);
    }

    [Fact]
    public void BodyVideo_SinSrc_NoCandidato()
    {
        var candidates = TeamsVideoTools.FindVideoCandidates([], null, "<div><video data-duration=\"PT5S\"></video></div>");
        Assert.Empty(candidates);
    }

    [Fact]
    public void BodyImagen_NoDevuelveVideos()
    {
        var candidates = TeamsVideoTools.FindVideoCandidates([], null, FindImageCandidatesTests.RealBody);
        Assert.Empty(candidates);
    }

    [Fact]
    public void BodyConImgYVideo_SoloDevuelveElVideo()
    {
        var html = "<div><img src=\"https://graph.microsoft.com/v1.0/x/messages/1/hostedContents/imgid/$value\">" +
                   RealBodyVideo + "</div>";

        var c = Assert.Single(TeamsVideoTools.FindVideoCandidates([], null, html));
        Assert.Equal("body<video src>", c.Source);
    }

    [Fact]
    public void DosVideoTags_DosCandidatos_SinDuplicados()
    {
        var second = RealBodyVideo
            .Replace("1791278477365", "1791278337771")
            .Replace("aWQ9LHR5cGU9Mix", "aWQ9LHR5cGU9OTk");
        var html = RealBodyVideo + second + RealBodyVideo; // tercero repetido

        var candidates = TeamsVideoTools.FindVideoCandidates([], null, html);
        Assert.Equal(2, candidates.Count);
    }

    [Fact]
    public void AttachmentVideoMp4_ConContentUrl_EsCandidato()
    {
        var attachments = new[]
        {
            new ChatMessageAttachment { ContentType = "video/mp4", ContentUrl = "https://eu-api.asm.skype.com/v1/objects/0-x/views/videoUrl", Name = "grabacion.mp4" },
            new ChatMessageAttachment { ContentType = "text/plain", ContentUrl = "https://ejemplo.com/nota.txt", Name = "nota.txt" }
        };

        var c = Assert.Single(TeamsVideoTools.FindVideoCandidates(attachments, null, "<p>mirad</p>"));
        Assert.Equal("attachment.contentUrl", c.Source);
        Assert.Equal("video/mp4", c.ContentType);
    }

    [Fact]
    public void AttachmentCardVideo_ExtraeUrlDelJsonContent()
    {
        var attachments = new[]
        {
            new ChatMessageAttachment
            {
                ContentType = "application/vnd.microsoft.card.video",
                Content = "{\"sources\":[{\"src\":\"https://example.sharepoint.com/sites/x/video.mp4\"}]}"
            }
        };

        var c = Assert.Single(TeamsVideoTools.FindVideoCandidates(attachments, null, null));
        Assert.Equal("attachment.content", c.Source);
        Assert.Equal("https://example.sharepoint.com/sites/x/video.mp4", c.Url);
    }

    [Fact]
    public void AttachmentNombreMp4_SinContentType_TambienEsCandidato()
    {
        var attachments = new[]
        {
            new ChatMessageAttachment { ContentType = null, ContentUrl = "https://eu-api.asm.skype.com/v1/objects/0-y/views/vid", Name = "clip.mov" }
        };

        var c = Assert.Single(TeamsVideoTools.FindVideoCandidates(attachments, null, "<p>hola</p>"));
        Assert.Equal("attachment.contentUrl", c.Source);
    }

    [Fact]
    public void HostedContentsVideoExplicito_EsCandidato()
    {
        var hosted = new[]
        {
            new ChatMessageHostedContent { Id = "vid1", ContentType = "video/mp4" },
            new ChatMessageHostedContent { Id = "img1", ContentType = "image/png" }
        };

        var c = Assert.Single(TeamsVideoTools.FindVideoCandidates([], hosted, "<p>sin cuerpo</p>"));
        Assert.Equal("hostedContent", c.Source);
        Assert.Equal("vid1", c.HostedContentId);
    }

    [Fact]
    public void VideoSourcesInHtml_ExtraeSrcDeMultiplesTags()
    {
        var html = "<video src=\"https://a.example/v.mp4\" data-duration=\"PT10S\"></video>" +
                   "<div>texto</div>" +
                   "<video data-duration=\"PT5S\"><source src=\"https://b.example/v.mp4\"></video>";

        var sources = TeamsVideoTools.VideoSourcesInHtml(html).ToList();

        // src directo del tag <video>; el <source> interno no es <video src> y se ignora a propósito
        // (Teams nunca lo usa: su shape real es siempre <video src>).
        Assert.Equal("https://a.example/v.mp4", Assert.Single(sources));
    }

    [Fact]
    public void VideoSourcesInHtml_SinVideo_Vacio()
    {
        Assert.Empty(TeamsVideoTools.VideoSourcesInHtml(FindImageCandidatesTests.RealBody));
        Assert.Empty(TeamsVideoTools.VideoSourcesInHtml(null));
        Assert.Empty(TeamsVideoTools.VideoSourcesInHtml(""));
    }
}

public class VideoProbeTests
{
    // Salida real de ffprobe -print_format json -show_format -show_streams sobre el mp4 de Fran
    // (60.02s, 540x960, h264 + aac).
    public const string RealProbeJson =
        """
        {
          "streams": [
            { "codec_name": "h264", "codec_type": "video", "width": 540, "height": 960 },
            { "codec_name": "aac", "codec_type": "audio" }
          ],
          "format": { "duration": "60.022993", "size": "9562621" }
        }
        """;

    [Fact]
    public void ParseProbeJson_ShapeReal_DevuelveMeta()
    {
        var probe = TeamsVideoTools.ParseProbeJson(RealProbeJson);

        Assert.NotNull(probe);
        Assert.Equal(60.022993, probe!.DurationSeconds!.Value, 3);
        Assert.Equal(540, probe.Width);
        Assert.Equal(960, probe.Height);
        Assert.True(probe.HasAudio);
    }

    [Fact]
    public void ParseProbeJson_SinAudio_False()
    {
        var json = """{"streams":[{"codec_type":"video","width":100,"height":50}],"format":{"duration":"3.5"}}""";
        var probe = TeamsVideoTools.ParseProbeJson(json);

        Assert.NotNull(probe);
        Assert.False(probe!.HasAudio);
        Assert.Equal(3.5, probe.DurationSeconds!.Value, 3);
    }

    [Fact]
    public void ParseProbeJson_Basura_Null()
    {
        Assert.Null(TeamsVideoTools.ParseProbeJson("no es json"));
        Assert.Null(TeamsVideoTools.ParseProbeJson("{}"));
    }

    [Fact]
    public void ParseIsoDuration_FormatosReales()
    {
        Assert.Equal(60, TeamsVideoTools.ParseIsoDuration("PT1M0S"));
        Assert.Equal(26, TeamsVideoTools.ParseIsoDuration("PT26S"));
        Assert.Equal(65, TeamsVideoTools.ParseIsoDuration("PT1M5S"));
        Assert.Equal(3661, TeamsVideoTools.ParseIsoDuration("PT1H1M1S"));
        Assert.Null(TeamsVideoTools.ParseIsoDuration(null));
        Assert.Null(TeamsVideoTools.ParseIsoDuration(""));
        Assert.Null(TeamsVideoTools.ParseIsoDuration("PT"));
    }

    [Fact]
    public void SniffVideoMime_Mp4YWebm()
    {
        // ftyp|isom  → mp4
        var mp4 = new byte[] { 0x00, 0x00, 0x00, 0x20, 0x66, 0x74, 0x79, 0x70, 0x69, 0x73, 0x6F, 0x6D };
        Assert.Equal("video/mp4", TeamsVideoTools.SniffVideoMime(mp4));

        // EBML (matroska/webm)
        var webm = new byte[] { 0x1A, 0x45, 0xDF, 0xA3, 0x01, 0x00, 0x00, 0x00 };
        Assert.Equal("video/webm", TeamsVideoTools.SniffVideoMime(webm));

        // Desconocido → video/mp4 (Teams siempre manda mp4; el mime solo se usa en el texto de metadatos).
        Assert.Equal("video/mp4", TeamsVideoTools.SniffVideoMime(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }));
    }
}

public class FfmpegArgsTests
{
    [Fact]
    public void SceneFrameArgs_LlevaSelectSceneYCapped()
    {
        var args = TeamsVideoTools.SceneFrameArgs("/tmp/in.mp4", "/tmp/out/f_%d.jpg", 6);

        var joined = string.Join(' ', args);
        Assert.Equal("ffmpeg", args[0]);
        Assert.Contains("-i", args);
        Assert.Contains("/tmp/in.mp4", args);
        Assert.Contains("select=gt(scene\\,0.3)", joined);
        Assert.Contains("scale=min(1280\\,iw):-2", joined);
        Assert.Contains("format=yuvj420p", joined);
        Assert.Contains("-frames:v", args);
        Assert.Contains("6", args);
        Assert.Contains("/tmp/out/f_%d.jpg", args);
        Assert.DoesNotContain("-vn", args);
    }

    [Fact]
    public void UniformFrameArgs_MuestreoEquidistante()
    {
        var args = TeamsVideoTools.UniformFrameArgs("/tmp/in.mp4", "/tmp/out/u_%d.jpg", 10, 6);

        var joined = string.Join(' ', args);
        Assert.Equal("ffmpeg", args[0]);
        Assert.Contains("fps=1/10", joined);
        Assert.Contains("-frames:v", args);
        Assert.Contains("6", args);
    }

    [Fact]
    public void UniformFrameArgs_IntervaloInvalido_UsaDiezSegundos()
    {
        var args = TeamsVideoTools.UniformFrameArgs("/tmp/in.mp4", "/tmp/out/u_%d.jpg", 0, 6);
        Assert.Contains("fps=1/10", string.Join(' ', args));
    }

    [Fact]
    public void AudioExtractArgs_SinVideoConMapDeAudio()
    {
        var args = TeamsVideoTools.AudioExtractArgs("/tmp/in.mp4", "/tmp/out/a.m4a");

        var joined = string.Join(' ', args);
        Assert.Equal("ffmpeg", args[0]);
        Assert.Contains("-map", args);
        Assert.Contains("0:a:0", args);
        Assert.Contains("-vn", args);
        Assert.Contains("-c:a", args);
        Assert.Contains("aac", args);
        Assert.Contains("96k", args);
        Assert.Contains("/tmp/out/a.m4a", args);
    }

    [Fact]
    public void FfprobeArgs_MuestraFormatYStreams()
    {
        var args = TeamsVideoTools.FfprobeArgs("/tmp/in.mp4");
        var joined = string.Join(' ', args);
        Assert.Equal("ffprobe", args[0]);
        Assert.Contains("-print_format", args);
        Assert.Contains("json", args);
        Assert.Contains("-show_format", args);
        Assert.Contains("-show_streams", args);
        Assert.Contains("/tmp/in.mp4", args);
    }
}
