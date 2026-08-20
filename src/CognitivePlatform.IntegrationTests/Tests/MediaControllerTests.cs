using System.Net;
using System.Net.Http.Headers;
using CognitivePlatform.Api.Domains.Media;
using CognitivePlatform.IntegrationTests.Infrastructure;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace CognitivePlatform.IntegrationTests.Tests;

[Trait("Category", "Integration")]
public sealed class MediaControllerTests : IDisposable
{
    private readonly ApiFixture _fixture;

    public MediaControllerTests(ITestOutputHelper output)
    {
        _fixture = new ApiFixture(output);
    }

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public async Task Media_Upload_List_GetById_Download_Delete_RoundTrip()
    {
        var ownerId   = Guid.NewGuid();
        var ownerType = "JournalEntry";

        _fixture.Log($"Act — POST /api/media/{ownerType}/{ownerId} with multipart file");
        using var content = new MultipartFormDataContent();
        var fileContent   = new ByteArrayContent("Test image bytes content"u8.ToArray());
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        content.Add(fileContent, "file", "test_note.txt");

        var uploadResponse = await _fixture.Client.PostAsync($"/api/media/{ownerType}/{ownerId}", content);
        uploadResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var attachment = await _fixture.ReadJsonAsync<MediaAttachmentDto>(uploadResponse);
        attachment.Should().NotBeNull();
        attachment!.FileName.Should().Be("test_note.txt");
        var attachmentId = attachment.Id.ToString("N");

        _fixture.Log($"Act — GET /api/media/{ownerType}/{ownerId}");
        var listResponse = await _fixture.Client.GetAsync($"/api/media/{ownerType}/{ownerId}");
        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var list = await _fixture.ReadJsonAsync<List<MediaAttachmentDto>>(listResponse);
        list.Should().NotBeNull();
        list!.Should().Contain(item => item.Id == attachment.Id);

        _fixture.Log($"Act — GET /api/media/{attachmentId}");
        var getResponse = await _fixture.Client.GetAsync($"/api/media/{attachmentId}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        _fixture.Log($"Act — GET /api/media/{attachmentId}/file");
        var downloadResponse = await _fixture.Client.GetAsync($"/api/media/{attachmentId}/file");
        downloadResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var downloadedText = await downloadResponse.Content.ReadAsStringAsync();
        downloadedText.Should().Be("Test image bytes content");

        _fixture.Log($"Act — DELETE /api/media/{attachmentId}");
        var deleteResponse = await _fixture.Client.DeleteAsync($"/api/media/{attachmentId}");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        _fixture.Log($"Verify — GET /api/media/{attachmentId} returns 404");
        var getAfterDelete = await _fixture.Client.GetAsync($"/api/media/{attachmentId}");
        getAfterDelete.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
