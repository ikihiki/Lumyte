using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Lumyte.Build;

internal static class ComponentScreeningNotification
{
    internal const string Marker = "<!-- lumyte-component-screening -->";
    internal const string WorkflowPath = ".github/workflows/component-screening.yml";

    internal static async Task<string> PublishFromEnvironmentAsync()
    {
        string eventPath = Environment.GetEnvironmentVariable("GITHUB_EVENT_PATH")
            ?? throw new InvalidOperationException("GITHUB_EVENT_PATH is required.");
        string repository = Environment.GetEnvironmentVariable("GITHUB_REPOSITORY")
            ?? throw new InvalidOperationException("GITHUB_REPOSITORY is required.");
        string token = Environment.GetEnvironmentVariable("GH_TOKEN")
            ?? throw new InvalidOperationException("GH_TOKEN is required.");
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(eventPath).ConfigureAwait(false));
        using var client = new HttpClient { BaseAddress = new Uri("https://api.github.com/"), Timeout = TimeSpan.FromSeconds(60) };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Lumyte-Fallout-ComponentScreening/1.0");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        return await PublishAsync(document.RootElement, repository, async (path, method, payload) =>
        {
            using var request = new HttpRequestMessage(new HttpMethod(method), path);
            if (payload is not null)
            {
                request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
            }

            using HttpResponseMessage response = await client.SendAsync(request).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
            return json.RootElement.Clone();
        }).ConfigureAwait(false);
    }

    internal static async Task<string> PublishAsync(
        JsonElement eventData,
        string repository,
        Func<string, string, string?, Task<JsonElement>> request)
    {
        if (!Regex.IsMatch(repository, @"\A[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+\z", RegexOptions.CultureInvariant))
        {
            throw new InvalidOperationException("Invalid repository identity.");
        }

        JsonElement run = eventData.GetProperty("workflow_run");
        if (Text(run, "event") != "pull_request" || Text(run, "path") != WorkflowPath)
        {
            return "ignored workflow";
        }

        JsonElement repo = await request($"repos/{repository}", "GET", null).ConfigureAwait(false);
        if (run.GetProperty("repository").GetProperty("id").GetInt64() != repo.GetProperty("id").GetInt64())
        {
            throw new InvalidOperationException("Run repository mismatch.");
        }

        string sha = Text(run, "head_sha");
        if (!Regex.IsMatch(sha, @"\A[0-9a-f]{40}\z", RegexOptions.CultureInvariant))
        {
            throw new InvalidOperationException("Invalid run head SHA.");
        }

        if (!run.TryGetProperty("pull_requests", out JsonElement pulls) || pulls.GetArrayLength() != 1)
        {
            return "missing or ambiguous PR association; summary remains in Actions";
        }

        long number = pulls[0].GetProperty("number").GetInt64();
        if (number <= 0)
        {
            throw new InvalidOperationException("Invalid PR number.");
        }

        string pullPath = $"repos/{repository}/pulls/{number.ToString(CultureInfo.InvariantCulture)}";
        JsonElement pull = await request(pullPath, "GET", null).ConfigureAwait(false);
        if (Text(pull.GetProperty("head"), "sha") != sha)
        {
            return "obsolete head";
        }

        JsonElement? owned = null;
        for (int page = 1; ; page++)
        {
            if (page > 100)
            {
                throw new InvalidOperationException("Comment pagination limit exceeded; no comment was changed.");
            }

            JsonElement comments = await request(
                $"repos/{repository}/issues/{number.ToString(CultureInfo.InvariantCulture)}/comments?per_page=100&page={page.ToString(CultureInfo.InvariantCulture)}",
                "GET",
                null).ConfigureAwait(false);
            foreach (JsonElement comment in comments.EnumerateArray())
            {
                if (Text(comment.GetProperty("user"), "login") == "github-actions[bot]"
                    && Text(comment, "body").StartsWith(Marker, StringComparison.Ordinal)
                    && (owned is null || comment.GetProperty("id").GetInt64() > owned.Value.GetProperty("id").GetInt64()))
                {
                    owned = comment.Clone();
                }
            }

            if (comments.GetArrayLength() < 100)
            {
                break;
            }
        }

        pull = await request(pullPath, "GET", null).ConfigureAwait(false);
        if (Text(pull.GetProperty("head"), "sha") != sha)
        {
            return "obsolete head";
        }

        string body = CreateBody(run, repository);
        if (owned is JsonElement existing)
        {
            if (!ShouldReplace(Text(existing, "body"), run))
            {
                return "newer result already published";
            }

            long id = existing.GetProperty("id").GetInt64();
            await request($"repos/{repository}/issues/comments/{id.ToString(CultureInfo.InvariantCulture)}", "PATCH", JsonSerializer.Serialize(new { body })).ConfigureAwait(false);
        }
        else
        {
            await request($"repos/{repository}/issues/{number.ToString(CultureInfo.InvariantCulture)}/comments", "POST", JsonSerializer.Serialize(new { body })).ConfigureAwait(false);
        }

        return "published";
    }

    internal static string CreateBody(JsonElement run, string repository)
    {
        string conclusion = Text(run, "conclusion");
        string status = conclusion switch
        {
            "success" => "測定処理が完了しました。退行候補・未測定範囲は実行結果を確認してください。",
            "failure" => "測定処理が失敗しました。退行なしとは判定できません。",
            "timed_out" => "測定が時間上限に達しました。退行なしとは判定できません。",
            "cancelled" => "測定は取り消されました。完了した比較結果ではありません。",
            _ => "測定は正常完了していません。実行結果を確認してください。",
        };
        long id = run.GetProperty("id").GetInt64();
        long number = run.GetProperty("run_number").GetInt64();
        long attempt = Attempt(run);
        string sha = Text(run, "head_sha");
        if (!Regex.IsMatch(sha, @"\A[0-9a-f]{40}\z", RegexOptions.CultureInvariant)
            || !Regex.IsMatch(repository, @"\A[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+\z", RegexOptions.CultureInvariant)
            || id <= 0 || number <= 0 || attempt <= 0)
        {
            throw new InvalidOperationException("Invalid trusted run identity.");
        }

        return FormattableString.Invariant($"{Marker}\n<!-- run: {number} attempt: {attempt} -->\n**部品性能スクリーニング（非ブロッキング）**\n\n{status}\n\n対象: {sha}\n[Job Summary・ログ・生データ](https://github.com/{repository}/actions/runs/{id})\n\n共有ランナーの短時間測定です。退行候補は確定診断ではありません。");
    }

    internal static bool ShouldReplace(string body, JsonElement run)
    {
        Match match = Regex.Match(body, @"<!-- run: ([0-9]+) attempt: ([0-9]+) -->", RegexOptions.CultureInvariant);
        return !match.Success || (long.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture), long.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture))
            .CompareTo((run.GetProperty("run_number").GetInt64(), Attempt(run))) <= 0;
    }

    private static long Attempt(JsonElement run) => run.TryGetProperty("run_attempt", out JsonElement attempt) ? attempt.GetInt64() : 1;

    private static string Text(JsonElement value, string name) => value.TryGetProperty(name, out JsonElement property) && property.ValueKind == JsonValueKind.String ? property.GetString()! : string.Empty;
}
