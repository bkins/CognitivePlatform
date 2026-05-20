using Moq;
using CognitivePlatform.Api.Data;
using CognitivePlatform.Api.Domains.Personality;

namespace CognitivePlatform.Tests;

public class PersonalityServiceTests
{
    private readonly Mock<IObjectStore> _storeMock = new();
    private readonly PersonalityService _service;

    public PersonalityServiceTests()
    {
        _storeMock.Setup(store => store.Save(It.IsAny<PersonalityDefinition>()
                                           , It.IsAny<string?>()
                                           , It.IsAny<string?>()))
                  .ReturnsAsync(string.Empty);

        _service = new PersonalityService(_storeMock.Object);
    }

    // ================================================================
    // GetAllAsync — seeding
    // ================================================================

    [Fact]
    public async Task GetAllAsync_ReturnsBuiltIns_WhenStoreIsEmpty()
    {
        _storeMock.Setup(store => store.List<PersonalityDefinition>(It.IsAny<string?>()
                                                                   , It.IsAny<DateTimeOffset?>()
                                                                   , It.IsAny<DateTimeOffset?>()))
                  .Returns(new List<PersonalityDefinition>());

        // First call seeds; second call (inside GetAllAsync after seed) returns seeded list.
        // We make List return the seeded records on the second invocation by switching state.
        var seeded = new List<PersonalityDefinition>();

        _storeMock.Setup(store => store.Save(It.IsAny<PersonalityDefinition>()
                                           , It.IsAny<string?>()
                                           , It.IsAny<string?>()))
                  .Callback<PersonalityDefinition, string?, string?>((personality, _, _) => seeded.Add(personality))
                  .ReturnsAsync(string.Empty);

        var callCount = 0;
        _storeMock.Setup(store => store.List<PersonalityDefinition>(It.IsAny<string?>()
                                                                   , It.IsAny<DateTimeOffset?>()
                                                                   , It.IsAny<DateTimeOffset?>()))
                  .Returns(() =>
                  {
                      callCount++;
                      return callCount == 1 ? new List<PersonalityDefinition>() : seeded;
                  });

        var result = await _service.GetAllAsync();

        Assert.Equal(6, result.Count);
        Assert.All(result, personality => Assert.True(personality.IsBuiltIn));
    }

    [Fact]
    public async Task GetAllAsync_ReturnsBuiltInsAndUserDefined_WhenBothExist()
    {
        var userDefined = new PersonalityDefinition
                          {
                                  Id        = Guid.NewGuid().ToString("N")
                                , Name      = "My Custom"
                                , IsBuiltIn = false
                          };
        var builtIn = new PersonalityDefinition
                      {
                              Id        = "00000000000000000000000000000001"
                            , Name      = "Friendly Helper"
                            , IsBuiltIn = true
                      };

        _storeMock.Setup(store => store.List<PersonalityDefinition>(It.IsAny<string?>()
                                                                   , It.IsAny<DateTimeOffset?>()
                                                                   , It.IsAny<DateTimeOffset?>()))
                  .Returns(new List<PersonalityDefinition> { builtIn, userDefined });

        var result = await _service.GetAllAsync();

        Assert.Equal(2, result.Count);
        Assert.Contains(result, personality => personality.IsBuiltIn);
        Assert.Contains(result, personality => personality.IsBuiltIn == false);
    }

    [Fact]
    public async Task GetAllAsync_ExcludesDeleted_WhenDeletedPersonalityExists()
    {
        var active  = new PersonalityDefinition { Id = "00000000000000000000000000000001", Name = "Friendly Helper", IsBuiltIn = true };
        var deleted = new PersonalityDefinition { Id = Guid.NewGuid().ToString("N"), Name = "Old Custom", IsDeleted = true };

        _storeMock.Setup(store => store.List<PersonalityDefinition>(It.IsAny<string?>()
                                                                   , It.IsAny<DateTimeOffset?>()
                                                                   , It.IsAny<DateTimeOffset?>()))
                  .Returns(new List<PersonalityDefinition> { active, deleted });

        var result = await _service.GetAllAsync();

        Assert.Single(result);
        Assert.DoesNotContain(result, personality => personality.IsDeleted);
    }

    // ================================================================
    // GetActiveAsync
    // ================================================================

    [Fact]
    public async Task GetActiveAsync_ReturnsActivePersonality()
    {
        var active   = new PersonalityDefinition { Id = "00000000000000000000000000000001", Name = "Friendly Helper", IsBuiltIn = true, IsActive = true };
        var inactive = new PersonalityDefinition { Id = "00000000000000000000000000000002", Name = "Programmer",      IsBuiltIn = true, IsActive = false };

        _storeMock.Setup(store => store.List<PersonalityDefinition>(It.IsAny<string?>()
                                                                   , It.IsAny<DateTimeOffset?>()
                                                                   , It.IsAny<DateTimeOffset?>()))
                  .Returns(new List<PersonalityDefinition> { active, inactive });

        var result = await _service.GetActiveAsync();

        Assert.NotNull(result);
        Assert.Equal("Friendly Helper", result.Name);
        Assert.True(result.IsActive);
    }

    [Fact]
    public async Task GetActiveAsync_ReturnsNull_WhenNoPersonalityIsActive()
    {
        var personality = new PersonalityDefinition { Id = "00000000000000000000000000000001", Name = "Friendly Helper", IsBuiltIn = true, IsActive = false };

        _storeMock.Setup(store => store.List<PersonalityDefinition>(It.IsAny<string?>()
                                                                   , It.IsAny<DateTimeOffset?>()
                                                                   , It.IsAny<DateTimeOffset?>()))
                  .Returns(new List<PersonalityDefinition> { personality });

        var result = await _service.GetActiveAsync();

        Assert.Null(result);
    }

    // ================================================================
    // SetActiveAsync
    // ================================================================

    [Fact]
    public async Task SetActiveAsync_SetsTargetActiveAndDeactivatesPrevious()
    {
        var previous = new PersonalityDefinition { Id = "00000000000000000000000000000001", Name = "Friendly Helper", IsBuiltIn = true, IsActive = true };
        var target   = new PersonalityDefinition { Id = "00000000000000000000000000000002", Name = "Programmer",      IsBuiltIn = true, IsActive = false };

        _storeMock.Setup(store => store.List<PersonalityDefinition>(It.IsAny<string?>()
                                                                   , It.IsAny<DateTimeOffset?>()
                                                                   , It.IsAny<DateTimeOffset?>()))
                  .Returns(new List<PersonalityDefinition> { previous, target });

        var result = await _service.SetActiveAsync(target.Id);

        Assert.True(result.IsActive);
        Assert.Equal("Programmer", result.Name);
        Assert.False(previous.IsActive);
    }

    [Fact]
    public async Task SetActiveAsync_ThrowsKeyNotFoundException_WhenIdNotFound()
    {
        var personality = new PersonalityDefinition { Id = "00000000000000000000000000000001", Name = "Friendly Helper", IsBuiltIn = true };

        _storeMock.Setup(store => store.List<PersonalityDefinition>(It.IsAny<string?>()
                                                                   , It.IsAny<DateTimeOffset?>()
                                                                   , It.IsAny<DateTimeOffset?>()))
                  .Returns(new List<PersonalityDefinition> { personality });

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => _service.SetActiveAsync("nonexistent-id"));
    }

    [Fact]
    public async Task SetActiveAsync_PersistsChanges_ToObjectStore()
    {
        var previous = new PersonalityDefinition { Id = "00000000000000000000000000000001", Name = "Friendly Helper", IsBuiltIn = true, IsActive = true };
        var target   = new PersonalityDefinition { Id = "00000000000000000000000000000002", Name = "Programmer",      IsBuiltIn = true, IsActive = false };

        _storeMock.Setup(store => store.List<PersonalityDefinition>(It.IsAny<string?>()
                                                                   , It.IsAny<DateTimeOffset?>()
                                                                   , It.IsAny<DateTimeOffset?>()))
                  .Returns(new List<PersonalityDefinition> { previous, target });

        await _service.SetActiveAsync(target.Id);

        _storeMock.Verify(store => store.Save(It.IsAny<PersonalityDefinition>()
                                            , It.IsAny<string?>()
                                            , It.IsAny<string?>())
                        , Times.Exactly(2));
    }

    // ================================================================
    // AddAsync
    // ================================================================

    [Fact]
    public async Task AddAsync_ForcesIsActive_ToFalse_RegardlessOfInput()
    {
        var personality = new PersonalityDefinition
                          {
                                  Name     = "Eager Custom"
                                , IsActive = true  // caller tries to add as active
                          };

        _storeMock.Setup(store => store.List<PersonalityDefinition>(It.IsAny<string?>()
                                                                   , It.IsAny<DateTimeOffset?>()
                                                                   , It.IsAny<DateTimeOffset?>()))
                  .Returns(new List<PersonalityDefinition>
                           {
                               new PersonalityDefinition { Id = "00000000000000000000000000000001", IsBuiltIn = true, IsActive = true }
                           });

        var result = await _service.AddAsync(personality);

        Assert.False(result.IsActive);
    }

    [Fact]
    public async Task AddAsync_StoresNewUserDefinedPersonality()
    {
        var personality = new PersonalityDefinition
                          {
                                  Name         = "My Coach"
                                , Description  = "Personal productivity guide"
                                , SystemPrompt = "You are my personal productivity coach."
                          };

        _storeMock.Setup(store => store.List<PersonalityDefinition>(It.IsAny<string?>()
                                                                   , It.IsAny<DateTimeOffset?>()
                                                                   , It.IsAny<DateTimeOffset?>()))
                  .Returns(new List<PersonalityDefinition>
                           {
                               new PersonalityDefinition { Id = "00000000000000000000000000000001", IsBuiltIn = true }
                           });

        var result = await _service.AddAsync(personality);

        Assert.NotEmpty(result.Id);
        Assert.False(result.IsBuiltIn);
        Assert.False(result.IsDeleted);
        Assert.Equal("My Coach", result.Name);

        _storeMock.Verify(store => store.Save(It.Is<PersonalityDefinition>(saved => saved.Name == "My Coach")
                                            , It.IsAny<string?>()
                                            , It.IsAny<string?>())
                        , Times.Once);
    }

    [Fact]
    public async Task AddAsync_AssignsId_WhenIdIsEmpty()
    {
        var personality = new PersonalityDefinition { Name = "No Id Yet", Id = string.Empty };

        _storeMock.Setup(store => store.List<PersonalityDefinition>(It.IsAny<string?>()
                                                                   , It.IsAny<DateTimeOffset?>()
                                                                   , It.IsAny<DateTimeOffset?>()))
                  .Returns(new List<PersonalityDefinition>
                           {
                               new PersonalityDefinition { Id = "00000000000000000000000000000001", IsBuiltIn = true }
                           });

        var result = await _service.AddAsync(personality);

        Assert.NotEmpty(result.Id);
    }
}
