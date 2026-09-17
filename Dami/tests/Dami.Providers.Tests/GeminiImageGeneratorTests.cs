using System.Net;
using System.Text;
using Dami.Contracts.Context;
using Dami.Contracts.Events;
using Dami.Contracts.Models;
using Dami.Contracts.Privacy;
using Dami.Privacy;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Dami.Providers.Tests;

public sealed class GeminiImageGeneratorTests
{
    private static readonly string onePixel = Convert.ToBase64String(
        Encoding.UTF8.GetBytes("pretend-png-bytes"));

    private sealed class Canned : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        public string Body { get; set; } = string.Empty;

        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;

        /// <summary>What the caller actually put on the wire.</summary>
        public string Sent { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            this.Request = request;
            if (request.Content is not null)
            {
                this.Sent = await request.Content.ReadAsStringAsync(cancellationToken);
            }

            return new HttpResponseMessage(this.Status)
            {
                Content = new StringContent(this.Body),
            };
        }
    }

    private static ImageRequest Request(PrivacyClass privacy = PrivacyClass.Egressable) =>
        new("a portrait", "daily portrait (evening)", privacy, Guid.NewGuid(),
            ExecutionOrigin.ScheduledService);

    private static (GeminiImageGenerator Generator, Canned Handler, IExecutionEventStore Events)
        Create(string? apiKey = "AIza-test", string allowedHost = "generativelanguage.googleapis.com",
               string? budgetRefusal = null, string? body = null)
    {
        var handler = new Canned
        {
            Body = body ?? $$$"""
                {"candidates":[{"content":{"parts":[
                  {"text":"here you go"},
                  {"inlineData":{"mimeType":"image/png","data":"{{{onePixel}}}"}}
                ]},"finishReason":"STOP"}]}
                """,
        };
        var egress = new EgressOptions();
        if (allowedHost.Length > 0)
        {
            egress.AllowedHosts.Add(allowedHost);
        }

        var events = Substitute.For<IExecutionEventStore>();
        var budget = Substitute.For<IEgressBudget>();
        budget.FindRefusalAsync(Arg.Any<CancellationToken>()).Returns(budgetRefusal);
        return (
            new GeminiImageGenerator(
                new HttpClient(handler),
                Options.Create(new GeminiImageOptions { ApiKey = apiKey ?? string.Empty }),
                Options.Create(egress),
                budget,
                events,
                TimeProvider.System,
                NullLogger<GeminiImageGenerator>.Instance),
            handler,
            events);
    }

    [Fact]
    public async Task Should_Return_The_Decoded_Image()
    {
        var (generator, _, _) = Create();

        var image = await generator.GenerateAsync(Request(), CancellationToken.None);

        Assert.Equal("pretend-png-bytes", Encoding.UTF8.GetString(image.Bytes.ToArray()));
    }

    [Fact]
    public async Task Should_Report_The_Content_Type_The_Provider_Returned()
    {
        // Gemini chooses the encoding; a JPEG labelled image/png would fail to open.
        var (generator, _, _) = Create(body: $$$"""
            {"candidates":[{"content":{"parts":[
              {"inlineData":{"mimeType":"image/jpeg","data":"{{{onePixel}}}"}}]}}]}
            """);

        var image = await generator.GenerateAsync(Request(), CancellationToken.None);

        Assert.Equal("image/jpeg", image.ContentType);
    }

    [Fact]
    public async Task Should_Name_The_File_By_Its_Returned_Encoding()
    {
        var (generator, _, _) = Create(body: $$$"""
            {"candidates":[{"content":{"parts":[
              {"inlineData":{"mimeType":"image/jpeg","data":"{{{onePixel}}}"}}]}}]}
            """);

        var image = await generator.GenerateAsync(Request(), CancellationToken.None);

        Assert.EndsWith(".jpg", image.FileName, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Should_Refuse_A_Prompt_That_Is_Not_Egressable()
    {
        // The caller should make this unreachable; the boundary enforces it anyway.
        var (generator, _, _) = Create();

        await Assert.ThrowsAsync<EgressRefusedException>(
            () => generator.GenerateAsync(Request(PrivacyClass.LocalOnly), CancellationToken.None));
    }

    [Fact]
    public async Task Should_Refuse_A_Host_That_Is_Not_Allowlisted()
    {
        // Being configured does not exempt a provider from the allowlist.
        var (generator, _, _) = Create(allowedHost: string.Empty);

        await Assert.ThrowsAsync<EgressRefusedException>(
            () => generator.GenerateAsync(Request(), CancellationToken.None));
    }

    [Fact]
    public async Task Should_Refuse_When_No_Key_Is_Configured()
    {
        // Absent capability, not an error to retry.
        var (generator, _, _) = Create(apiKey: null);

        await Assert.ThrowsAsync<EgressRefusedException>(
            () => generator.GenerateAsync(Request(), CancellationToken.None));
    }

    [Fact]
    public async Task Should_Refuse_When_The_Egress_Budget_Is_Spent()
    {
        var (generator, _, _) = Create(budgetRefusal: "egress budget exhausted");

        await Assert.ThrowsAsync<EgressRefusedException>(
            () => generator.GenerateAsync(Request(), CancellationToken.None));
    }

    [Fact]
    public async Task Should_Not_Reach_The_Network_When_Refused()
    {
        var (generator, handler, _) = Create(allowedHost: string.Empty);

        await Assert.ThrowsAsync<EgressRefusedException>(
            () => generator.GenerateAsync(Request(), CancellationToken.None));

        Assert.Null(handler.Request);
    }

    [Fact]
    public async Task Should_Not_Spend_Money_When_The_Budget_Refuses()
    {
        var (generator, handler, _) = Create(budgetRefusal: "egress budget exhausted");

        await Assert.ThrowsAsync<EgressRefusedException>(
            () => generator.GenerateAsync(Request(), CancellationToken.None));

        Assert.Null(handler.Request);
    }

    [Fact]
    public async Task Should_Record_A_Refusal_In_The_Event_Stream()
    {
        var (generator, _, events) = Create(allowedHost: string.Empty);

        await Assert.ThrowsAsync<EgressRefusedException>(
            () => generator.GenerateAsync(Request(), CancellationToken.None));

        await events.Received().AppendAsync(
            Arg.Is<ExecutionEvent>(e => e.Type == ExecutionEventType.EgressRefused),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_Record_A_Completed_Egress_On_Success()
    {
        var (generator, _, events) = Create();

        await generator.GenerateAsync(Request(), CancellationToken.None);

        await events.Received().AppendAsync(
            Arg.Is<ExecutionEvent>(e => e.Type == ExecutionEventType.EgressCompleted),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_Record_EgressFailed_When_The_Provider_Errors()
    {
        // The prompt is already on the wire by then; a dangling EgressRequested with no
        // outcome is indistinguishable from a call that never happened.
        var (generator, handler, events) = Create();
        handler.Status = HttpStatusCode.InternalServerError;

        await Assert.ThrowsAsync<HttpRequestException>(
            () => generator.GenerateAsync(Request(), CancellationToken.None));

        await events.Received().AppendAsync(
            Arg.Is<ExecutionEvent>(e => e.Type == ExecutionEventType.EgressFailed),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_Never_Put_The_Prompt_Into_An_Event_Label()
    {
        // Purpose lines, never prompt text — the same rule every other door follows.
        var (generator, _, events) = Create();

        await generator.GenerateAsync(Request(), CancellationToken.None);

        await events.DidNotReceive().AppendAsync(
            Arg.Is<ExecutionEvent>(e => e.Label.Contains("a portrait", StringComparison.Ordinal)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_Send_The_Key_In_The_Google_Header_Not_The_Url()
    {
        // The query-string form Google also accepts would put the key in every access log.
        var (generator, handler, _) = Create();

        await generator.GenerateAsync(Request(), CancellationToken.None);

        Assert.Equal("AIza-test", handler.Request!.Headers.GetValues("x-goog-api-key").Single());
        Assert.DoesNotContain("AIza-test", handler.Request.RequestUri!.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Should_Post_To_The_Configured_Models_GenerateContent_Route()
    {
        var (generator, handler, _) = Create();

        await generator.GenerateAsync(Request(), CancellationToken.None);

        Assert.Equal(
            "/v1beta/models/" + new GeminiImageOptions().Model + ":generateContent",
            handler.Request!.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task Should_Send_The_Prompt_As_The_Text_Part()
    {
        var (generator, handler, _) = Create();

        await generator.GenerateAsync(Request(), CancellationToken.None);

        Assert.Contains("\"text\":\"a portrait\"", handler.Sent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Should_Ask_For_An_Image_Only()
    {
        // Without this Gemini answers in prose and the call is paid for nothing.
        var (generator, handler, _) = Create();

        await generator.GenerateAsync(Request(), CancellationToken.None);

        Assert.Contains("\"responseModalities\":[\"IMAGE\"]", handler.Sent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Should_Translate_The_Requested_Size_Into_An_Aspect_Ratio()
    {
        // Gemini has no WxH vocabulary; the portrait shape must survive the translation.
        var (generator, handler, _) = Create();

        await generator.GenerateAsync(Request() with { Size = "1024x1536" }, CancellationToken.None);

        Assert.Contains("\"aspectRatio\":\"2:3\"", handler.Sent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Should_Omit_The_Aspect_Ratio_When_The_Size_Is_Not_A_Known_Shape()
    {
        // Let the model pick rather than send a value the API will reject.
        var (generator, handler, _) = Create();

        await generator.GenerateAsync(Request() with { Size = "auto" }, CancellationToken.None);

        Assert.DoesNotContain("aspectRatio", handler.Sent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Should_Send_One_Reference_As_An_Inline_Image_Part()
    {
        var (generator, handler, _) = Create();
        var request = Request() with
        {
            Reference = new ImageReference(
                "dami-canonical.png", "image/png", Encoding.UTF8.GetBytes("identity")),
        };

        await generator.GenerateAsync(request, CancellationToken.None);

        Assert.Contains(
            "\"inlineData\":{\"mimeType\":\"image/png\",\"data\":\""
                + Convert.ToBase64String(Encoding.UTF8.GetBytes("identity")) + "\"}",
            handler.Sent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Should_Tell_The_Model_An_Edit_Reference_Is_The_Picture_To_Change()
    {
        // The same rule the subscription door states in its prompt: an edit keeps
        // everything but the described change; an anchor keeps only the identity.
        var (generator, handler, _) = Create();
        var request = Request() with
        {
            Reference = new ImageReference("scene.png", "image/png", Encoding.UTF8.GetBytes("scene")),
            EditReference = true,
        };

        await generator.GenerateAsync(request, CancellationToken.None);

        Assert.Contains("keep everything else", handler.Sent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Should_Not_Send_Any_Image_Part_Without_A_Reference()
    {
        var (generator, handler, _) = Create();

        await generator.GenerateAsync(Request(), CancellationToken.None);

        Assert.DoesNotContain("inlineData", handler.Sent, StringComparison.Ordinal);
    }

    [Fact]
    public void Read_Should_Fail_Loudly_When_The_Provider_Returns_No_Image()
    {
        // A 200 with prose and no image part is the shape a safety refusal takes.
        Assert.Throws<InvalidOperationException>(
            () => GeminiImageGenerator.Read(
                """{"candidates":[{"content":{"parts":[{"text":"I can't draw that."}]},"finishReason":"IMAGE_SAFETY"}]}""",
                Request()));
    }

    [Fact]
    public void Read_Should_Name_The_Finish_Reason_When_There_Is_No_Image()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => GeminiImageGenerator.Read(
                """{"candidates":[{"content":{"parts":[]},"finishReason":"IMAGE_SAFETY"}]}""",
                Request()));

        Assert.Contains("IMAGE_SAFETY", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Read_Should_Name_The_Block_Reason_When_The_Prompt_Was_Blocked()
    {
        // A blocked prompt has no candidates at all; promptFeedback is the only clue.
        var exception = Assert.Throws<InvalidOperationException>(
            () => GeminiImageGenerator.Read(
                """{"promptFeedback":{"blockReason":"PROHIBITED_CONTENT"}}""", Request()));

        Assert.Contains("PROHIBITED_CONTENT", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Read_Should_Accept_The_Snake_Case_Field_Names_Too()
    {
        // The REST reference documents inline_data; the live API emits inlineData.
        var image = GeminiImageGenerator.Read(
            $$$"""{"candidates":[{"content":{"parts":[{"inline_data":{"mime_type":"image/png","data":"{{{onePixel}}}"}}]}}]}""",
            Request());

        Assert.Equal("pretend-png-bytes", Encoding.UTF8.GetString(image.Bytes.ToArray()));
    }
}
