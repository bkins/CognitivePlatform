using CognitivePlatform.Api.Controllers.Admin;
using CognitivePlatform.Api.Data;
using CognitivePlatform.Api.KnowledgeInbox;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

namespace CognitivePlatform.Tests;

public sealed class AdminKnowledgeControllerTests : IDisposable
{
    private const string AdminSecret = "test-admin-secret";

    private readonly SqliteConnection         _persistentConnection;
    private readonly SqliteObjectStore        _store;
    private readonly AdminKnowledgeController _sut;

    public AdminKnowledgeControllerTests()
    {
        var databaseName     = Guid.NewGuid().ToString("N");
        var connectionString = $"Data Source=file:{databaseName}?mode=memory&cache=shared";

        _persistentConnection = new SqliteConnection(connectionString);
        _persistentConnection.Open();
        _store = new SqliteObjectStore(connectionString);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["AdminSettings:AdminSecret"] = AdminSecret })
            .Build();

        _sut = new AdminKnowledgeController(configuration, _store);
        _sut.ControllerContext = new ControllerContext { HttpContext = AuthorizedContext() };
    }

    public void Dispose() => _persistentConnection.Dispose();

    [Fact]
    public async Task Update_Returns400_WhenTitleIsBlank()
    {
        var result = await _sut.Update(Guid.NewGuid().ToString(), new UpdateKnowledgeRequest { Title = "  " });

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("Title is required.", badRequest.Value);
    }

    [Fact]
    public async Task Update_Returns404_WhenItemDoesNotExist()
    {
        var result = await _sut.Update(Guid.NewGuid().ToString(), new UpdateKnowledgeRequest { Title = "Correction" });

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Update_PreservesProtectedFields_AndRecordsCorrection()
    {
        var item = new KnowledgeItemDto
                   {
                       Id             = Guid.NewGuid()
                     , Kind           = KnowledgeKind.Conversation
                     , Title          = "Original"
                     , Summary        = "Original summary"
                     , Status         = KnowledgeStatus.Archived
                     , CreatedAt      = DateTimeOffset.UtcNow.AddDays(-1)
                     , LastModifiedAt = DateTimeOffset.UtcNow.AddHours(-1)
                     , Tags           = ["source"]
                     , Importance     = 8
                     , Urgency        = 4
                   };
        await _store.Save(item, id: item.IdString);

        var result = await _sut.Update(item.IdString, new UpdateKnowledgeRequest { Title = " Corrected ", Summary = " Updated summary " });

        Assert.IsType<OkObjectResult>(result);
        var saved = _store.Get<KnowledgeItemDto>(item.IdString);
        Assert.NotNull(saved);
        Assert.Equal("Corrected", saved.Title);
        Assert.Equal("Updated summary", saved.Summary);
        Assert.True(saved.IsEdited);
        Assert.True(saved.LastModifiedAt > item.LastModifiedAt);
        Assert.Equal(item.Id, saved.Id);
        Assert.Equal(item.Kind, saved.Kind);
        Assert.Equal(item.Status, saved.Status);
        Assert.Equal(item.CreatedAt, saved.CreatedAt);
        Assert.Equal(item.Tags, saved.Tags);
        Assert.Equal(item.Importance, saved.Importance);
        Assert.Equal(item.Urgency, saved.Urgency);
    }

    private static HttpContext AuthorizedContext()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Admin-Secret"] = AdminSecret;
        return context;
    }
}
