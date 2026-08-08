using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace WindowsComputerUseMCP.Skills
{
    public sealed record SlackMessage(string Id, string Ts, string? User, string Text, JsonElement? Raw = null, JsonElement? Reactions = null);
    public sealed record SlackChannel(string Id, string Name);

    /// <summary>
    /// Minimal Slack reader using Web API. Requires bearer token via constructor or SLACK_BOT_TOKEN env var.
    /// Supports conversations.list and conversations.history with cursor pagination.
    /// </summary>
    public sealed class SlackReader : IDisposable
    {
        private readonly HttpClient _http;
        private readonly string _token;
        private bool _disposed;

        public SlackReader(string? token = null, HttpClient? httpClient = null)
        {
            _token = token ?? Environment.GetEnvironmentVariable("SLACK_BOT_TOKEN") ?? throw new ArgumentException("SLACK_BOT_TOKEN not set and no token provided.");
            _http = httpClient ?? new HttpClient();
            _http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _token);
        }

        public async Task<IReadOnlyList<SlackChannel>> ListChannelsAsync()
        {
            var list = new List<SlackChannel>();
            string? cursor = null;
            do
            {
                var url = "https://slack.com/api/conversations.list?limit=200" + (cursor is null ? string.Empty : "&cursor=" + Uri.EscapeDataString(cursor));
                using var resp = await _http.GetAsync(url).ConfigureAwait(false);
                var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                using var doc = JsonDocument.Parse(body);
                if (!doc.RootElement.TryGetProperty("ok", out var ok) || !ok.GetBoolean())
                {
                    var err = doc.RootElement.TryGetProperty("error", out var e) ? e.GetString() : "unknown";
                    throw new InvalidOperationException($"Slack API conversations.list failed: {err}");
                }

                if (doc.RootElement.TryGetProperty("channels", out var channels))
                {
                    foreach (var el in channels.EnumerateArray())
                    {
                        var id = el.GetProperty("id").GetString()!;
                        var name = el.TryGetProperty("name", out var nm) ? nm.GetString() : el.GetProperty("name_normalized").GetString();
                        list.Add(new SlackChannel(id, name ?? id));
                    }
                }

                cursor = doc.RootElement.TryGetProperty("response_metadata", out var meta) && meta.TryGetProperty("next_cursor", out var nc) ? nc.GetString() : null;
            } while (!string.IsNullOrEmpty(cursor));

            return list;
        }

        /// <summary>
        /// Get channel history since optional DateTime (UTC). Returns up to limit per page and follows cursor until done.
        /// </summary>
        public async Task<IReadOnlyList<SlackMessage>> GetChannelHistoryAsync(string channelId, DateTime? sinceUtc = null, int limit = 200)
        {
            var result = new List<SlackMessage>();
            string? cursor = null;
            var oldest = sinceUtc.HasValue ? ((DateTimeOffset)sinceUtc.Value).ToUnixTimeSeconds().ToString() : null;

            do
            {
                var url = $"https://slack.com/api/conversations.history?channel={Uri.EscapeDataString(channelId)}&limit={limit}" + (oldest is null ? string.Empty : "&oldest=" + oldest) + (cursor is null ? string.Empty : "&cursor=" + Uri.EscapeDataString(cursor));
                using var resp = await _http.GetAsync(url).ConfigureAwait(false);
                var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                using var doc = JsonDocument.Parse(body);
                if (!doc.RootElement.TryGetProperty("ok", out var ok) || !ok.GetBoolean())
                {
                    var err = doc.RootElement.TryGetProperty("error", out var e) ? e.GetString() : "unknown";
                    throw new InvalidOperationException($"Slack API conversations.history failed: {err}");
                }

                if (doc.RootElement.TryGetProperty("messages", out var messages))
                {
                    foreach (var m in messages.EnumerateArray())
                    {
                        var ts = m.GetProperty("ts").GetString()!;
                        var text = m.TryGetProperty("text", out var t) ? t.GetString() : string.Empty;
                        var user = m.TryGetProperty("user", out var u) ? u.GetString() : null;
                        JsonElement? reactions = null;
                        if (m.TryGetProperty("reactions", out var r)) reactions = r;
                        result.Add(new SlackMessage(m.GetProperty("client_msg_id").GetString() ?? Guid.NewGuid().ToString(), ts, user, text ?? string.Empty, m, reactions));
                    }
                }

                cursor = doc.RootElement.TryGetProperty("response_metadata", out var meta) && meta.TryGetProperty("next_cursor", out var nc) ? nc.GetString() : null;
            } while (!string.IsNullOrEmpty(cursor));

            return result;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _http?.Dispose();
            _disposed = true;
        }
    }
}
