using System.Net;
using Microsoft.EntityFrameworkCore;
using Prismedia.Contracts.Plugins;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Plugins;
using Prismedia.Infrastructure.StashCompat;
using Prismedia.Infrastructure.StashCompat.Model;

namespace Prismedia.Infrastructure.Tests;

public sealed class StashCompatTests {
    private const string SampleHtml = """
        <html><body>
          <h1 class="title">  Test Scene Title  </h1>
          <span class="date">2023-05-14</span>
          <div class="desc">Some details here.</div>
          <a class="studiolink" href="https://studio.example/foo">Cool Studio</a>
          <a class="perf">Alice</a>
          <a class="perf">Bob</a>
          <span class="tag">Tag1</span>
          <span class="tag">Tag2</span>
          <img class="cover" src="https://img.example/cover.jpg"/>
          <a class="permalink" href="https://site.example/scene/123">link</a>
        </body></html>
        """;

    private const string SampleYaml = """
        name: Test Site
        sceneByURL:
          - action: scrapeXPath
            url:
              - site.example
            scraper: sceneScraper
        sceneByName:
          action: scrapeXPath
          scraper: sceneScraper
        performerByURL:
          action: scrapeXPath
          scraper: sceneScraper
        xPathScrapers:
          sceneScraper:
            scene:
              Title: //h1[@class="title"]/text()
              Date:
                selector: //span[@class="date"]/text()
                postProcess:
                  - parseDate: "2006-01-02"
              Details: //div[@class="desc"]/text()
              URL: //a[@class="permalink"]/@href
              Image: //img[@class="cover"]/@src
              Studio:
                Name: //a[@class="studiolink"]/text()
                URL: //a[@class="studiolink"]/@href
              Performers:
                Name: //a[@class="perf"]/text()
              Tags:
                Name: //span[@class="tag"]/text()
        """;

    // ── postProcess pipeline ──────────────────────────────────────────

    [Fact]
    public void ReplaceAppliesDotallRegex() {
        var rules = StashYamlNode.Parse("""
            - replace:
                - regex: "Title:(.*)End"
                  with: "$1"
            """);
        var result = StashSelector.ApplyPostProcess("Title:\nHello\nEnd", rules);
        Assert.Equal("Hello", result);
    }

    [Fact]
    public void MapReplacesExactMatch() {
        var rules = StashYamlNode.Parse("""
            - map:
                f: Female
                m: Male
            """);
        Assert.Equal("Female", StashSelector.ApplyPostProcess("f", rules));
        Assert.Equal("other", StashSelector.ApplyPostProcess("other", rules));
    }

    [Fact]
    public void SubStringSlicesValue() {
        var rules = StashYamlNode.Parse("""
            - subString:
                start: 4
                end: 9
            """);
        Assert.Equal("world", StashSelector.ApplyPostProcess("foo world bar", rules));
    }

    [Fact]
    public void SplitTakesValueBeforeSeparator() {
        var rules = StashYamlNode.Parse("""
            - split: " |"
            """);
        Assert.Equal("Scene Name", StashSelector.ApplyPostProcess("Scene Name | Studio", rules));
    }

    [Theory]
    [InlineData("2006-01-02", "2023-05-14", "2023-05-14")]
    [InlineData("January 2, 2006", "May 14, 2023", "2023-05-14")]
    [InlineData("01/02/2006", "05/14/2023", "2023-05-14")]
    public void ParseDateNormalizesGoLayout(string layout, string input, string expected) {
        var rules = StashYamlNode.Parse($"""
            - parseDate: "{layout}"
            """);
        Assert.Equal(expected, StashSelector.ApplyPostProcess(input, rules));
    }

    [Fact]
    public void ApplyCommonSubstitutesVariables() {
        var common = StashYamlNode.Parse("""
            $base: //div[@class="scene"]
            """);
        Assert.Equal("//div[@class=\"scene\"]//h1", StashSelector.ApplyCommon("$base//h1", common));
    }

    // ── XPath engine ──────────────────────────────────────────────────

    [Fact]
    public void XPathEngineExtractsSceneFields() {
        var definition = StashScraperDefinition.TryParse(SampleYaml)!;
        var scene = new StashXPathEngine().EvaluateScene(SampleHtml, definition.XPathScraper("sceneScraper"));

        Assert.NotNull(scene);
        Assert.Equal("Test Scene Title", scene!.Title);
        Assert.Equal("2023-05-14", scene.Date);
        Assert.Equal("Some details here.", scene.Details);
        Assert.Equal("https://site.example/scene/123", scene.Url);
        Assert.Equal("https://img.example/cover.jpg", scene.Image);
        Assert.Equal("Cool Studio", scene.Studio?.Name);
        Assert.Equal(["Alice", "Bob"], scene.Performers);
        Assert.Equal(["Tag1", "Tag2"], scene.Tags);
    }

    // ── result mapping ────────────────────────────────────────────────

    [Fact]
    public void MapperProducesProposalPatch() {
        var scene = new StashScrapedScene {
            Title = "Test Scene Title",
            Date = "2023-05-14",
            Details = "Some details here.",
            Url = "https://site.example/scene/123",
            Image = "https://img.example/cover.jpg",
            Code = "ABC-123",
            Studio = new StashScrapedStudio { Name = "Cool Studio" },
            Performers = ["Alice", "Bob"],
            Tags = ["Tag1", "Tag2"]
        };

        var proposal = StashResultMapper.ToProposal(
            scene, "stash-test-site", "Test Site", "video", "https://site.example/scene/123", "Matched by URL", 0.9m);

        Assert.Equal("video", proposal.TargetKind);
        Assert.Equal("Test Scene Title", proposal.Patch.Title);
        Assert.Equal("Some details here.", proposal.Patch.Description);
        Assert.Equal("2023-05-14", proposal.Patch.Dates["release"]);
        Assert.Equal("Cool Studio", proposal.Patch.Studio);
        Assert.Equal(["Tag1", "Tag2"], proposal.Patch.Tags);
        Assert.Equal(["Alice", "Bob"], proposal.Patch.Credits.Select(credit => credit.Name).ToArray());
        Assert.All(proposal.Patch.Credits, credit => Assert.Equal("performer", credit.Role));
        Assert.Equal("ABC-123", proposal.Patch.ExternalIds["stash-test-site"]);
        Assert.Contains("https://site.example/scene/123", proposal.Patch.Urls);
        Assert.Single(proposal.Images);
        Assert.Equal("poster", proposal.Images[0].Kind);
    }

    [Fact]
    public void MapperBuildsCandidateForNameSearch() {
        var scene = new StashScrapedScene { Title = "Search Hit", Date = "2021-03-02", Code = "X1" };
        var candidate = StashResultMapper.ToCandidate(scene, "stash-test-site");

        Assert.NotNull(candidate);
        Assert.Equal("Search Hit", candidate!.Title);
        Assert.Equal(2021, candidate.Year);
        Assert.Equal("X1", candidate.ExternalIds["stash-test-site"]);
    }

    // ── manifest synthesis ────────────────────────────────────────────

    [Fact]
    public void ManifestFactoryDerivesSupportsFromCapabilities() {
        var manifest = StashScraperManifestFactory.TryCreate(SampleYaml, "/tmp/Test Site.yml");

        Assert.NotNull(manifest);
        Assert.Equal("stash-compat", manifest!.Runtime);
        Assert.Equal("stash-test-site", manifest.Id);
        Assert.True(manifest.IsNsfw);

        var video = manifest.Supports.Single(support => support.EntityKind == "video");
        Assert.Contains("lookup-url", video.Actions);
        Assert.Contains("search", video.Actions);
        var person = manifest.Supports.Single(support => support.EntityKind == "person");
        Assert.Contains("lookup-url", person.Actions);
    }

    // ── runner end-to-end ─────────────────────────────────────────────

    [Fact]
    public async Task RunnerReturnsProposalForUrlLookup() {
        var yamlPath = Path.Combine(Path.GetTempPath(), $"stash-{Guid.NewGuid():N}.yml");
        await File.WriteAllTextAsync(yamlPath, SampleYaml);
        try {
            var manifest = StashScraperManifestFactory.TryCreate(SampleYaml, yamlPath)!;
            var descriptor = new PluginDescriptor(manifest, yamlPath, Path.GetDirectoryName(yamlPath)!, yamlPath);
            var runner = new StashCompatRunner(new HttpClient(new FixedHtmlHandler(SampleHtml)));
            var request = new IdentifyPluginRequest(
                ProtocolVersion: 2,
                Action: "lookup-url",
                Auth: new Dictionary<string, string>(),
                Entity: new IdentifyEntitySnapshot(Guid.NewGuid(), "video", "Local Title"),
                Query: new IdentifyQuery(null, "https://site.example/scene/123", null),
                Hints: new IdentifyMatchHints(new Dictionary<string, string>(), [], null, null));

            var response = await runner.IdentifyAsync(descriptor, request, CancellationToken.None);

            Assert.True(response.Ok);
            Assert.NotNull(response.Result);
            Assert.Equal("Test Scene Title", response.Result!.Patch.Title);
            Assert.Equal("Cool Studio", response.Result.Patch.Studio);
            Assert.Equal(2, response.Result.Patch.Credits.Count);
        } finally {
            File.Delete(yamlPath);
        }
    }

    private const string SearchYaml = """
        name: Search Site
        sceneByName:
          action: scrapeXPath
          queryURL: https://search.example/find?q={title}
          scraper: searchScraper
        sceneByURL:
          - action: scrapeXPath
            url:
              - search.example
            scraper: sceneScraper
        xPathScrapers:
          searchScraper:
            scene:
              Title: //div[@class="result"]/a/text()
              URL: //div[@class="result"]/a/@href
          sceneScraper:
            scene:
              Title: //h1/text()
        """;

    private const string SearchResultsHtml = """
        <html><body>
          <div class="result"><a href="https://search.example/s/1">First Hit</a></div>
          <div class="result"><a href="https://search.example/s/2">Second Hit</a></div>
        </body></html>
        """;

    [Fact]
    public async Task RunnerReturnsCandidatesForNameSearch() {
        var yamlPath = Path.Combine(Path.GetTempPath(), $"stash-search-{Guid.NewGuid():N}.yml");
        await File.WriteAllTextAsync(yamlPath, SearchYaml);
        try {
            var manifest = StashScraperManifestFactory.TryCreate(SearchYaml, yamlPath)!;
            var descriptor = new PluginDescriptor(manifest, yamlPath, Path.GetDirectoryName(yamlPath)!, yamlPath);
            var runner = new StashCompatRunner(new HttpClient(new FixedHtmlHandler(SearchResultsHtml)));
            var request = new IdentifyPluginRequest(
                ProtocolVersion: 2,
                Action: "search",
                Auth: new Dictionary<string, string>(),
                Entity: new IdentifyEntitySnapshot(Guid.NewGuid(), "video", "Some Scene"),
                Query: new IdentifyQuery("Some Scene", null, null),
                Hints: new IdentifyMatchHints(new Dictionary<string, string>(), [], null, null));

            var response = await runner.IdentifyAsync(descriptor, request, CancellationToken.None);

            Assert.True(response.Ok);
            Assert.Null(response.Result!.Patch);
            Assert.Equal(2, response.Result.Candidates.Count);
            Assert.Equal("First Hit", response.Result.Candidates[0].Title);
            Assert.Equal("https://search.example/s/1", response.Result.Candidates[0].ExternalIds[manifest.Id]);
        } finally {
            File.Delete(yamlPath);
        }
    }

    [Fact]
    public async Task RunnerResolvesChosenCandidateUrlFromExternalIds() {
        var yamlPath = Path.Combine(Path.GetTempPath(), $"stash-pick-{Guid.NewGuid():N}.yml");
        await File.WriteAllTextAsync(yamlPath, SearchYaml);
        try {
            var manifest = StashScraperManifestFactory.TryCreate(SearchYaml, yamlPath)!;
            var descriptor = new PluginDescriptor(manifest, yamlPath, Path.GetDirectoryName(yamlPath)!, yamlPath);
            var runner = new StashCompatRunner(new HttpClient(new FixedHtmlHandler("<html><body><h1>Resolved Scene</h1></body></html>")));
            // Mirrors identifyWithCandidate: the chosen candidate's external ids carry the scene URL.
            var request = new IdentifyPluginRequest(
                ProtocolVersion: 2,
                Action: "search",
                Auth: new Dictionary<string, string>(),
                Entity: new IdentifyEntitySnapshot(Guid.NewGuid(), "video", "Some Scene"),
                Query: new IdentifyQuery(null, null, new Dictionary<string, string> { [manifest.Id] = "https://search.example/s/1" }),
                Hints: new IdentifyMatchHints(new Dictionary<string, string>(), [], null, null));

            var response = await runner.IdentifyAsync(descriptor, request, CancellationToken.None);

            Assert.True(response.Ok);
            Assert.NotNull(response.Result?.Patch);
            Assert.Equal("Resolved Scene", response.Result!.Patch.Title);
        } finally {
            File.Delete(yamlPath);
        }
    }

    [Fact]
    public async Task CatalogDiscoversAndInstallsDroppedScraperYaml() {
        var root = Path.Combine(Path.GetTempPath(), $"stash-discovery-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(Path.Combine(root, "Test Site.yml"), SampleYaml);
        try {
            await using var db = CreateContext();
            var catalog = new PluginCatalogService(db, new PluginCatalogOptions([root], root, "1.0.0"));

            var providers = await catalog.ListProvidersAsync(CancellationToken.None);
            var provider = Assert.Single(providers, candidate => candidate.Id == "stash-test-site");
            Assert.False(provider.Installed);
            Assert.True(provider.IsNsfw);
            Assert.Contains(provider.Supports, support => support.EntityKind == "video");

            var installed = await catalog.InstallAsync("stash-test-site", CancellationToken.None);
            Assert.NotNull(installed);
            Assert.True(installed!.Installed);

            var descriptor = await catalog.FindProviderAsync("stash-test-site", "video", CancellationToken.None);
            Assert.NotNull(descriptor);
            Assert.Equal("stash-compat", descriptor!.Manifest.Runtime);

            var config = db.ProviderConfigs.Single(row => row.ProviderCode == "stash-test-site");
            Assert.Equal(Prismedia.Domain.Entities.ProviderType.StashCompat, config.ProviderType);
        } finally {
            Directory.Delete(root, recursive: true);
        }
    }

    // ── install from CommunityScrapers index ──────────────────────────

    [Fact]
    public void InstallerParsesIndexWithRequires() {
        const string indexYaml = """
            - id: Algolia
              name: Algolia
              version: abc123
              path: Algolia.zip
              sha256: deadbeef
              requires:
                - py_common
                - AyloAPI
            - id: py_common
              name: py_common
              version: def456
              path: py_common.zip
              sha256: cafe
            """;
        var entries = StashScraperInstaller.ParseIndex(indexYaml);

        Assert.Equal(2, entries.Count);
        var algolia = entries["Algolia"];
        Assert.Equal("stash-algolia", algolia.ProviderId);
        Assert.Equal("Algolia.zip", algolia.Path);
        Assert.Equal(["py_common", "AyloAPI"], algolia.Requires);
    }

    [Fact]
    public async Task CatalogInstallsScraperFromRemoteIndexWithDependency() {
        var indexUrl = "https://index.example/stable/index.yml";
        var indexYaml = """
            - id: TestSite
              name: Test Site
              version: v1
              path: TestSite.zip
              sha256: ""
              requires:
                - py_common
            - id: py_common
              name: py_common
              version: v1
              path: py_common.zip
              sha256: ""
            """;
        // CommunityScrapers zips name the YAML after the package id (TestSite.zip -> TestSite.yml),
        // so the index-derived and discovery-derived provider ids agree.
        var archives = new Dictionary<string, byte[]> {
            ["https://index.example/stable/TestSite.zip"] = ZipWith(("TestSite.yml", SampleYaml)),
            ["https://index.example/stable/py_common.zip"] = ZipWith(("py_common/log.py", "def log(): pass\n"))
        };

        var cacheRoot = Path.Combine(Path.GetTempPath(), $"stash-install-{Guid.NewGuid():N}");
        Directory.CreateDirectory(cacheRoot);
        try {
            await using var db = CreateContext();
            var options = new PluginCatalogOptions([], cacheRoot, "1.0.0", null, indexUrl);
            var http = new HttpClient(new IndexAndArchiveHandler(indexUrl, indexYaml, archives));
            var catalog = new PluginCatalogService(db, options, http);

            var available = await catalog.ListStashScrapersAsync(CancellationToken.None);
            Assert.Contains(available, entry => entry.ProviderId == "stash-testsite");

            var installed = await catalog.InstallAsync("stash-testsite", CancellationToken.None);

            Assert.NotNull(installed);
            Assert.True(installed!.Installed);
            Assert.True(File.Exists(Path.Combine(cacheRoot, "scrapers", "TestSite", "TestSite.yml")));
            Assert.True(File.Exists(Path.Combine(cacheRoot, "scrapers", "py_common", "py_common", "log.py")));
        } finally {
            Directory.Delete(cacheRoot, recursive: true);
        }
    }

    private static byte[] ZipWith(params (string Name, string Content)[] files) {
        using var memory = new MemoryStream();
        using (var archive = new System.IO.Compression.ZipArchive(memory, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true)) {
            foreach (var (name, content) in files) {
                var entry = archive.CreateEntry(name);
                using var writer = new StreamWriter(entry.Open());
                writer.Write(content);
            }
        }

        return memory.ToArray();
    }

    private sealed class IndexAndArchiveHandler(string indexUrl, string indexYaml, IReadOnlyDictionary<string, byte[]> archives)
        : HttpMessageHandler {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
            var url = request.RequestUri!.ToString();
            if (url == indexUrl) {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(indexYaml) });
            }

            if (archives.TryGetValue(url, out var bytes)) {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    // ── NSFW propagation ──────────────────────────────────────────────

    [Fact]
    public async Task NsfwProposalMarksRootCreditsStudioAndTagsNsfw() {
        await using var db = CreateContext();
        var entityId = Guid.NewGuid();
        db.Entities.Add(new Prismedia.Infrastructure.Persistence.Entities.EntityRow {
            Id = entityId,
            KindCode = "video",
            Title = "Clean Title",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var patch = new EntityMetadataPatch(
            "Adult Scene", null,
            new Dictionary<string, string>(), [], ["Hardcore"], "Adult Studio",
            [new CreditPatch("Alice", "performer", null, 0)],
            new Dictionary<string, string>(), new Dictionary<string, int>(), new Dictionary<string, int>(), null) {
            Flags = new EntityMetadataFlagsPatch(null, true, null)
        };
        var proposal = new EntityMetadataProposal(
            "stash-x:1", "Stash X", "video", 0.9m, "Matched by URL", patch, [], [], [], entityId, []);

        var apply = new EntityMetadataApplyService(db, new PluginArtworkServiceOptions(Path.GetTempPath()));
        var ok = await apply.ApplyAsync(entityId, proposal, ["title", "credits", "studio", "tags"], null, CancellationToken.None);

        Assert.True(ok);
        Assert.True(db.Entities.Single(row => row.Id == entityId).IsNsfw);
        Assert.True(db.Entities.Single(row => row.KindCode == "person" && row.Title == "Alice").IsNsfw);
        Assert.True(db.Entities.Single(row => row.KindCode == "studio" && row.Title == "Adult Studio").IsNsfw);
        Assert.True(db.Entities.Single(row => row.KindCode == "tag" && row.Title == "Hardcore").IsNsfw);
    }

    private static PrismediaDbContext CreateContext() {
        var options = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<PrismediaDbContext>()
            .UseInMemoryDatabase($"stash-compat-{Guid.NewGuid():N}")
            .Options;
        return new PrismediaDbContext(options);
    }

    // ── python script actions ─────────────────────────────────────────

    [Fact]
    public async Task ScriptExecutorParsesSceneJsonFromStdout() {
        const string sceneJson = """
            {"title":"Scripted Scene","date":"2022-01-02","studio":{"name":"Py Studio"},
             "performers":[{"name":"Carol"},{"name":"Dave"}],"tags":[{"name":"T"}],
             "url":"https://py.example/s/9"}
            """;
        var executor = new StashScriptExecutor(new StubProcessExecutor(0, sceneJson));
        var action = StashAction.FromNode(StashYamlNode.Parse("""
            action: script
            script: ["python3", "scraper.py"]
            """))!;

        var scene = await executor.ScrapeSceneAsync("/tmp/site/Foo.yml", action, new StashScrapeInput(Url: "https://py.example/s/9"), CancellationToken.None);

        Assert.NotNull(scene);
        Assert.Equal("Scripted Scene", scene!.Title);
        Assert.Equal("Py Studio", scene.Studio?.Name);
        Assert.Equal(["Carol", "Dave"], scene.Performers);
    }

    [Fact]
    public async Task ScriptExecutorTreatsExitCode69AsNoResult() {
        var executor = new StashScriptExecutor(new StubProcessExecutor(69, "null"));
        var action = StashAction.FromNode(StashYamlNode.Parse("""
            action: script
            script: ["python3", "scraper.py"]
            """))!;

        var scene = await executor.ScrapeSceneAsync("/tmp/site/Foo.yml", action, new StashScrapeInput(Url: "x"), CancellationToken.None);

        Assert.Null(scene);
    }

    [Fact]
    public async Task RunnerReportsPythonUnavailableWhenNoExecutor() {
        const string scriptYaml = """
            name: Script Site
            sceneByURL:
              action: script
              script: ["python3", "scraper.py"]
            """;
        var yamlPath = Path.Combine(Path.GetTempPath(), $"stash-script-{Guid.NewGuid():N}.yml");
        await File.WriteAllTextAsync(yamlPath, scriptYaml);
        try {
            var manifest = StashScraperManifestFactory.TryCreate(scriptYaml, yamlPath)!;
            var descriptor = new PluginDescriptor(manifest, yamlPath, Path.GetDirectoryName(yamlPath)!, yamlPath);
            var runner = new StashCompatRunner(new HttpClient(new FixedHtmlHandler("")));
            var request = new IdentifyPluginRequest(
                ProtocolVersion: 2,
                Action: "lookup-url",
                Auth: new Dictionary<string, string>(),
                Entity: new IdentifyEntitySnapshot(Guid.NewGuid(), "video", "X"),
                Query: new IdentifyQuery(null, "https://script.example/s/1", null),
                Hints: new IdentifyMatchHints(new Dictionary<string, string>(), [], null, null));

            var response = await runner.IdentifyAsync(descriptor, request, CancellationToken.None);

            Assert.False(response.Ok);
            Assert.Contains("python", response.Error, StringComparison.OrdinalIgnoreCase);
        } finally {
            File.Delete(yamlPath);
        }
    }

    private sealed class StubProcessExecutor(int exitCode, string stdout) : Prismedia.Infrastructure.Processes.ProcessExecutor {
        public override Task<Prismedia.Infrastructure.Processes.ProcessExecutionResult> RunWithStdinAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            string standardInput,
            IReadOnlyDictionary<string, string>? environment,
            string? workingDirectory,
            CancellationToken cancellationToken) =>
            Task.FromResult(new Prismedia.Infrastructure.Processes.ProcessExecutionResult(exitCode, stdout, string.Empty));
    }

    private sealed class FixedHtmlHandler(string html) : HttpMessageHandler {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) {
                Content = new StringContent(html)
            });
    }
}
