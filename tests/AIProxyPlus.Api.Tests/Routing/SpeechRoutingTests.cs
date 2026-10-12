using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AIProxyPlus.Api.Tests.Infrastructure;
using AIProxyPlus.Domain.Entities;
using AIProxyPlus.Domain.Enums;
using FluentAssertions;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace AIProxyPlus.Api.Tests.Routing;

public class SpeechRoutingTests : IAsyncLifetime, IDisposable
{
    private readonly ApiTestFactory _factory = new();
    private WireMockServer _backend = null!;

    public Task InitializeAsync()
    {
        _backend = WireMockServer.Start();
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _backend.Stop();
        return Task.CompletedTask;
    }

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Transcriptions_WithMultipartAudio_ForwardsOriginalBytesAndBoundary()
    {
        var modelName = SeedModel();
        _backend.Given(Request.Create().WithPath("/openai/v1/audio/transcriptions").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200).WithBodyAsJson(new { text = "hello" }));

        const string boundary = "speech-test-boundary";
        using var content = new MultipartFormDataContent(boundary);
        content.Add(new StringContent(modelName), "model");
        var audioBytes = new byte[] { 0, 1, 2, 127, 128, 254, 255 };
        var audioContent = new ByteArrayContent(audioBytes);
        audioContent.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        content.Add(audioContent, "file", "sample.wav");
        var expectedBody = await content.ReadAsByteArrayAsync();

        var response = await _factory.CreateClient().PostAsync("/openai/v1/audio/transcriptions", content);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var received = _backend.LogEntries.Should().ContainSingle().Which.RequestMessage;
        received.Headers.Should().ContainKey("Content-Type")
            .WhoseValue.Should().ContainSingle()
            .Which.Should().Contain($"boundary=\"{boundary}\"");
        received.BodyData.Should().NotBeNull();
        received.BodyData!.BodyAsBytes.Should().Equal(expectedBody);
    }

    [Fact]
    public async Task Speech_WithBinaryUpstreamResponse_ReturnsOriginalAudioAndContentType()
    {
        var modelName = SeedModel();
        var audioBytes = new byte[] { 82, 73, 70, 70, 0, 128, 255, 10 };
        _backend.Given(Request.Create().WithPath("/openai/v1/audio/speech").UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "audio/mpeg")
                .WithBody(audioBytes));

        var response = await _factory.CreateClient().PostAsJsonAsync(
            "/openai/v1/audio/speech",
            new { model = modelName, input = "Hello", voice = "alloy" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("audio/mpeg");
        (await response.Content.ReadAsByteArrayAsync()).Should().Equal(audioBytes);
    }

    private string SeedModel()
    {
        var modelName = $"speech-{Guid.NewGuid():N}";
        var model = Model.Create(Guid.NewGuid(), modelName, capabilities: [ModelCapability.SpeechToText, ModelCapability.TextToSpeech]);
        var endpoint = Endpoint.Create(
            Guid.NewGuid(), $"speech-endpoint-{Guid.NewGuid():N}", _backend.Url!, AuthMode.ApiKey,
            secretRef: "endpoints/x", maxRetries: 0);
        var route = ApiRoute.Create(Guid.NewGuid(), "Native OpenAI", Provider.OpenAI, "/openai/v1");
        model.AddEndpoint(ModelEndpoint.Create(Guid.NewGuid(), model.Id, endpoint, route, 1));
        _factory.Models.Seed(model);
        return modelName;
    }
}