namespace CognitivePlatform.Admin.Services;

public sealed record BacklogStoryResult(
    bool    IsSuccess
  , string? StoryId
  , string? ErrorMessage)
{
    public static BacklogStoryResult Success(string storyId)
    {
        return new BacklogStoryResult(true, storyId, null);
    }

    public static BacklogStoryResult Failure(string errorMessage)
    {
        return new BacklogStoryResult(false, null, errorMessage);
    }
}
