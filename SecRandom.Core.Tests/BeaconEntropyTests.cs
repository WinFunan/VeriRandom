using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SecRandom.Core.Models.Verification;
using SecRandom.Core.Services;
using SecRandom.Core.Services.Config;
using SecRandom.Core.Services.Verification;
using SecRandom.Services.Verification;
using SecRandom.Shared;

namespace SecRandom.Core.Tests;

public sealed class BeaconEntropyTests : IDisposable
{
    private readonly string _dataRoot =
        Path.Combine(Path.GetTempPath(), "SecRandom", "beacon-tests", Guid.NewGuid().ToString("N"));

    public BeaconEntropyTests()
    {
        ResetDataRootForTests();
        ConfigureDataRootForTests(_dataRoot);
    }

    public void Dispose()
    {
        ResetDataRootForTests();
        if (Directory.Exists(_dataRoot))
            Directory.Delete(_dataRoot, recursive: true);
    }

    [Fact]
    public void Derive_IsDeterministicAndProducesA32ByteSeed()
    {
        var pulse = CreatePulse(1000);

        var first = BeaconSeedDerivation.Derive(pulse, 0);
        var second = BeaconSeedDerivation.Derive(pulse, 0);

        Assert.Equal(32, first.Length);
        Assert.Equal(first, second);
    }

    [Fact]
    public void Derive_DependsOnPulseAndSequence()
    {
        var pulse = CreatePulse(1000);
        var newerPulse = CreatePulse(1001);
        var differentOutput = CreatePulse(1000, output: new string('b', 128));

        var baseline = BeaconSeedDerivation.Derive(pulse, 0);

        Assert.NotEqual(baseline, BeaconSeedDerivation.Derive(pulse, 1));
        Assert.NotEqual(baseline, BeaconSeedDerivation.Derive(newerPulse, 0));
        Assert.NotEqual(baseline, BeaconSeedDerivation.Derive(differentOutput, 0));
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz")]
    public void DecodeOutputValue_RejectsMalformedValues(string output)
    {
        Assert.ThrowsAny<Exception>(() => BeaconSeedDerivation.DecodeOutputValue(output));
    }

    [Fact]
    public void SequencePolicy_StartsAtZeroAndIncrementsWithinAPulse()
    {
        var pulse = CreatePulse(1000);

        Assert.Equal(0, BeaconSequencePolicy.ResolveNext(null, pulse, out var noState));
        Assert.Equal(BeaconSequenceFailure.None, noState);

        var state = new BeaconSequenceState(pulse.PulseIndex, pulse.ChainIndex, pulse.OutputValue, 0);
        Assert.Equal(1, BeaconSequencePolicy.ResolveNext(state, pulse, out var samePulse));
        Assert.Equal(BeaconSequenceFailure.None, samePulse);

        Assert.Equal(0, BeaconSequencePolicy.ResolveNext(state, CreatePulse(1001), out var newerPulse));
        Assert.Equal(BeaconSequenceFailure.None, newerPulse);
    }

    [Fact]
    public void SequencePolicy_RejectsRegressionAndValueConflict()
    {
        var state = new BeaconSequenceState(1000, 1, new string('a', 128), 5);

        Assert.Equal(-1, BeaconSequencePolicy.ResolveNext(state, CreatePulse(999), out var regression));
        Assert.Equal(BeaconSequenceFailure.PulseIndexRegression, regression);

        Assert.Equal(-1, BeaconSequencePolicy.ResolveNext(state, CreatePulse(1000, output: new string('c', 128)),
            out var conflict));
        Assert.Equal(BeaconSequenceFailure.PulseValueConflict, conflict);
    }

    [Fact]
    public void EndpointPolicy_UsesTheOfficialDefaultAndRejectsRemoteHttp()
    {
        Assert.Equal(
            BeaconEndpointPolicy.DefaultEndpoint,
            BeaconEndpointPolicy.Normalize(null).ToString());
        Assert.Equal(
            "https://example.org/beacon/2.0/",
            BeaconEndpointPolicy.Normalize("https://example.org/beacon/2.0").ToString());
        Assert.Equal(
            "http://127.0.0.1:8080/",
            BeaconEndpointPolicy.Normalize("http://127.0.0.1:8080").ToString());

        Assert.Throws<FormatException>(() => BeaconEndpointPolicy.Normalize("http://example.org/beacon/"));
        Assert.Throws<FormatException>(() => BeaconEndpointPolicy.Normalize("not-a-url"));
    }

    [Fact]
    public async Task Provider_ReservesSequentialSeedsWithinOnePulseAndResetsOnANewPulse()
    {
        var client = new StubBeaconClient(CreatePulse(2000));
        var provider = new BeaconEntropyProvider(client, CreateConfigHandler(), NullLogger<BeaconEntropyProvider>.Instance);

        var first = await provider.ReserveAsync(CancellationToken.None);
        var second = await provider.ReserveAsync(CancellationToken.None);

        Assert.Equal(0, first.Beacon.Sequence);
        Assert.Equal(1, second.Beacon.Sequence);
        Assert.Equal(2000, first.Beacon.PulseIndex);
        Assert.Equal(BeaconSeedDerivation.DerivationId, first.Beacon.Derivation);
        Assert.NotEqual(first.Seed, second.Seed);

        client.Pulse = CreatePulse(2001);
        var nextPulse = await provider.ReserveAsync(CancellationToken.None);
        Assert.Equal(0, nextPulse.Beacon.Sequence);
        Assert.Equal(2001, nextPulse.Beacon.PulseIndex);
    }

    [Fact]
    public async Task Provider_PersistsTheSequenceAnchorAcrossInstances()
    {
        var client = new StubBeaconClient(CreatePulse(3000));
        var firstProvider = new BeaconEntropyProvider(client, CreateConfigHandler(), NullLogger<BeaconEntropyProvider>.Instance);
        _ = await firstProvider.ReserveAsync(CancellationToken.None);

        var secondProvider = new BeaconEntropyProvider(client, CreateConfigHandler(), NullLogger<BeaconEntropyProvider>.Instance);
        var resumed = await secondProvider.ReserveAsync(CancellationToken.None);

        Assert.Equal(1, resumed.Beacon.Sequence);
        Assert.True(File.Exists(Utils.GetFilePath("proofs", "beacon-state.json")));
    }

    [Fact]
    public async Task Provider_RefusesToMoveBackwardsInsteadOfSubstitutingLocalEntropy()
    {
        var client = new StubBeaconClient(CreatePulse(4000));
        var provider = new BeaconEntropyProvider(client, CreateConfigHandler(), NullLogger<BeaconEntropyProvider>.Instance);
        _ = await provider.ReserveAsync(CancellationToken.None);

        client.Pulse = CreatePulse(3999);
        await Assert.ThrowsAsync<BeaconEntropyException>(() => provider.ReserveAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Provider_SurfacesBeaconFailuresWithoutAFallbackSeed()
    {
        var client = new StubBeaconClient(CreatePulse(5000), new HttpRequestException("offline"));
        var provider = new BeaconEntropyProvider(client, CreateConfigHandler(), NullLogger<BeaconEntropyProvider>.Instance);

        await Assert.ThrowsAsync<HttpRequestException>(() => provider.ReserveAsync(CancellationToken.None));
        Assert.False(File.Exists(Utils.GetFilePath("proofs", "beacon-state.json")));
    }

    [Fact]
    public async Task NistBeaconClient_ParsesAPublishedPulseAndValidatesItsOutput()
    {
        const string json = """
                            {"pulse":{"version":"Version 2.0","cipherSuite":0,"period":60,"certificateId":"cert-1","chainIndex":7,"pulseIndex":8,"timeStamp":"2026-01-02T03:04:05.000Z","outputValue":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","signatureValue":"c2ln"}}
                            """;
        var httpClient = new HttpClient(new StubHandler(json));
        var beaconClient = new NistBeaconClient(httpClient, NullLogger<NistBeaconClient>.Instance);

        var pulse = await beaconClient.GetLatestPulseAsync(new Uri(BeaconEndpointPolicy.DefaultEndpoint), CancellationToken.None);

        Assert.Equal(8, pulse.PulseIndex);
        Assert.Equal(7, pulse.ChainIndex);
        Assert.Equal(60, pulse.PeriodSeconds);
        Assert.Equal("cert-1", pulse.CertificateId);
        Assert.Equal(new string('a', 128), pulse.OutputValue);
    }

    [Fact]
    public async Task NistBeaconClient_RejectsAPulseWithAMalformedOutput()
    {
        const string json = """
                            {"pulse":{"period":60,"certificateId":"cert-1","chainIndex":7,"pulseIndex":8,"timeStamp":"2026-01-02T03:04:05.000Z","outputValue":"not-hex","signatureValue":"c2ln"}}
                            """;
        var httpClient = new HttpClient(new StubHandler(json));
        var beaconClient = new NistBeaconClient(httpClient, NullLogger<NistBeaconClient>.Instance);

        await Assert.ThrowsAsync<FormatException>(() =>
            beaconClient.GetLatestPulseAsync(new Uri(BeaconEndpointPolicy.DefaultEndpoint), CancellationToken.None));
    }

    private static BeaconPulse CreatePulse(long pulseIndex, string? output = null)
        => new(pulseIndex, 1, 60, DateTimeOffset.UnixEpoch, output ?? new string('a', 128), "c2ln", "cert-1");

    private static MainConfigHandler CreateConfigHandler()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.None));
        services.AddCoreRuntimeServices();
        return services.BuildServiceProvider().GetRequiredService<MainConfigHandler>();
    }

    private static void ConfigureDataRootForTests(string dataRoot)
        => GetUtilsMethod("ConfigureDataRoot").Invoke(null, [dataRoot]);

    private static void ResetDataRootForTests()
        => GetUtilsMethod("ResetDataRootForTests").Invoke(null, null);

    private static MethodInfo GetUtilsMethod(string name)
        => typeof(Utils).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)
           ?? throw new InvalidOperationException($"Utils.{name} was not found.");

    private sealed class StubBeaconClient(BeaconPulse pulse, Exception? failure = null) : INistBeaconClient
    {
        public BeaconPulse Pulse { get; set; } = pulse;

        public Task<BeaconPulse> GetLatestPulseAsync(Uri endpoint, CancellationToken cancellationToken)
            => failure is null ? Task.FromResult(Pulse) : Task.FromException<BeaconPulse>(failure);
    }

    private sealed class StubHandler(string json, HttpStatusCode statusCode = HttpStatusCode.OK) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
    }
}
