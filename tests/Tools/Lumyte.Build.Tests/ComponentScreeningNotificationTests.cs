using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace Lumyte.Build.Tests;

/// <summary>Checks trusted notification metadata without downloading or executing PR artifacts.</summary>
public sealed class ComponentScreeningNotificationTests
{
    /// <summary>Posts matching final heads even after their PR is merged.</summary>
    /// <returns>The asynchronous assertion.</returns>
    [Fact]
    public async Task MatchingMergedHeadPostsLinkAsync()
    {
        var fixture = new NotificationFixture();
        Assert.Equal("published", await fixture.PublishAsync());
        (string Path, string Method, string Body) write = Assert.Single(fixture.Writes);
        Assert.Equal("POST", write.Method);
        Assert.Contains("actions/runs/123", write.Body, StringComparison.Ordinal);
        Assert.Contains(new string('a', 40), write.Body, StringComparison.Ordinal);
    }

    /// <summary>Rejects results for superseded commits before posting.</summary>
    /// <returns>The asynchronous assertion.</returns>
    [Fact]
    public async Task ObsoleteHeadNeverPostsAsync()
    {
        var fixture = new NotificationFixture { FirstHead = new string('b', 40) };
        Assert.Equal("obsolete head", await fixture.PublishAsync());
        Assert.Empty(fixture.Writes);
    }

    /// <summary>Rechecks the head immediately before updating the comment.</summary>
    /// <returns>The asynchronous assertion.</returns>
    [Fact]
    public async Task HeadMovingDuringLookupNeverPostsAsync()
    {
        var fixture = new NotificationFixture { FinalHead = new string('b', 40) };
        Assert.Equal("obsolete head", await fixture.PublishAsync());
        Assert.Empty(fixture.Writes);
    }

    /// <summary>Keeps newer runs and newer attempts of the same run.</summary>
    /// <returns>The asynchronous assertion.</returns>
    [Fact]
    public async Task NewerResultsAreNotOverwrittenAsync()
    {
        var fixture = new NotificationFixture();
        fixture.AddComment(6, "github-actions[bot]", ComponentScreeningNotification.Marker + "\n<!-- run: 11 attempt: 1 -->");
        Assert.Equal("newer result already published", await fixture.PublishAsync());
        Assert.Empty(fixture.Writes);
        Assert.False(ComponentScreeningNotification.ShouldReplace("<!-- run: 10 attempt: 2 -->", fixture.RunElement));
    }

    /// <summary>Only modifies comments owned by the Actions bot.</summary>
    /// <returns>The asynchronous assertion.</returns>
    [Fact]
    public async Task UpdatesOnlyBotOwnedCommentAsync()
    {
        var fixture = new NotificationFixture();
        fixture.AddComment(5, "human", ComponentScreeningNotification.Marker);
        fixture.AddComment(6, "github-actions[bot]", ComponentScreeningNotification.Marker + "\n<!-- run: 9 attempt: 1 -->");
        Assert.Equal("published", await fixture.PublishAsync());
        (string Path, string Method, string Body) write = Assert.Single(fixture.Writes);
        Assert.Equal("repos/owner/repo/issues/comments/6", write.Path);
        Assert.Equal("PATCH", write.Method);
    }

    /// <summary>Does not infer a destination when association metadata is missing.</summary>
    /// <returns>The asynchronous assertion.</returns>
    [Fact]
    public async Task MissingAssociationDoesNotGuessAsync()
    {
        var fixture = new NotificationFixture();
        fixture.Run["pull_requests"] = new JsonArray();
        Assert.Contains("missing", await fixture.PublishAsync(), StringComparison.Ordinal);
        Assert.Empty(fixture.Writes);
    }

    /// <summary>Ignores completion events from other workflows.</summary>
    /// <returns>The asynchronous assertion.</returns>
    [Fact]
    public async Task OtherWorkflowDoesNotCallApiAsync()
    {
        var fixture = new NotificationFixture();
        fixture.Run["path"] = ".github/workflows/other.yml";
        Assert.Equal("ignored workflow", await fixture.PublishAsync());
        Assert.Equal(0, fixture.RequestCount);
    }

    /// <summary>Rejects cross-repository result metadata.</summary>
    /// <returns>The asynchronous assertion.</returns>
    [Fact]
    public async Task RepositoryMismatchFailsClosedAsync()
    {
        var fixture = new NotificationFixture();
        fixture.Run["repository"]!["id"] = 43;
        await Assert.ThrowsAsync<InvalidOperationException>(fixture.PublishAsync);
        Assert.Empty(fixture.Writes);
    }

    /// <summary>Does not describe cancellation, failure, timeout or missing outcomes as success.</summary>
    /// <param name="conclusion">The GitHub completion conclusion.</param>
    [Theory]
    [InlineData("failure")]
    [InlineData("timed_out")]
    [InlineData("cancelled")]
    [InlineData("skipped")]
    [InlineData(null)]
    public void UnsuccessfulConclusionsAreNotClean(string? conclusion)
    {
        var fixture = new NotificationFixture();
        fixture.Run["conclusion"] = conclusion;
        Assert.DoesNotContain("測定処理が完了しました", ComponentScreeningNotification.CreateBody(fixture.RunElement, "owner/repo"), StringComparison.Ordinal);
    }

    /// <summary>Validates full identities rather than accepting substring or newline matches.</summary>
    [Fact]
    public void InvalidIdentityIsRejected()
    {
        var fixture = new NotificationFixture();
        fixture.Run["head_sha"] = new string('a', 40) + "\n";
        Assert.Throws<InvalidOperationException>(() => ComponentScreeningNotification.CreateBody(fixture.RunElement, "owner/repo"));
        fixture.Run["head_sha"] = new string('a', 40);
        Assert.Throws<InvalidOperationException>(() => ComponentScreeningNotification.CreateBody(fixture.RunElement, "owner/repo\n"));
    }

    /// <summary>The Fallout entry point can reject unrelated events without a network call.</summary>
    /// <returns>The asynchronous real-target assertion.</returns>
    [Fact]
    public async Task FalloutTargetIgnoresOtherWorkflowAsync()
    {
        using var fixture = new BuildFixture();
        string eventPath = Path.Combine(fixture.Root, "notification event.json");
        File.WriteAllText(eventPath, """{"workflow_run":{"event":"push","path":"other"}}""");
        fixture.Environment["GITHUB_EVENT_PATH"] = eventPath;
        fixture.Environment["GITHUB_REPOSITORY"] = "owner/repo";
        fixture.Environment["GH_TOKEN"] = "unused";
        BuildFixture.ProcessResult result = await fixture.RunAsync("ComponentScreeningNotify");
        Assert.True(result.ExitCode == 0, result.Output + result.Error);
        Assert.Contains("ignored workflow", result.Output, StringComparison.Ordinal);
    }

    private sealed class NotificationFixture
    {
        private int _pullReads;

        public NotificationFixture()
        {
            Run = new JsonObject
            {
                ["event"] = "pull_request",
                ["path"] = ComponentScreeningNotification.WorkflowPath,
                ["repository"] = new JsonObject { ["id"] = 42 },
                ["pull_requests"] = new JsonArray(new JsonObject { ["number"] = 7 }),
                ["id"] = 123,
                ["run_number"] = 10,
                ["run_attempt"] = 1,
                ["head_sha"] = new string('a', 40),
                ["conclusion"] = "success",
            };
        }

        public JsonObject Run { get; }

        public JsonElement RunElement => JsonSerializer.SerializeToElement(Run);

        public string FirstHead { get; set; } = new('a', 40);

        public string FinalHead { get; set; } = new('a', 40);

        public int RequestCount { get; private set; }

        public List<(string Path, string Method, string Body)> Writes { get; } = [];

        private JsonArray Comments { get; } = [];

        public void AddComment(long id, string login, string body) => Comments.Add(new JsonObject
        {
            ["id"] = id,
            ["user"] = new JsonObject { ["login"] = login },
            ["body"] = body,
        });

        public Task<string> PublishAsync() => ComponentScreeningNotification.PublishAsync(
            JsonSerializer.SerializeToElement(new { workflow_run = Run }), "owner/repo", RequestAsync);

        private Task<JsonElement> RequestAsync(string path, string method, string? payload)
        {
            RequestCount++;
            if (method != "GET")
            {
                using var document = JsonDocument.Parse(payload!);
                Writes.Add((path, method, document.RootElement.GetProperty("body").GetString()!));
                return Task.FromResult(JsonSerializer.SerializeToElement(new { id = 90 }));
            }

            if (path == "repos/owner/repo")
            {
                return Task.FromResult(JsonSerializer.SerializeToElement(new { id = 42 }));
            }

            if (path == "repos/owner/repo/pulls/7")
            {
                string sha = _pullReads++ == 0 ? FirstHead : FinalHead;
                return Task.FromResult(JsonSerializer.SerializeToElement(new { head = new { sha }, state = "closed", merged = true }));
            }

            Assert.Contains("/comments?", path, StringComparison.Ordinal);
            return Task.FromResult(JsonSerializer.SerializeToElement(Comments));
        }
    }
}
