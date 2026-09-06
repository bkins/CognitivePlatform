namespace CognitivePlatform.Admin.Services;

public sealed record AddBacklogStoryRequest(
    string ProjectName
  , string Title
  , string Description
  , string Area
  , string Status);
