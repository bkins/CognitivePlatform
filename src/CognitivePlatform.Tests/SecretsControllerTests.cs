using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CognitivePlatform.Api.Contracts;
using CognitivePlatform.Api.Controllers;
using CognitivePlatform.Api.Data;
using CognitivePlatform.Api.Domains.Journal;
using CognitivePlatform.Api.Domains.Journal.Interfaces;
using CognitivePlatform.Api.Domains.Secrets;
using CognitivePlatform.Api.Domains.Tasks;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace CognitivePlatform.Tests;

public sealed class SecretsControllerTests
{
    private readonly Mock<ISecretVaultService> _vaultMock   = new();
    private readonly Mock<IObjectStore>        _storeMock   = new();
    private readonly Mock<IJournalService>     _journalMock = new();
    private readonly Mock<ITaskService>        _taskMock    = new();
    private readonly SecretsController         _controller;

    public SecretsControllerTests()
    {
        _controller = new SecretsController(
            _vaultMock.Object
          , _storeMock.Object
          , _journalMock.Object
          , _taskMock.Object);
    }

    [Fact]
    public async Task Setup_ReturnsBadRequest_WhenPinIsEmpty()
    {
        var result = await _controller.Setup(new VaultPinRequest(string.Empty));

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("PIN is required.", badRequest.Value);
    }

    [Fact]
    public async Task Setup_ReturnsOk_WhenSetupSucceeds()
    {
        _vaultMock.Setup(vault => vault.SetupAsync("1234"))
                  .ReturnsAsync(true);

        var result = await _controller.Setup(new VaultPinRequest("1234"));

        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(okResult.Value);
    }

    [Fact]
    public async Task Unlock_ReturnsBadRequest_WhenPinIsIncorrect()
    {
        _vaultMock.Setup(vault => vault.UnlockAsync("9999"))
                  .ReturnsAsync(false);

        var result = await _controller.Unlock(new VaultPinRequest("9999"));

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("Incorrect PIN.", badRequest.Value);
    }

    [Fact]
    public async Task Unlock_ReturnsOk_WhenPinIsCorrect()
    {
        _vaultMock.Setup(vault => vault.UnlockAsync("1234"))
                  .ReturnsAsync(true);

        var result = await _controller.Unlock(new VaultPinRequest("1234"));

        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(okResult.Value);
    }

    [Fact]
    public void Lock_CallsVaultLock_AndReturnsOk()
    {
        var result = _controller.Lock();

        _vaultMock.Verify(vault => vault.Lock(), Times.Once);
        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public void GetStatus_ReturnsInitializedAndUnlockedState()
    {
        _vaultMock.Setup(vault => vault.IsInitialized()).Returns(true);
        _vaultMock.Setup(vault => vault.IsUnlocked()).Returns(true);

        var result = _controller.GetStatus();

        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(okResult.Value);
    }

    [Fact]
    public async Task ArchiveInboxItem_ReturnsVaultUnlockRequired_WhenVaultIsLocked()
    {
        _vaultMock.Setup(vault => vault.IsUnlocked()).Returns(false);

        var request = new ArchiveInboxItemRequest(Guid.NewGuid(), "Task");
        var result = await _controller.ArchiveInboxItem(request, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<ConverseResponse>(okResult.Value);
        Assert.False(response.Success);
        Assert.True(response.IsVaultUnlockRequired);
    }

    [Fact]
    public async Task ArchiveInboxItem_EncryptsAndSavesJournalEntry_WhenUnlocked()
    {
        var itemId = Guid.NewGuid();
        var itemIdString = itemId.ToString("N");
        var entry = new JournalEntry { Id = itemIdString };
        var revision = new JournalRevision
                       {
                           RevisionId = Guid.NewGuid().ToString("N")
                         , EntryId    = itemIdString
                         , Text       = "Confidential medical record note"
                         , CreatedUtc = DateTimeOffset.UtcNow
                       };

        _vaultMock.Setup(vault => vault.IsUnlocked()).Returns(true);
        _vaultMock.Setup(vault => vault.EncryptAsync("Confidential medical record note"))
                  .ReturnsAsync(("encrypted_payload", "nonce_123", "tag_456"));

        _journalMock.Setup(journal => journal.GetEntry(itemIdString))
                    .Returns(entry);
        _journalMock.Setup(journal => journal.GetRevisionHistory(itemIdString))
                    .Returns(new List<JournalRevision> { revision });

        _storeMock.Setup(store => store.Save(It.IsAny<SecretEntry>(), It.IsAny<string>(), It.IsAny<string>()))
                  .ReturnsAsync("saved_id");

        var request = new ArchiveInboxItemRequest(itemId, "Journal");
        var result = await _controller.ArchiveInboxItem(request, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<ConverseResponse>(okResult.Value);
        Assert.True(response.Success);
        _journalMock.Verify(journal => journal.DeleteEntry(itemIdString, "Archived to Secrets Vault"), Times.Once);
        _storeMock.Verify(store => store.Save(It.Is<SecretEntry>(secret => secret.Category == "Journal" && secret.EncryptedPayload == "encrypted_payload"), It.IsAny<string>(), It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task ArchiveInboxItem_EncryptsAndSavesTask_WhenUnlocked()
    {
        var itemId = Guid.NewGuid();
        var itemIdString = itemId.ToString("N");
        var task = new TaskItem
                   {
                       Id               = itemIdString
                     , ShortDescription = "Pay property taxes"
                     , Details          = "Account: 9988-77"
                   };

        _vaultMock.Setup(vault => vault.IsUnlocked()).Returns(true);
        _vaultMock.Setup(vault => vault.EncryptAsync(It.IsAny<string>()))
                  .ReturnsAsync(("encrypted_task_payload", "nonce_789", "tag_012"));

        _taskMock.Setup(service => service.Get(itemIdString))
                 .Returns(task);

        _storeMock.Setup(store => store.Save(It.IsAny<SecretEntry>(), It.IsAny<string>(), It.IsAny<string>()))
                  .ReturnsAsync("saved_id");

        var request = new ArchiveInboxItemRequest(itemId, "Task");
        var result = await _controller.ArchiveInboxItem(request, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<ConverseResponse>(okResult.Value);
        Assert.True(response.Success);
        _taskMock.Verify(service => service.Delete(itemIdString), Times.Once);
        _storeMock.Verify(store => store.Save(It.Is<SecretEntry>(secret => secret.Category == "Task" && secret.Title == "Pay property taxes"), It.IsAny<string>(), It.IsAny<string>()), Times.Once);
    }
}
