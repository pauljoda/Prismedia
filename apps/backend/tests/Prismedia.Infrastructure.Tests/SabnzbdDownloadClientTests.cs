using System.Net;
using System.Text;
using Prismedia.Application.Acquisition;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Acquisition;

namespace Prismedia.Infrastructure.Tests;

/// <summary>
/// Pins the SABnzbd adapter's wire behavior against canned API responses: nzo_id tracking through
/// queue and history, the completion rule (only a Completed history slot is complete — post-processing
/// states stay in-flight so imports never fire on a half-unpacked payload), definitive-failure mapping
/// with SABnzbd's explanation, the in-band error envelope, and API-key auth on the query string.
/// </summary>
public sealed class SabnzbdDownloadClientTests {
    private static readonly DownloadClientConnection Connection =
        new(Guid.NewGuid(), DownloadClientKind.Sabnzbd, "http://sab:8080", null, null, "prismedia", ApiKey: "secret-key");

    [Fact]
    public async Task AddReturnsTheNzoIdAndAuthenticatesWithTheApiKey() {
        var handler = new CannedHandler(_ => """{"status": true, "nzo_ids": ["SABnzbd_nzo_abc123"]}""");
        var client = new SabnzbdDownloadClient(new HttpClient(handler));

        var id = await client.AddAsync(Connection, new DownloadAddRequest("http://indexer/get.nzb", null, "prismedia"), CancellationToken.None);

        Assert.Equal("SABnzbd_nzo_abc123", id);
        var request = Assert.Single(handler.Requests);
        Assert.Contains("mode=addurl", request);
        Assert.Contains("apikey=secret-key", request);
        Assert.Contains("cat=prismedia", request);
    }

    [Fact]
    public async Task QueueSlotMapsProgressAndNeverReadsComplete() {
        var handler = new CannedHandler(uri => uri.Contains("mode=queue")
            ? """{"queue": {"kbpersec": "2048.0", "slots": [{"nzo_id": "SABnzbd_nzo_abc123", "filename": "My.Show.S01E01", "status": "Downloading", "mb": "100.0", "mbleft": "25.0", "cat": "prismedia", "timeleft": "0:02:30"}]}}"""
            : """{"history": {"slots": []}}""");
        var client = new SabnzbdDownloadClient(new HttpClient(handler));

        var status = await client.GetItemAsync(Connection, "SABnzbd_nzo_abc123", CancellationToken.None);

        Assert.NotNull(status);
        Assert.Equal(0.75, status.Progress, precision: 3);
        Assert.False(status.IsComplete);
        Assert.False(status.IsFailed);
        Assert.Null(status.ContentPath);
    }

    [Fact]
    public async Task OnlyACompletedHistorySlotIsCompleteAndCarriesTheStoragePath() {
        var handler = new CannedHandler(uri => uri.Contains("mode=queue")
            ? """{"queue": {"slots": []}}"""
            : """{"history": {"slots": [{"nzo_id": "SABnzbd_nzo_abc123", "name": "My.Show.S01E01", "status": "Completed", "storage": "/downloads/complete/prismedia/My.Show.S01E01", "bytes": 104857600, "category": "prismedia"}]}}""");
        var client = new SabnzbdDownloadClient(new HttpClient(handler));

        var status = await client.GetItemAsync(Connection, "SABnzbd_nzo_abc123", CancellationToken.None);

        Assert.NotNull(status);
        Assert.True(status.IsComplete);
        Assert.Equal(1, status.Progress);
        Assert.Equal("/downloads/complete/prismedia/My.Show.S01E01", status.ContentPath);
    }

    [Fact]
    public async Task PostProcessingHistoryStatesStayInFlight() {
        var handler = new CannedHandler(uri => uri.Contains("mode=queue")
            ? """{"queue": {"slots": []}}"""
            : """{"history": {"slots": [{"nzo_id": "SABnzbd_nzo_abc123", "name": "My.Show.S01E01", "status": "Extracting", "storage": "", "category": "prismedia"}]}}""");
        var client = new SabnzbdDownloadClient(new HttpClient(handler));

        var status = await client.GetItemAsync(Connection, "SABnzbd_nzo_abc123", CancellationToken.None);

        Assert.NotNull(status);
        Assert.False(status.IsComplete);
        Assert.False(status.IsFailed);
    }

    [Fact]
    public async Task AFailedHistorySlotMapsToADefinitiveFailureWithTheExplanation() {
        var handler = new CannedHandler(uri => uri.Contains("mode=queue")
            ? """{"queue": {"slots": []}}"""
            : """{"history": {"slots": [{"nzo_id": "SABnzbd_nzo_abc123", "name": "My.Show.S01E01", "status": "Failed", "fail_message": "Unpacking failed, archive requires a password", "category": "prismedia"}]}}""");
        var client = new SabnzbdDownloadClient(new HttpClient(handler));

        var status = await client.GetItemAsync(Connection, "SABnzbd_nzo_abc123", CancellationToken.None);

        Assert.NotNull(status);
        Assert.True(status.IsFailed);
        Assert.False(status.IsComplete);
        Assert.Equal("Unpacking failed, archive requires a password", status.FailureMessage);
    }

    [Fact]
    public async Task ListItemsScopesToTheCategoryAcrossQueueAndHistory() {
        var handler = new CannedHandler(uri => uri.Contains("mode=queue")
            ? """{"queue": {"slots": [{"nzo_id": "q1", "filename": "Mine", "status": "Downloading", "mb": "10", "mbleft": "5", "cat": "prismedia"}, {"nzo_id": "q2", "filename": "Other", "status": "Downloading", "mb": "10", "mbleft": "5", "cat": "tv"}]}}"""
            : """{"history": {"slots": [{"nzo_id": "h1", "name": "Done", "status": "Completed", "storage": "/d/Done", "category": "prismedia"}, {"nzo_id": "h2", "name": "Foreign", "status": "Completed", "storage": "/d/F", "category": "movies"}]}}""");
        var client = new SabnzbdDownloadClient(new HttpClient(handler));

        var items = await client.ListItemsAsync(Connection, CancellationToken.None);

        Assert.Equal(["q1", "h1"], items.Select(item => item.ClientItemId).ToArray());
    }

    [Fact]
    public async Task TheInBandErrorEnvelopeBecomesAnException() {
        var handler = new CannedHandler(_ => """{"status": false, "error": "API Key Incorrect"}""");
        var client = new SabnzbdDownloadClient(new HttpClient(handler));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.AddAsync(Connection, new DownloadAddRequest("http://indexer/get.nzb", null, "prismedia"), CancellationToken.None));
        Assert.Contains("API Key Incorrect", ex.Message);
    }

    [Fact]
    public async Task AForbiddenResponseReadsAsARejectedApiKey() {
        // SABnzbd 5.x answers a wrong API key with HTTP 403 instead of the in-band envelope (verified live).
        var handler = new CannedHandler(_ => "denied") { StatusCode = HttpStatusCode.Forbidden };
        var client = new SabnzbdDownloadClient(new HttpClient(handler));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.AddAsync(Connection, new DownloadAddRequest("http://indexer/get.nzb", null, "prismedia"), CancellationToken.None));
        Assert.Contains("rejected the API key", ex.Message);
    }

    [Fact]
    public async Task APlainTextErrorBodyBecomesAReadableException() {
        // Pre-5.x SABnzbd ignores output=json for auth errors and returns plain text with HTTP 200.
        var handler = new CannedHandler(_ => "API Key Incorrect");
        var client = new SabnzbdDownloadClient(new HttpClient(handler));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.AddAsync(Connection, new DownloadAddRequest("http://indexer/get.nzb", null, "prismedia"), CancellationToken.None));
        Assert.Contains("API Key Incorrect", ex.Message);
    }

    /// <summary>Responds to every request with a canned body chosen by the request URI; records the URIs seen.</summary>
    private sealed class CannedHandler(Func<string, string> bodyFor) : HttpMessageHandler {
        public List<string> Requests { get; } = [];
        public HttpStatusCode StatusCode { get; init; } = HttpStatusCode.OK;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
            var uri = request.RequestUri!.ToString();
            Requests.Add(uri);
            return Task.FromResult(new HttpResponseMessage(StatusCode) {
                Content = new StringContent(bodyFor(uri), Encoding.UTF8, "application/json")
            });
        }
    }
}
