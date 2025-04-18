using System.Net;
using PackForge.Core.Service;

namespace PackForgeUnitTests;

public class MavenServiceTests
{
    private class FakeHttpMessageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            handler(request);
    }

    private static HttpClient CreateHttpClient(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler) => new(new FakeHttpMessageHandler(handler));

    private class TempDirectory : IDisposable
    {
        public string DirectoryPath { get; }
        public TempDirectory()
        {
            DirectoryPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(DirectoryPath);
        }
        public void Dispose()
        {
            if (Directory.Exists(DirectoryPath))
                Directory.Delete(DirectoryPath, true);
        }
    }

    [Fact]
    public async Task FetchAvailableVersions_UnknownLoader_ReturnsEmptyList()
    {
        List<string> result = await MavenService.FetchAvailableVersions("Unknown", "1.20");
        Assert.Empty(result);
    }

    [Fact]
    public async Task FetchAvailableVersions_Forge_ReturnsExpectedVersions()
    {
        const string fakeXml = """
            <metadata>
              <versioning>
                <versions>
                  <version>1.20-1</version>
                  <version>1.20-2</version>
                  <version>1.21-1</version>
                </versions>
              </versioning>
            </metadata>
            """;

        MavenService.HttpClient = CreateHttpClient(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(fakeXml)
        }));

        List<string> result = await MavenService.FetchAvailableVersions("Forge", "1.20");
        Assert.Equal(["1.20-1", "1.20-2"], result);
    }

    [Fact]
    public async Task FetchAvailableVersions_NeoForge_ReturnsExpectedVersions()
    {
        const string fakeXml = """
            <metadata>
              <versioning>
                <versions>
                  <version>20.2.1</version>
                  <version>20.2.2</version>
                  <version>20.3.0</version>
                </versions>
              </versioning>
            </metadata>
            """;
        MavenService.HttpClient = CreateHttpClient(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(fakeXml)
        }));

        List<string> result = await MavenService.FetchAvailableVersions("NeoForge", "1.20.2");
        Assert.Equal(["20.2.1", "20.2.2"], result);
    }

    [Fact]
    public async Task FetchAvailableVersions_HttpException_ReturnsEmptyList()
    {
        MavenService.HttpClient = CreateHttpClient(_ => throw new HttpRequestException("Network error"));

        List<string> result = await MavenService.FetchAvailableVersions("Forge", "1.20");
        Assert.Empty(result);
    }

    [Fact]
    public async Task DownloadLoader_InvalidParameters_DoesNothing()
    {
        string tempDir = Path.GetTempPath();
        await MavenService.DownloadLoader(null, "1.0", tempDir);
        await MavenService.DownloadLoader("Forge", null, tempDir);
        await MavenService.DownloadLoader("Forge", "1.0", tempDir);
        await MavenService.DownloadLoader("Forge", "1.0", "NonExistingDirectoryPath");
    }

    [Fact]
    public async Task DownloadLoader_Forge_Success()
    {
        using TempDirectory tempDir = new();
        const string loaderType = "Forge";
        const string loaderVersion = "1.20-1";
        const string loaderName = $"{loaderType}-{loaderVersion}";
        string expectedFileName = Path.Combine(tempDir.DirectoryPath, $"{loaderName}-installer.jar");
        byte[] fakeData = [1, 2, 3, 4];

        MavenService.HttpClient = CreateHttpClient(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(fakeData)
        }));

        await MavenService.DownloadLoader(loaderType, loaderVersion, tempDir.DirectoryPath);
        Assert.True(File.Exists(expectedFileName));
        Assert.Equal(fakeData, await File.ReadAllBytesAsync(expectedFileName));
    }

    [Fact]
    public async Task DownloadLoader_NeoForge_Success()
    {
        using TempDirectory tempDir = new();
        const string loaderType = "NeoForge";
        const string loaderVersion = "1.0.0";
        const string loaderName = $"{loaderType}-{loaderVersion}";
        string expectedFileName = Path.Combine(tempDir.DirectoryPath, $"{loaderName}-installer.jar");
        byte[] fakeData = [5, 6, 7, 8];

        MavenService.HttpClient = CreateHttpClient(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(fakeData)
        }));

        await MavenService.DownloadLoader(loaderType, loaderVersion, tempDir.DirectoryPath);
        Assert.True(File.Exists(expectedFileName));
        Assert.Equal(fakeData, await File.ReadAllBytesAsync(expectedFileName));
    }

    [Fact]
    public async Task DownloadLoader_HttpException_NoFileCreated()
    {
        using TempDirectory tempDir = new();
        const string loaderType = "Forge";
        const string loaderVersion = "1.20-1";
        const string loaderName = $"{loaderType}-{loaderVersion}";
        string expectedFileName = Path.Combine(tempDir.DirectoryPath, $"{loaderName}-installer.jar");

        MavenService.HttpClient = CreateHttpClient(_ => throw new HttpRequestException("Download failed"));

        await MavenService.DownloadLoader(loaderType, loaderVersion, tempDir.DirectoryPath);
        Assert.False(File.Exists(expectedFileName));
    }
}
