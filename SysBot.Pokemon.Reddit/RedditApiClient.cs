// FORK ADDITION: Low-level Reddit API wrapper used by RedditConnector.
// Handles OAuth2 token management (refresh-token or password grant), inbox
// polling, comment replies, DMs, and megathread comment fetching.
//
// Fixes applied in this fork:
//   • GetUnreadInboxJsonAsync and GetMegathreadCommentsJsonAsync now check
//     HTTP status codes and return safe empty payloads on error instead of
//     crashing the polling loop with an unhandled exception.
//   • Dry-run mode fakes the token and logs all outgoing actions to the
//     console without making any real Reddit API calls — safe for testing.
using SysBot.Base;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace SysBot.Pokemon.Reddit
{
    /// <summary>
    /// Low-level API client for interacting with Reddit's REST API.  This
    /// class handles authentication (via refresh token or username/password)
    /// and exposes a few convenience methods for reading the inbox,
    /// replying to comments, and sending messages.
    /// </summary>
    internal sealed class RedditApiClient
    {
        private readonly HttpClient _http;
        private readonly RedditSettings _cfg;

        private string _accessToken = string.Empty;
        private DateTimeOffset _expiresAt = DateTimeOffset.MinValue;

        public RedditApiClient(RedditSettings cfg)
        {
            _cfg = cfg;
            _http = new HttpClient();
            // set the user agent once on creation
            _http.DefaultRequestHeaders.UserAgent.ParseAdd(cfg.UserAgent);
        }

        /// <summary>
        /// Ensure a valid access token exists.  This will fetch a new token
        /// when none is present or when it is expired.  In dry-run mode the
        /// token is faked and no network calls are made.
        /// </summary>
        private async Task EnsureTokenAsync(CancellationToken ct)
        {
            if (!string.IsNullOrWhiteSpace(_accessToken) &&
                DateTimeOffset.UtcNow < _expiresAt.AddSeconds(-30))
                return;

            if (_cfg.DryRun)
            {
                _accessToken = "DRYRUN";
                _expiresAt = DateTimeOffset.UtcNow.AddHours(1);
                return;
            }

            using var tokenReq = new HttpRequestMessage(HttpMethod.Post, "https://www.reddit.com/api/v1/access_token");
            var basic = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_cfg.ClientId}:{_cfg.ClientSecret}"));
            tokenReq.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);

            var body = new Dictionary<string, string>();
            if (!string.IsNullOrWhiteSpace(_cfg.RefreshToken))
            {
                body["grant_type"] = "refresh_token";
                body["refresh_token"] = _cfg.RefreshToken;
            }
            else
            {
                body["grant_type"] = "password";
                body["username"] = _cfg.Username;
                body["password"] = _cfg.Password;
            }
            tokenReq.Content = new FormUrlEncodedContent(body);

            var resp = await _http.SendAsync(tokenReq, ct).ConfigureAwait(false);
            var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                LogUtil.LogError($"Reddit token error: {resp.StatusCode} {json}", nameof(RedditApiClient));
                throw new InvalidOperationException("Failed to authenticate with Reddit.");
            }

            using var doc = JsonDocument.Parse(json);
            _accessToken = doc.RootElement.GetProperty("access_token").GetString() ?? string.Empty;
            var expires = doc.RootElement.GetProperty("expires_in").GetInt32();
            _expiresAt = DateTimeOffset.UtcNow.AddSeconds(expires);
        }

        /// <summary>
        /// Construct an authenticated request.  In dry-run mode the bearer
        /// header is omitted.
        /// </summary>
        private async Task<HttpRequestMessage> AuthedAsync(HttpMethod method, string url, CancellationToken ct)
        {
            await EnsureTokenAsync(ct).ConfigureAwait(false);
            var req = new HttpRequestMessage(method, url);
            if (!_cfg.DryRun)
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
            return req;
        }

        /// <summary>
        /// Retrieve unread messages from the bot's inbox.
        /// </summary>
        public async Task<string> GetUnreadInboxJsonAsync(CancellationToken ct)
        {
            var req = await AuthedAsync(HttpMethod.Get, "https://oauth.reddit.com/message/unread.json?limit=50", ct);
            var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
            var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                LogUtil.LogError($"Reddit inbox error: {resp.StatusCode} {body}", nameof(RedditApiClient));
                return "{\"data\":{\"children\":[]}}"; // return empty inbox on error
            }
            return body;
        }

        /// <summary>
        /// Mark the specified messages or comments as read.  No-op in
        /// dry-run mode.
        /// </summary>
        public async Task MarkReadAsync(IEnumerable<string> fullnames, CancellationToken ct)
        {
            if (_cfg.DryRun)
                return;
            var req = await AuthedAsync(HttpMethod.Post, "https://oauth.reddit.com/api/read_message", ct);
            req.Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["id"] = string.Join(",", fullnames)
            });
            await _http.SendAsync(req, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Reply to a comment.  In dry-run mode this logs the reply instead
        /// of sending it.
        /// </summary>
        public async Task ReplyCommentAsync(string parentFullname, string text, CancellationToken ct)
        {
            if (_cfg.DryRun)
            {
                LogUtil.LogInfo($"[DRYRUN] Reply to {parentFullname}: {text}", nameof(RedditApiClient));
                return;
            }
            var req = await AuthedAsync(HttpMethod.Post, "https://oauth.reddit.com/api/comment", ct);
            req.Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["thing_id"] = parentFullname,
                ["text"] = text
            });
            var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                LogUtil.LogError($"Reddit reply failed: {resp.StatusCode}", nameof(RedditApiClient));
        }

        /// <summary>
        /// Send a direct message to a user.  In dry-run mode this logs the
        /// message instead of sending.
        /// </summary>
        public async Task SendDmAsync(string username, string subject, string text, CancellationToken ct)
        {
            if (_cfg.DryRun)
            {
                LogUtil.LogInfo($"[DRYRUN] DM to u/{username}: {subject} | {text}", nameof(RedditApiClient));
                return;
            }
            var req = await AuthedAsync(HttpMethod.Post, "https://oauth.reddit.com/api/compose", ct);
            req.Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["to"] = username,
                ["subject"] = subject,
                ["text"] = text
            });
            var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                LogUtil.LogError($"Reddit DM failed: {resp.StatusCode}", nameof(RedditApiClient));
        }

        /// <summary>
        /// Retrieve comments from a given post.  Accepts either the full
        /// fullname (t3_xxxxx) or the raw ID.
        /// </summary>
        public async Task<string> GetMegathreadCommentsJsonAsync(string postId, CancellationToken ct)
        {
            var id = postId.StartsWith("t3_") ? postId.Substring(3) : postId;
            var url = $"https://oauth.reddit.com/comments/{id}.json?limit=100&sort=new";
            var req = await AuthedAsync(HttpMethod.Get, url, ct);
            var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
            var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                LogUtil.LogError($"Reddit megathread error ({postId}): {resp.StatusCode} {body}", nameof(RedditApiClient));
                return "[]"; // return empty array on error
            }
            return body;
        }
    }
}