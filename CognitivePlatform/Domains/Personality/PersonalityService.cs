using CognitivePlatform.Api.Data;

namespace CognitivePlatform.Api.Domains.Personality;

public class PersonalityService : IPersonalityService
{
    private readonly IObjectStore _store;

    // Fixed IDs for the 6 built-in personalities so seeding is idempotent.
    private static class BuiltInIds
    {
        public const string FriendlyHelper     = "3f7a1a2b-0001-4000-8000-000000000001";
        public const string Programmer         = "3f7a1a2b-0002-4000-8000-000000000002";
        public const string Witty              = "3f7a1a2b-0003-4000-8000-000000000003";
        public const string Zen                = "3f7a1a2b-0004-4000-8000-000000000004";
        public const string TechExpert         = "3f7a1a2b-0005-4000-8000-000000000005";
        public const string MotivationalCoach  = "3f7a1a2b-0006-4000-8000-000000000006";
    }

    public PersonalityService(IObjectStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public async Task<IReadOnlyList<PersonalityDefinition>> GetAllAsync()
    {
        await EnsureSeededAsync().ConfigureAwait(false);

        return _store.List<PersonalityDefinition>()
                     .Where(personality => personality.IsDeleted == false)
                     .ToList();
    }

    public async Task<PersonalityDefinition?> GetActiveAsync()
    {
        await EnsureSeededAsync().ConfigureAwait(false);

        return _store.List<PersonalityDefinition>()
                     .Where(personality => personality.IsDeleted == false)
                     .FirstOrDefault(personality => personality.IsActive);
    }

    public async Task<PersonalityDefinition> SetActiveAsync(string personalityId)
    {
        await EnsureSeededAsync().ConfigureAwait(false);

        var all    = _store.List<PersonalityDefinition>();
        var target = all.FirstOrDefault(personality => personality.Id == personalityId
                                                    && personality.IsDeleted == false);

        if (target is null)
            throw new KeyNotFoundException($"Personality '{personalityId}' not found.");

        var saveTasks = new List<Task>();

        foreach (var personality in all.Where(personality => personality.IsActive
                                                          && personality.Id != personalityId))
        {
            personality.IsActive  = false;
            personality.UpdatedAt = DateTimeOffset.UtcNow;
            saveTasks.Add(_store.Save(personality, id: personality.Id));
        }

        target.IsActive  = true;
        target.UpdatedAt = DateTimeOffset.UtcNow;
        saveTasks.Add(_store.Save(target, id: target.Id));

        await Task.WhenAll(saveTasks).ConfigureAwait(false);

        return target;
    }

    public async Task<PersonalityDefinition> AddAsync(PersonalityDefinition personality)
    {
        if (personality is null) throw new ArgumentNullException(nameof(personality));

        if (string.IsNullOrWhiteSpace(personality.Id))
            personality.Id = Guid.NewGuid().ToString("N");

        var now = DateTimeOffset.UtcNow;
        personality.IsBuiltIn  = false;
        personality.IsDeleted  = false;
        personality.IsActive   = false; // new personalities are always inactive; use SetActiveAsync to activate
        personality.CreatedAt  = now;
        personality.UpdatedAt  = now;

        await _store.Save(personality, id: personality.Id).ConfigureAwait(false);

        return personality;
    }

    // --- Private helpers --------------------------------------------------------

    private async Task EnsureSeededAsync()
    {
        var existing = _store.List<PersonalityDefinition>();

        // If any old built-in IDs are present, soft delete them so they migrate cleanly
        var hasOldIds = existing.Any(personality => personality.IsBuiltIn && personality.Id.StartsWith("00000000"));
        if (hasOldIds)
        {
            foreach (var old in existing.Where(p => p.IsBuiltIn && p.Id.StartsWith("00000000")))
            {
                await _store.SoftDeleteAsync<PersonalityDefinition>(old.Id).ConfigureAwait(false);
            }
            existing = _store.List<PersonalityDefinition>();
        }

        if (existing.Any(personality => personality.IsBuiltIn))
            return;

        foreach (var builtIn in BuildBuiltIns())
            await _store.Save(builtIn, id: builtIn.Id).ConfigureAwait(false);
    }

    private static IReadOnlyList<PersonalityDefinition> BuildBuiltIns()
    {
        var now = DateTimeOffset.UtcNow;

        return new List<PersonalityDefinition>
               {
                   new PersonalityDefinition
                   {
                           Id           = BuiltInIds.FriendlyHelper
                         , Name         = "Friendly Helper"
                         , Description  = "Kind, casual, and helpful"
                         , SystemPrompt = "You're a helpful assistant. Be friendly and warm."
                         , IsBuiltIn    = true
                         , IsActive     = true
                         , CreatedAt    = now
                         , UpdatedAt    = now
                   }
                 , new PersonalityDefinition
                   {
                           Id           = BuiltInIds.Programmer
                         , Name         = "Programmer"
                         , Description  = "Smart, concise, accurate technical coding help"
                         , SystemPrompt = "You are a helpful and expert AI coding assistant with deep knowledge of C#, .NET, and modern software development. "
                                        + "Respond with complete code, brief explanations, and follow good architecture and performance practices."
                         , IsBuiltIn    = true
                         , ModelConfig  = new PersonalityModelConfig
                                          {
                                                  Temperature = 0.2f
                                          }
                         , CreatedAt    = now
                         , UpdatedAt    = now
                   }
                 , new PersonalityDefinition
                   {
                           Id           = BuiltInIds.Witty
                         , Name         = "Witty"
                         , Description  = "Sarcastic tech genius who jokes around"
                         , SystemPrompt = "You are a witty, sarcastic assistant who knows a lot but likes to joke around. "
                                        + "Be helpful, but never miss a chance for a pun."
                         , IsBuiltIn    = true
                         , CreatedAt    = now
                         , UpdatedAt    = now
                   }
                 , new PersonalityDefinition
                   {
                           Id           = BuiltInIds.Zen
                         , Name         = "Zen"
                         , Description  = "Mindful and calm, like a wise teacher"
                         , SystemPrompt = "You are a calm, thoughtful assistant who answers gently, like a wise teacher. "
                                        + "Keep responses short, kind, and reflective."
                         , IsBuiltIn    = true
                         , CreatedAt    = now
                         , UpdatedAt    = now
                   }
                 , new PersonalityDefinition
                   {
                           Id           = BuiltInIds.TechExpert
                         , Name         = "Tech Expert"
                         , Description  = "Analytical and precise technical guidance"
                         , SystemPrompt = "You're a technical expert. Be concise, accurate, and professional."
                         , IsBuiltIn    = true
                         , CreatedAt    = now
                         , UpdatedAt    = now
                   }
                 , new PersonalityDefinition
                   {
                           Id           = BuiltInIds.MotivationalCoach
                         , Name         = "Motivational Coach"
                         , Description  = "Uplifting and motivational"
                         , SystemPrompt = "You're a motivational coach. Be encouraging, positive, and supportive."
                         , IsBuiltIn    = true
                         , CreatedAt    = now
                         , UpdatedAt    = now
                   }
               };
    }
}
