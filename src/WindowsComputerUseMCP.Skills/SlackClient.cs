using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace WindowsComputerUseMCP.Skills
{
    /// <summary>
    /// Small Slack Web API client for posting messages. Requires SLACK_BOT_TOKEN environment variable (or pass token to constructor).
    /// Does not store secrets or attempt complex retry logic — intended as a simple integration helper.
    /// </summary>
    public sealed class SlackClient : IDisposable
    {
        private readonly HttpClient _http;
        private readonly string _token;
        private bool _disposed;

        public SlackClient(string? token = null, HttpClient? httpClient = null)
        {
            _token = token ?? Environment.GetEnvironmentVariable("SLACK_BOT_TOKEN") ?? throw new ArgumentException("SLACK_BOT_TOKEN environment variable is not set and no token was provided.");
            _http = httpClient ?? new HttpClient();
            _http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _token);
        }

        /// <summary>
        /// Send a plain text message to a channel or user. Channel can be a channel ID (e.g. C123456) or a user ID (U123456) for IMs.
        /// To post to a channel by name, pass the channel name with a leading '#', e.g. "#general" — Slack will resolve it if allowed.
        /// </summary>
        public async Task<bool> SendMessageAsync(string channel, string text)
        {
            if (string.IsNullOrWhiteSpace(channel)) throw new ArgumentException("channel");
            if (text is null) throw new ArgumentNullException(nameof(text));

            var payload = new { channel = channel, text = text };
            var json = JsonSerializer.Serialize(payload);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");

            using var resp = await _http.PostAsync("https://slack.com/api/chat.postMessage", content).ConfigureAwait(false);
            var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);

            if (!resp.IsSuccessStatusCode)
            {
                throw new HttpRequestException($"Slack API returned HTTP {(int)resp.StatusCode}: {body}");
            }

            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("ok", out var okProp) && okProp.GetBoolean())
                {
                    return true;
                }

                var err = doc.RootElement.TryGetProperty("error", out var e) ? e.GetString() : "unknown_error";
                throw new InvalidOperationException($"Slack API error: {err}");
            }
            catch (JsonException)
            {
                throw new InvalidOperationException($"Unexpected Slack API response: {body}");
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _http?.Dispose();
            _disposed = true;
        }
    }
}
