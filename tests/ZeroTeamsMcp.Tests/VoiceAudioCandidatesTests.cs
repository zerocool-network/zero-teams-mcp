using Microsoft.Graph.Models;
using ZeroTeamsMcp.Server.Tools;

public class VoiceAudioCandidatesTests
{
    [Fact]
    public void Detects_audio_attachment_url_and_card_json_url()
    {
        var atts = new[]
        {
            new ChatMessageAttachment { ContentType = "reference", ContentUrl = "https://x.sharepoint.com/doc.pdf" },
            new ChatMessageAttachment { ContentType = "audio/mp4", ContentUrl = "https://graph.microsoft.com/v1.0/a/$value" },
            new ChatMessageAttachment { ContentType = "application/vnd.microsoft.card.audio",
                Content = """{"media":[{"url":"https://graph.microsoft.com/v1.0/b/$value"}]}""" }
        };

        var c = TeamsVoiceTools.FindAudioCandidates(atts, null, null);

        Assert.Equal(2, c.Count);
        Assert.Contains(c, x => x.Url!.EndsWith("/a/$value"));
        Assert.Contains(c, x => x.Url!.EndsWith("/b/$value"));
    }

    [Fact]
    public void Real_teams_voice_card_shape()
    {
        var card = new ChatMessageAttachment
        {
            Id = "bfb101ece45946be988be560a74322f3",
            ContentType = "application/vnd.microsoft.card.audio",
            Content = "{\r\n  \"duration\": \"PT2M5S\",\r\n  \"media\": [{ \"url\": \"https://graph.microsoft.com/v1.0/chats/c/messages/m/hostedContents/h/$value\" }]\r\n}"
        };
        var hosted = new[] { new ChatMessageHostedContent { Id = "h", ContentType = null } };

        var c = TeamsVoiceTools.FindAudioCandidates([card], hosted, """<div><attachment id="bfb101ece45946be988be560a74322f3"></attachment></div>""");

        Assert.Single(c);
        Assert.Equal("attachment.content", c[0].Source);
        Assert.EndsWith("/hostedContents/h/$value", c[0].Url);
        Assert.Equal(125, TeamsVoiceTools.CardDurationSeconds([card]));
    }

    [Fact]
    public void Bare_attachment_body_falls_back_to_non_image_hosted_contents()
    {
        var hosted = new[]
        {
            new ChatMessageHostedContent { Id = "img", ContentType = "image/png" },
            new ChatMessageHostedContent { Id = "h1", ContentType = null }
        };

        var c = TeamsVoiceTools.FindAudioCandidates([], hosted, """<attachment id="abc"></attachment>""");

        Assert.Single(c);
        Assert.Equal("h1", c[0].HostedContentId);
    }

    [Fact]
    public void Image_message_yields_no_candidates()
    {
        var hosted = new[] { new ChatMessageHostedContent { Id = "img", ContentType = "image/png" } };
        Assert.Empty(TeamsVoiceTools.FindAudioCandidates([], hosted, """<p><img src="x"></p>"""));
    }
}

public class ScribeParseTests
{
    [Fact]
    public void Parses_text_duration_and_confidence()
    {
        var json = """
        {"language_code":"spa","language_probability":0.98,"text":" hola mundo ",
         "words":[{"text":"hola","start":0.1,"end":0.5,"type":"word","logprob":0},
                  {"text":" ","start":0.5,"end":0.6,"type":"spacing"},
                  {"text":"mundo","start":0.6,"end":1.2,"type":"word","logprob":0}]}
        """;
        var r = TeamsVoiceTools.ParseScribe("m1", json);
        Assert.Equal("hola mundo", r.Transcript);
        Assert.Equal(1.2, r.DurationSeconds);
        Assert.Equal(1.0, r.Confidence);
    }

    [Fact]
    public void Missing_words_gives_nulls()
    {
        var r = TeamsVoiceTools.ParseScribe("m1", """{"text":"x"}""");
        Assert.Null(r.DurationSeconds);
        Assert.Null(r.Confidence);
    }
}
