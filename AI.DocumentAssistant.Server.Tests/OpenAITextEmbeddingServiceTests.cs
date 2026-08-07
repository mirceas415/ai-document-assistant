using AI.DocumentAssistant.Server.Embeddings;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AI.DocumentAssistant.Server.Tests;

public sealed class OpenAITextEmbeddingServiceTests
{
    [Fact]
    public async Task OfficialSdkAdapterFailsSafelyWithoutConfiguredApiKey()
    {
        var configuration = new ConfigurationBuilder().Build();
        var client = new OpenAISdkEmbeddingClient(
            configuration,
            NullLogger<OpenAISdkEmbeddingClient>.Instance);

        var exception = await Assert.ThrowsAsync<DocumentEmbeddingException>(() =>
            client.GenerateEmbeddingsAsync(
                ["A safe offline test input."],
                EmbeddingArchitecture.DefaultModel,
                EmbeddingArchitecture.Dimensions,
                CancellationToken.None));

        Assert.Equal("Embedding service configuration is unavailable.", exception.SafeMessage);
    }

    [Fact]
    public async Task GenerateEmbeddingsPreservesInputAndOutputOrdering()
    {
        var inputs = new[] { "chunk-three", "chunk-one", "chunk-two" };
        var markers = new Dictionary<string, float>(StringComparer.Ordinal)
        {
            ["chunk-one"] = 1,
            ["chunk-two"] = 2,
            ["chunk-three"] = 3
        };
        var client = new RecordingEmbeddingClient((batch, _, dimensions, _) =>
            Task.FromResult<IReadOnlyList<float[]>>(
                batch.Select(input => CreateVector(dimensions, markers[input])).ToArray()));
        var service = CreateService(client, batchSize: 2);

        var result = await service.GenerateEmbeddingsAsync(inputs, CancellationToken.None);

        Assert.Equal(inputs, client.Calls.SelectMany(call => call.Inputs));
        Assert.Equal(new[] { 3f, 1f, 2f }, result.Embeddings.Select(vector => vector[0]));
        Assert.Equal(EmbeddingArchitecture.DefaultModel, result.Model);
        Assert.Equal(EmbeddingArchitecture.Dimensions, result.Dimensions);
    }

    [Fact]
    public async Task GenerateEmbeddingsRejectsUnexpectedProviderResultCount()
    {
        var client = new RecordingEmbeddingClient((_, _, dimensions, _) =>
            Task.FromResult<IReadOnlyList<float[]>>([CreateVector(dimensions, 1)]));
        var service = CreateService(client, batchSize: 3);

        var exception = await Assert.ThrowsAsync<DocumentEmbeddingException>(() =>
            service.GenerateEmbeddingsAsync(["one", "two"], CancellationToken.None));

        Assert.Contains("result count", exception.SafeMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GenerateEmbeddingsRejectsUnexpectedVectorDimensions()
    {
        var client = new RecordingEmbeddingClient((_, _, dimensions, _) =>
            Task.FromResult<IReadOnlyList<float[]>>([CreateVector(dimensions - 1, 1)]));
        var service = CreateService(client);

        var exception = await Assert.ThrowsAsync<DocumentEmbeddingException>(() =>
            service.GenerateEmbeddingsAsync(["one"], CancellationToken.None));

        Assert.Contains("vector size", exception.SafeMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t\r\n")]
    public async Task GenerateEmbeddingsRejectsEmptyOrWhitespaceInput(string input)
    {
        var client = new RecordingEmbeddingClient((_, _, _, _) =>
            throw new InvalidOperationException("The provider must not be called."));
        var service = CreateService(client);

        var exception = await Assert.ThrowsAsync<DocumentEmbeddingException>(() =>
            service.GenerateEmbeddingsAsync([input], CancellationToken.None));

        Assert.Contains("Empty document chunks", exception.SafeMessage, StringComparison.Ordinal);
        Assert.Empty(client.Calls);
    }

    [Fact]
    public async Task GenerateEmbeddingsHonorsAndForwardsCancellation()
    {
        var providerStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new RecordingEmbeddingClient(async (_, _, _, cancellationToken) =>
        {
            providerStarted.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return Array.Empty<float[]>();
        });
        var service = CreateService(client);
        using var cancellationSource = new CancellationTokenSource();

        var operation = service.GenerateEmbeddingsAsync(["cancellable input"], cancellationSource.Token);
        await providerStarted.Task;
        cancellationSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation);
        var call = Assert.Single(client.Calls);
        Assert.True(call.CancellationToken.CanBeCanceled);
        Assert.True(call.CancellationToken.IsCancellationRequested);
    }

    [Theory]
    [InlineData("A plain English document chunk remains unchanged.")]
    [InlineData("\u0218tiin\u021B\u0103 \u0219i \u00EEnv\u0103\u021Bare: \u0103\u00E2\u00EE\u0219\u021B, Rom\u00E2nia.")]
    [InlineData("Contractul este valid, \u0219i the English clause remains intact.")]
    [InlineData("\u039A\u03B1\u03BB\u03B7\u03BC\u03AD\u03C1\u03B1 \u4E16\u754C \U0001F469\U0001F3FD\u200D\U0001F4BB e\u0301 \u2014 Unicode stays exact.")]
    public async Task GenerateEmbeddingsPreservesExactMultilingualAndUnicodeInput(string input)
    {
        var client = new RecordingEmbeddingClient((batch, _, dimensions, _) =>
            Task.FromResult<IReadOnlyList<float[]>>(
                batch.Select((_, index) => CreateVector(dimensions, index + 1)).ToArray()));
        var service = CreateService(client);

        await service.GenerateEmbeddingsAsync([input], CancellationToken.None);

        var call = Assert.Single(client.Calls);
        Assert.Equal(input, Assert.Single(call.Inputs));
    }

    [Fact]
    public async Task BatchSizeThreeWithEightInputsProducesThreeThreeTwoAndExactAssociation()
    {
        var inputs = Enumerable.Range(1, 8)
            .Select(index => $"chunk-{index}")
            .ToArray();
        var client = new RecordingEmbeddingClient((batch, _, dimensions, _) =>
            Task.FromResult<IReadOnlyList<float[]>>(
                batch.Select(input =>
                {
                    var marker = int.Parse(input.AsSpan("chunk-".Length));
                    return CreateVector(dimensions, marker);
                }).ToArray()));
        var service = CreateService(client, batchSize: 3);

        var result = await service.GenerateEmbeddingsAsync(inputs, CancellationToken.None);

        Assert.Equal(new[] { 3, 3, 2 }, client.Calls.Select(call => call.Inputs.Count));
        Assert.Equal(inputs, client.Calls.SelectMany(call => call.Inputs));
        Assert.Equal(
            Enumerable.Range(1, 8).Select(index => (float)index),
            result.Embeddings.Select(vector => vector[0]));
        Assert.All(client.Calls, call =>
        {
            Assert.Equal(EmbeddingArchitecture.DefaultModel, call.Model);
            Assert.Equal(EmbeddingArchitecture.Dimensions, call.Dimensions);
        });
        Assert.All(result.Embeddings, vector =>
            Assert.Equal(EmbeddingArchitecture.Dimensions, vector.Length));
    }

    private static OpenAITextEmbeddingService CreateService(
        IOpenAIEmbeddingClient client,
        int batchSize = EmbeddingArchitecture.DefaultBatchSize) =>
        new(
            client,
            Options.Create(new OpenAIEmbeddingOptions
            {
                EmbeddingModel = EmbeddingArchitecture.DefaultModel,
                EmbeddingDimensions = EmbeddingArchitecture.Dimensions,
                BatchSize = batchSize
            }),
            NullLogger<OpenAITextEmbeddingService>.Instance);

    private static float[] CreateVector(int dimensions, float marker)
    {
        var vector = new float[dimensions];
        vector[0] = marker;
        return vector;
    }

    private sealed class RecordingEmbeddingClient(
        Func<IReadOnlyList<string>, string, int, CancellationToken, Task<IReadOnlyList<float[]>>> handler)
        : IOpenAIEmbeddingClient
    {
        public List<ProviderCall> Calls { get; } = [];

        public Task<IReadOnlyList<float[]>> GenerateEmbeddingsAsync(
            IReadOnlyList<string> inputs,
            string model,
            int dimensions,
            CancellationToken cancellationToken)
        {
            Calls.Add(new ProviderCall(inputs.ToArray(), model, dimensions, cancellationToken));
            return handler(inputs, model, dimensions, cancellationToken);
        }
    }

    private sealed record ProviderCall(
        IReadOnlyList<string> Inputs,
        string Model,
        int Dimensions,
        CancellationToken CancellationToken);
}
