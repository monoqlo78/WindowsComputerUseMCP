using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using WindowsComputerUseMCP.Skills.Abstractions;

namespace WindowsComputerUseMCP.Skills
{
    public sealed class SlackSkillPack : ISkillPack
    {
        public string AppId => "slack";
        public string DisplayName => "Slack";
        public IReadOnlyList<string> ProcessNames => Array.Empty<string>();

        public IReadOnlyList<SkillActionDescriptor> ListActions() => new[]
        {
            new SkillActionDescriptor
            {
                Name = "scan-channel",
                Description = "Scan a Slack channel for procedures, approvals, and TODOs. Parameters: channel (id or name), days (int, optional, default 14).",
                Parameters = new[]
                {
                    new SkillParameterDescriptor { Name = "channel", Type = "string", Required = true, Description = "Channel ID (C...) or name (#name)" },
                    new SkillParameterDescriptor { Name = "days", Type = "int", Required = false, Description = "How many past days to scan (default 14)" }
                }
            }
        };

        public async Task<SkillActionOutcome> InvokeAsync(string actionName, IReadOnlyDictionary<string, object?> arguments, CancellationToken cancellationToken = default)
        {
            if (actionName is null) throw new ArgumentNullException(nameof(actionName));
            if (actionName.Equals("scan-channel", StringComparison.OrdinalIgnoreCase))
            {
                if (!arguments.TryGetValue("channel", out var cobj) || cobj is null) return SkillActionOutcome.Fail("'channel' argument is required");
                var channelArg = cobj.ToString()!;
                var days = 14;
                if (arguments.TryGetValue("days", out var daysObj) && daysObj is not null && int.TryParse(daysObj.ToString(), out var d)) days = d;

                var since = DateTime.UtcNow.AddDays(-days);

                try
                {
                    using var reader = new SlackReader();
                    // resolve channel id if name provided
                    var channels = await reader.ListChannelsAsync();
                    var target = channels.FirstOrDefault(ch => ch.Id.Equals(channelArg, StringComparison.OrdinalIgnoreCase)
                                                               || (channelArg.StartsWith("#") && ("#" + ch.Name).Equals(channelArg, StringComparison.OrdinalIgnoreCase))
                                                               || ch.Name.Equals(channelArg.TrimStart('#'), StringComparison.OrdinalIgnoreCase));
                    if (target is null) return SkillActionOutcome.Fail($"Channel '{channelArg}' not found in workspace. Available channels: {string.Join(',', channels.Select(c=>c.Name).Take(10))}");

                    var msgs = await reader.GetChannelHistoryAsync(target.Id, since);

                    var analysis = AnalyzeMessages(msgs, target, since, DateTime.UtcNow);

                    // write artifacts
                    var cwd = Directory.GetCurrentDirectory();
                    var artifactsDir = Path.Combine(cwd, "artifacts");
                    Directory.CreateDirectory(artifactsDir);
                    var dateSuffix = DateTime.UtcNow.ToString("yyyyMMdd");
                    var jsonPath = Path.Combine(artifactsDir, $"slack-scan-{target.Id}-{dateSuffix}.json");
                    var mdPath = Path.Combine(artifactsDir, $"slack-scan-{target.Id}-{dateSuffix}.md");

                    var options = new JsonSerializerOptions { WriteIndented = true };
                    var json = JsonSerializer.Serialize(analysis, options);
                    await File.WriteAllTextAsync(jsonPath, json, cancellationToken).ConfigureAwait(false);

                    // markdown summary
                    var md = BuildMarkdownSummary(analysis);
                    await File.WriteAllTextAsync(mdPath, md, cancellationToken).ConfigureAwait(false);

                    var data = new { json = jsonPath, markdown = mdPath, summary = analysis.Summary };                    
                    return SkillActionOutcome.Ok(data, message: $"Scanned {msgs.Count} messages in channel {target.Name}. Artifacts: {jsonPath}, {mdPath}");
                }
                catch (Exception ex)
                {
                    return SkillActionOutcome.Fail("Error scanning Slack: " + ex.Message);
                }
            }

            return SkillActionOutcome.Fail("Unknown action: " + actionName);
        }

        private static AnalysisResult AnalyzeMessages(IReadOnlyList<SlackMessage> msgs, SlackChannel channel, DateTime fromUtc, DateTime toUtc)
        {
            var procedures = new List<ProcedureItem>();
            var approvals = new List<ApprovalItem>();
            var todos = new List<TodoItem>();

            var procKeywords = new[] { "手順", "手続き", "手順書", "やること", "作業手順", "実施方法" };
            var todoPattern = new Regex(@"(?i)\b(todo|TODO|やること|やる|お願い|お願いします)\b");
            var duePattern = new Regex(@"(?i)(due[:\s]?|期限[:\s]?|までに[:\s]?)(?<date>[^\n,]+)");
            var assigneePattern = new Regex(@"@(?<user>[A-Za-z0-9._-]+)");
            var approvePattern = new Regex(@"(?i)\b(approve|approved|承認|確認済み|✅)\b");

            foreach (var m in msgs)
            {
                var text = m.Text ?? string.Empty;
                if (procKeywords.Any(k => text.Contains(k, StringComparison.OrdinalIgnoreCase)))
                {
                    procedures.Add(new ProcedureItem { Id = Guid.NewGuid().ToString(), Snippet = GetSnippet(text), MessageTs = m.Ts, Author = m.User, ThreadTs = m.Ts, Links = ExtractLinks(text) });
                }

                if (todoPattern.IsMatch(text))
                {
                    var todo = new TodoItem { MessageTs = m.Ts, Text = text, Assignee = null, Status = "open" };
                    var a = assigneePattern.Match(text);
                    if (a.Success) todo.Assignee = a.Groups["user"].Value;
                    var d = duePattern.Match(text);
                    if (d.Success)
                    {
                        todo.DueDate = d.Groups["date"].Value.Trim();
                        // naive overdue check: try parse
                        if (DateTime.TryParse(todo.DueDate, out var dueDt) && dueDt < DateTime.UtcNow) todo.Status = "overdue";
                    }
                    todos.Add(todo);
                }

                if (approvePattern.IsMatch(text) || (m.Reactions is not null && m.Reactions.Value.ValueKind == JsonValueKind.Array && m.Reactions.Value.EnumerateArray().Any()))
                {
                    var appr = new ApprovalItem { MessageTs = m.Ts, ApprovedBy = new List<string>(), ApprovalsText = new List<string>() };
                    // if reactions present, add reaction names
                    if (m.Reactions is not null && m.Reactions.Value.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var r in m.Reactions.Value.EnumerateArray())
                        {
                            if (r.TryGetProperty("name", out var nm)) appr.ApprovalsText.Add(nm.GetString() ?? "");
                            if (r.TryGetProperty("users", out var us) && us.ValueKind == JsonValueKind.Array)
                            {
                                foreach (var u in us.EnumerateArray()) appr.ApprovedBy.Add(u.GetString() ?? string.Empty);
                            }
                        }
                    }

                    approvals.Add(appr);
                }
            }

            var summary = new List<string>();
            summary.Add($"Analyzed {msgs.Count} messages in #{channel.Name} from {fromUtc:yyyy-MM-dd} to {toUtc:yyyy-MM-dd}.");
            summary.Add($"Found {procedures.Count} procedure candidates, {todos.Count} TODOs, {approvals.Count} approval-like messages.");
            if (todos.Any(t => t.Status == "overdue")) summary.Add($"There are {todos.Count(t => t.Status=="overdue")} overdue TODO(s). Consider follow-up.");

            return new AnalysisResult
            {
                ChannelId = channel.Id,
                ChannelName = channel.Name,
                ScannedFrom = fromUtc,
                ScannedTo = toUtc,
                MessagesAnalyzed = msgs.Count,
                Procedures = procedures,
                Approvals = approvals,
                Todos = todos,
                Summary = summary
            };
        }

        private static string[] ExtractLinks(string text)
        {
            var urls = new List<string>();
            var urlPattern = new Regex(@"https?://[^\s)]+", RegexOptions.Compiled | RegexOptions.IgnoreCase);
            foreach (Match m in urlPattern.Matches(text)) urls.Add(m.Value);
            return urls.ToArray();
        }

        private static string GetSnippet(string text, int max = 240) => text.Length <= max ? text : text.Substring(0, max) + "...";

        private sealed record AnalysisResult
        {
            public required string ChannelId { get; init; }
            public required string ChannelName { get; init; }
            public required DateTime ScannedFrom { get; init; }
            public required DateTime ScannedTo { get; init; }
            public required int MessagesAnalyzed { get; init; }
            public required IReadOnlyList<ProcedureItem> Procedures { get; init; }
            public required IReadOnlyList<ApprovalItem> Approvals { get; init; }
            public required IReadOnlyList<TodoItem> Todos { get; init; }
            public required IReadOnlyList<string> Summary { get; init; }
        }

        private sealed record ProcedureItem
        {
            public required string Id { get; init; }
            public required string Snippet { get; init; }
            public required string MessageTs { get; init; }
            public string? Author { get; init; }
            public string? ThreadTs { get; init; }
            public required string[] Links { get; init; }
        }

        private sealed record ApprovalItem
        {
            public required string MessageTs { get; init; }
            public required List<string> ApprovedBy { get; init; }
            public required List<string> ApprovalsText { get; init; }
            public string? RequiredBy { get; init; }
        }

        private sealed record TodoItem
        {
            public required string MessageTs { get; init; }
            public string? Assignee { get; init; }
            public string? DueDate { get; init; }
            public required string Text { get; init; }
            public required string Status { get; init; }
        }

        private static string BuildMarkdownSummary(AnalysisResult r)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"# Slack scan summary for #{r.ChannelName}");
            sb.AppendLine();
            foreach (var s in r.Summary) sb.AppendLine("- " + s);
            sb.AppendLine();
            sb.AppendLine("## Top procedures");
            foreach (var p in r.Procedures.Take(5)) sb.AppendLine($"- [{p.Id}] {p.Snippet} (ts: {p.MessageTs})");
            sb.AppendLine();
            sb.AppendLine("## TODOs");
            foreach (var t in r.Todos.Take(10)) sb.AppendLine($"- {t.Status.ToUpper()}: {t.Text} (assignee: {t.Assignee ?? "(none)"}, due: {t.DueDate ?? "(none)"})");
            sb.AppendLine();
            sb.AppendLine("## Approvals");
            foreach (var a in r.Approvals.Take(10)) sb.AppendLine($"- ts: {a.MessageTs}, approvedBy: {string.Join(',', a.ApprovedBy)} reactions: {string.Join(',', a.ApprovalsText)}");
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine("Generated by WindowsComputerUseMCP SlackSkillPack");
            return sb.ToString();
        }
    }
}
