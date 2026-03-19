// FORK ADDITION: Reddit integration for SysBot.
// Allows users to request trades by DM-ing the bot's Reddit account or posting
// in a configured megathread.  Supports !help, !check (legality), and !trade.
//
// Key design decisions / fixes applied in this fork:
//   • Bounded _seen set (HashSet + Queue, cap 2000) prevents unbounded RAM growth
//     over long uptimes.  The set is also persisted to reddit_seen.json so the bot
//     doesn't re-process old messages after a restart.
//   • Queue-closed and queue-full checks happen *before* any PKM work so users get
//     a clear error instead of a silent drop.
//   • Trade code is generated via _hub.Config.Trade.GetRandomTradeCode() so it
//     respects the hub's MinTradeCode/MaxTradeCode range — consistent with Discord.
//   • EntityConverter null result is handled gracefully with a user-facing error
//     instead of a NullReferenceException crash.
//   • FNV-1a hash uses full char value (not just low byte) for correct Unicode
//     hashing of Reddit usernames.
//   • ThrottleAsync owns the _lastAction timestamp — it is no longer updated
//     redundantly at the call site.
//   • Single confirmation message after queuing (removed duplicate DM).
using PKHeX.Core;
using SysBot.Base;
using System.Text.Json;

namespace SysBot.Pokemon.Reddit
{
    /// <summary>
    /// Connects SysBot to Reddit.  This class polls the bot's inbox and a
    /// set of configured megathread posts for commands.  Supported
    /// commands include !help, !check, and !trade.  Trade requests are
    /// validated for legality against the active game context and then
    /// enqueued into the same shared queue as Discord requests.
    /// </summary>
    public sealed class RedditConnector<T> where T : PKM, new()
    {
        private readonly PokeTradeHub<T> _hub;
        private readonly RedditSettings _cfg;
        private readonly RedditApiClient _api;

        // Bounded seen-set: cap at SeenCapacity to prevent unbounded growth over long uptimes.
        private readonly HashSet<string> _seen = new(StringComparer.OrdinalIgnoreCase);
        private readonly Queue<string> _seenOrder = new();
        private const int SeenCapacity = 2000;

        // Path for persisting _seen across restarts.
        private static readonly string SeenFilePath = Path.Combine(
            AppContext.BaseDirectory, "reddit_seen.json");

        public RedditConnector(PokeTradeHub<T> hub, RedditSettings cfg)
        {
            _hub = hub;
            _cfg = cfg;
            _api = new RedditApiClient(cfg);
            LoadSeen();
        }

        /// <summary>
        /// Start polling Reddit.  This method returns immediately and
        /// schedules the polling loop on a background task.
        /// </summary>
        public void Start(CancellationToken token)
        {
            if (!_cfg.Enabled)
                return;
            _ = Task.Run(() => LoopAsync(token), token);
            LogUtil.LogInfo("RedditConnector started.", nameof(RedditConnector<T>));
        }

        private async Task LoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await PollInboxAsync(ct).ConfigureAwait(false);
                    await PollMegathreadsAsync(ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    LogUtil.LogSafe(ex, nameof(RedditConnector<T>));
                }
                var delay = TimeSpan.FromSeconds(Math.Max(5, _cfg.PollSeconds));
                try { await Task.Delay(delay, ct).ConfigureAwait(false); }
                catch (TaskCanceledException) { }
            }
        }

        /// <summary>
        /// Poll the bot's inbox for unread messages and comments.
        /// </summary>
        private async Task PollInboxAsync(CancellationToken ct)
        {
            var json = await _api.GetUnreadInboxJsonAsync(ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            var children = doc.RootElement.GetProperty("data").GetProperty("children");
            var toMarkRead = new List<string>();
            foreach (var child in children.EnumerateArray())
            {
                var data = child.GetProperty("data");
                var fullname = data.GetProperty("name").GetString() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(fullname) || !TryAddSeen(fullname))
                    continue;
                var author = data.GetProperty("author").GetString() ?? string.Empty;
                var body = data.TryGetProperty("body", out var bodyProp) ? bodyProp.GetString() ?? string.Empty : string.Empty;
                if (string.IsNullOrWhiteSpace(author) || string.IsNullOrWhiteSpace(body))
                    continue;
                if (!string.IsNullOrWhiteSpace(_cfg.Username) && author.Equals(_cfg.Username, StringComparison.OrdinalIgnoreCase))
                    continue;
                await HandleCommandAsync(author, body, isDm: true, replyFullname: fullname, ct).ConfigureAwait(false);
                toMarkRead.Add(fullname);
            }
            if (toMarkRead.Count > 0)
                await _api.MarkReadAsync(toMarkRead, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Poll configured megathread posts for new top-level comments.
        /// </summary>
        private async Task PollMegathreadsAsync(CancellationToken ct)
        {
            var posts = SplitCsv(_cfg.MegathreadPostIdsCsv);
            if (posts.Count == 0)
                return;
            foreach (var postId in posts)
            {
                var json = await _api.GetMegathreadCommentsJsonAsync(postId, ct).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                // comments response is [postListing, commentListing]
                if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() < 2)
                    continue;
                var commentListing = doc.RootElement[1];
                var children = commentListing.GetProperty("data").GetProperty("children");
                foreach (var child in children.EnumerateArray())
                {
                    if (!child.TryGetProperty("kind", out var kind) || kind.GetString() != "t1")
                        continue;
                    var data = child.GetProperty("data");
                    var fullname = data.GetProperty("name").GetString() ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(fullname) || !TryAddSeen(fullname))
                        continue;
                    var author = data.GetProperty("author").GetString() ?? string.Empty;
                    var body = data.GetProperty("body").GetString() ?? string.Empty;
                    var subreddit = data.GetProperty("subreddit").GetString() ?? string.Empty;
                    if (!IsAllowedSubreddit(subreddit))
                        continue;
                    if (string.IsNullOrWhiteSpace(author) || string.IsNullOrWhiteSpace(body))
                        continue;
                    await HandleCommandAsync(author, body, isDm: false, replyFullname: fullname, ct).ConfigureAwait(false);
                }
            }
            SaveSeen();
        }

        /// <summary>
        /// Process a single Reddit command.
        /// </summary>
        private async Task HandleCommandAsync(string username, string body, bool isDm, string replyFullname, CancellationToken ct)
        {
            var prefix = string.IsNullOrWhiteSpace(_cfg.CommandPrefix) ? "!" : _cfg.CommandPrefix;
            var trimmed = body.Trim();
            if (!trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return;

            var cmdLine = trimmed.Split('\n')[0].Trim();
            var parts = cmdLine.Substring(prefix.Length).Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
                return;

            var cmd = parts[0].ToLowerInvariant();

            if (cmd == "help")
            {
                var msg = $"Commands:\n\n"
                        + $"`{prefix}check` + Showdown set (legality check)\n"
                        + $"`{prefix}trade` + code + Showdown set (queues if legal)\n\n"
                        + "Trade format example (DM):\n"
                        + $"`{prefix}trade 1234-5678` then paste the Showdown set on the next lines.";
                await ReplyAsync(isDm, username, replyFullname, msg, ct).ConfigureAwait(false);
                return;
            }

            if (cmd == "check")
            {
                var setText = ExtractAfterFirstLine(trimmed);
                var res = ShowdownLegalityValidator.Validate<T>(setText, _cfg.StrictSetMatching);
                var msg = res.Ok ? "✅ Legal for the current bot/game." : $"❌ {res.Message}";
                await ReplyAsync(isDm, username, replyFullname, msg + _cfg.Footer, ct).ConfigureAwait(false);
                return;
            }

            if (cmd == "trade")
            {
                if (_cfg.RequireDmForTrades && !isDm)
                {
                    await ReplyAsync(false, username, replyFullname,
                        $"Trades are accepted via DM to keep the queue clean. Please DM me:\n\n`{prefix}trade 1234-5678` + your Showdown set.", ct).ConfigureAwait(false);
                    return;
                }

                // Check queue availability before doing any work
                if (!_hub.Queues.Info.GetCanQueue())
                {
                    await ReplyAsync(isDm, username, replyFullname,
                        $"⚠️ The queue is currently closed. Try again later.{_cfg.Footer}", ct).ConfigureAwait(false);
                    return;
                }

                var queueCount = _hub.Queues.Info.Count;
                var queueMax = _hub.Config.Queues.ThresholdLock;
                if (queueMax > 0 && queueCount >= queueMax)
                {
                    await ReplyAsync(isDm, username, replyFullname,
                        $"⚠️ Queue is full ({queueCount}/{queueMax}). Try again later.{_cfg.Footer}", ct).ConfigureAwait(false);
                    return;
                }

                // Attempt to parse a user-provided link code
                int tradeCode;
                bool userProvidedCode = TryParseTradeCodeFromFirstLine(cmdLine, prefix, out tradeCode);
                if (!userProvidedCode)
                {
                    // Generate a code using hub config range (respects MinTradeCode/MaxTradeCode)
                    tradeCode = _hub.Config.Trade.GetRandomTradeCode();
                    var formatted = FormatTradeCode(tradeCode);
                    await _api.SendDmAsync(username, "Your Trade Code",
                        $"✅ I generated a trade code for you: **{formatted}**\n\nPlease search that code when I DM you that you are up next.", ct).ConfigureAwait(false);
                    // If the request came from a public comment, also reply telling them to check DMs
                    if (!isDm)
                    {
                        await _api.ReplyCommentAsync(replyFullname,
                            "✅ Check your DMs — I sent you a generated link code for this trade.", ct).ConfigureAwait(false);
                    }
                }
                else
                {
                    // Confirm the provided code via DM
                    var formatted = FormatTradeCode(tradeCode);
                    await _api.SendDmAsync(username, "Trade Code Confirmed",
                        $"✅ Using your code: **{formatted}**", ct).ConfigureAwait(false);
                }

                // Extract the rest of the body (after the first line) as the Showdown set
                var setText = ExtractAfterFirstLine(trimmed);
                var val = ShowdownLegalityValidator.Validate<T>(setText, _cfg.StrictSetMatching);
                if (!val.Ok || val.Pokemon is null)
                {
                    await ReplyAsync(isDm, username, replyFullname, $"❌ {val.Message}{_cfg.Footer}", ct).ConfigureAwait(false);
                    return;
                }

                // Convert to correct PKM type
                var pkm = val.Pokemon;
                T pkT;
                if (pkm is T direct)
                    pkT = direct;
                else
                {
                    var converted = EntityConverter.ConvertToType(pkm, typeof(T), out _) as T;
                    if (converted is null)
                    {
                        await ReplyAsync(isDm, username, replyFullname,
                            $"❌ Could not convert that Pokémon for the current game.{_cfg.Footer}", ct).ConfigureAwait(false);
                        return;
                    }
                    pkT = converted;
                }

                // Derive a stable user ID salted with "reddit:" to avoid collisions with Discord IDs
                var userId = StableUlongFromString($"reddit:{username}");
                var trainer = new PokeTradeTrainerInfo(username, userId);
                var notifier = new RedditTradeNotifier<T>(_api, username);
                var detail = new PokeTradeDetail<T>(pkT, trainer, notifier, PokeTradeType.Specific, tradeCode);
                var entry = new TradeEntry<T>(detail, userId, PokeRoutineType.LinkTrade, username);
                var added = _hub.Queues.Info.AddToTradeQueue(entry, userId);

                if (added == QueueResultAdd.AlreadyInQueue)
                {
                    await ReplyAsync(isDm, username, replyFullname,
                        $"⚠️ You are already in the queue.{_cfg.Footer}", ct).ConfigureAwait(false);
                    return;
                }

                // Single confirmation — ReplyAsync sends a DM when isDm=true, or a public reply otherwise
                await ReplyAsync(isDm, username, replyFullname,
                    $"✅ Added to queue (position #{_hub.Queues.Info.Count}). I'll DM you when it's your turn.{_cfg.Footer}", ct).ConfigureAwait(false);
                return;
            }
        }

        /// <summary>
        /// Reply either publicly or privately, respecting dry-run mode and throttling.
        /// </summary>
        private async Task ReplyAsync(bool isDm, string username, string replyFullname, string msg, CancellationToken ct)
        {
            await ThrottleAsync(ct).ConfigureAwait(false);
            if (isDm)
                await _api.SendDmAsync(username, "CrabBot", msg, ct).ConfigureAwait(false);
            else
                await _api.ReplyCommentAsync(replyFullname, msg, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Rate-limit outgoing actions in live mode.  No-op when dry-run.
        /// </summary>
        private async Task ThrottleAsync(CancellationToken ct)
        {
            if (_cfg.DryRun)
                return;
            var min = Math.Max(1, _cfg.MinSecondsBetweenActions);
            var since = DateTimeOffset.UtcNow - _lastAction;
            if (since.TotalSeconds < min)
                await Task.Delay(TimeSpan.FromSeconds(min - since.TotalSeconds), ct).ConfigureAwait(false);
            _lastAction = DateTimeOffset.UtcNow;
        }

        private DateTimeOffset _lastAction = DateTimeOffset.MinValue;

        private bool IsAllowedSubreddit(string subreddit)
        {
            var allow = SplitCsv(_cfg.AllowedSubredditsCsv);
            if (allow.Count == 0)
                return true;
            return allow.Contains(subreddit, StringComparer.OrdinalIgnoreCase);
        }

        private static string ExtractAfterFirstLine(string text)
        {
            var idx = text.IndexOf('\n');
            return idx < 0 ? string.Empty : text[(idx + 1)..].Trim();
        }

        /// <summary>
        /// Attempts to parse an 8-digit link code from the first line of a
        /// command.  Supported formats: `12345678` and `1234-5678`.
        /// </summary>
        private static bool TryParseTradeCodeFromFirstLine(string firstLine, string prefix, out int code)
        {
            code = 0;
            var trimmed = firstLine.Trim();
            if (!trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return false;
            var pieces = trimmed.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (pieces.Length < 2)
                return false;
            var raw = pieces[1].Trim().Replace("-", string.Empty).Replace("_", string.Empty);
            if (raw.Length != 8 || !int.TryParse(raw, out var parsed))
                return false;
            code = parsed;
            return true;
        }

        /// <summary>Format a link code as XXXX-XXXX.</summary>
        private static string FormatTradeCode(int code) => $"{code:0000-0000}";

        private static List<string> SplitCsv(string csv) =>
            string.IsNullOrWhiteSpace(csv) ? [] : csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

        /// <summary>
        /// Add a fullname to the bounded seen set.  Evicts the oldest entry
        /// when the capacity is exceeded.  Returns false if already present.
        /// </summary>
        private bool TryAddSeen(string fullname)
        {
            if (!_seen.Add(fullname))
                return false;
            _seenOrder.Enqueue(fullname);
            if (_seenOrder.Count > SeenCapacity)
                _seen.Remove(_seenOrder.Dequeue());
            return true;
        }

        /// <summary>
        /// Persist the current seen set to disk so the bot does not
        /// re-process messages after a restart.
        /// </summary>
        private void SaveSeen()
        {
            try
            {
                File.WriteAllText(SeenFilePath, JsonSerializer.Serialize(_seenOrder.ToArray()));
            }
            catch (Exception ex)
            {
                LogUtil.LogSafe(ex, nameof(RedditConnector<T>));
            }
        }

        /// <summary>
        /// Load the persisted seen set from disk on startup.
        /// </summary>
        private void LoadSeen()
        {
            try
            {
                if (!File.Exists(SeenFilePath))
                    return;
                var entries = JsonSerializer.Deserialize<string[]>(File.ReadAllText(SeenFilePath)) ?? [];
                foreach (var e in entries)
                    TryAddSeen(e);
                LogUtil.LogInfo($"Loaded {_seen.Count} seen Reddit IDs from disk.", nameof(RedditConnector<T>));
            }
            catch (Exception ex)
            {
                LogUtil.LogSafe(ex, nameof(RedditConnector<T>));
            }
        }

        /// <summary>
        /// Compute a stable 64-bit hash from a string using FNV-1a.
        /// </summary>
        private static ulong StableUlongFromString(string s)
        {
            const ulong offset = 14695981039346656037;
            const ulong prime = 1099511628211;
            ulong hash = offset;
            foreach (var ch in s)
            {
                hash ^= ch;   // use full char value, not just low byte
                hash *= prime;
            }
            return hash;
        }
    }
}
