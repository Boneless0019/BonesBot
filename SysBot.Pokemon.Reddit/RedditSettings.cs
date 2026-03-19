using System.ComponentModel;

namespace SysBot.Pokemon.Reddit
{
    /// <summary>
    /// Configuration for Reddit integration.  These settings control how the
    /// bot logs in to Reddit, which threads to watch, and how it interacts
    /// with users.  You can enable or disable the integration entirely by
    /// toggling the <see cref="Enabled"/> property.
    /// </summary>
    public sealed class RedditSettings
    {
        private const string Operation = nameof(Operation);

        public override string ToString() => "Reddit Settings";

        /// <summary>
        /// Enable Reddit integration.  When false, no Reddit polling or
        /// posting will occur.
        /// </summary>
        [Category(Operation), Description("Enable Reddit integration.")]
        public bool Enabled { get; set; } = false;

        /// <summary>
        /// OAuth client ID for your Reddit application.
        /// </summary>
        [Category(Operation), Description("Reddit OAuth Client ID.")]
        public string ClientId { get; set; } = string.Empty;

        /// <summary>
        /// OAuth client secret for your Reddit application.
        /// </summary>
        [Category(Operation), Description("Reddit OAuth Client Secret.")]
        public string ClientSecret { get; set; } = string.Empty;

        /// <summary>
        /// Reddit account username, used with password authentication when
        /// no refresh token is supplied.
        /// </summary>
        [Category(Operation), Description("Reddit account username (for password flow; optional if using refresh token).")]
        public string Username { get; set; } = string.Empty;

        /// <summary>
        /// Reddit account password, used with password authentication when
        /// no refresh token is supplied.
        /// </summary>
        [Category(Operation), Description("Reddit account password (for password flow; optional if using refresh token).")]
        public string Password { get; set; } = string.Empty;

        /// <summary>
        /// OAuth refresh token.  When provided, refresh-token flow is used
        /// instead of username/password.  Recommended for long-lived bots.
        /// </summary>
        [Category(Operation), Description("OAuth refresh token (recommended). If set, refresh-token flow is used.")]
        public string RefreshToken { get; set; } = string.Empty;

        /// <summary>
        /// User-Agent header sent to Reddit.  Reddit requires a unique
        /// descriptive UA string.  Example: "CrabBot/1.0 by u/YourUser".
        /// </summary>
        [Category(Operation), Description("User-Agent (required by Reddit). Example: CrabBot/1.0 by u/YourUser")]
        public string UserAgent { get; set; } = "CrabBot/1.0 by u/CrabBot";

        /// <summary>
        /// Command prefix used in Reddit commands.  Defaults to '!'.
        /// </summary>
        [Category(Operation), Description("Command prefix for Reddit (default '!').")]
        public string CommandPrefix { get; set; } = "!";

        /// <summary>
        /// Comma-separated list of megathread post IDs to monitor for
        /// commands.  Use 't3_xxxxx' or just the post ID.
        /// </summary>
        [Category(Operation), Description("Comma-separated list of megathread post IDs to watch. Use 't3_xxxxx' or 'xxxxx'.")]
        public string MegathreadPostIdsCsv { get; set; } = string.Empty;

        /// <summary>
        /// Comma-separated list of allowed subreddit names.  If empty,
        /// all subreddits are allowed.
        /// </summary>
        [Category(Operation), Description("Comma-separated list of allowed subreddit names (optional safety allowlist).")]
        public string AllowedSubredditsCsv { get; set; } = string.Empty;

        /// <summary>
        /// When true, trades must originate via DM.  Megathread commands
        /// will still respond to !check and help, but !trade will instruct
        /// users to DM the bot instead of queueing directly.
        /// </summary>
        [Category(Operation), Description("If true, trades are only accepted via DM. Megathread can still do !check and instructions.")]
        public bool RequireDmForTrades { get; set; } = true;

        /// <summary>
        /// Polling interval for checking Reddit inbox and megathread comments.
        /// Measured in seconds.
        /// </summary>
        [Category(Operation), Description("Polling interval seconds for inbox + megathread scanning.")]
        public int PollSeconds { get; set; } = 15;

        /// <summary>
        /// When true, no network actions are performed and all Reddit
        /// interactions are logged instead.  Useful for testing.
        /// </summary>
        [Category(Operation), Description("Dry run: logs actions but does not call Reddit APIs.")]
        public bool DryRun { get; set; } = true;

        /// <summary>
        /// When true, requests are rejected if the AutoLegality system has
        /// to change important properties such as species, ability, or
        /// moves in order to legalize the set.
        /// </summary>
        [Category(Operation), Description("If true, rejects requests when legality repair changes important parts of the request.")]
        public bool StrictSetMatching { get; set; } = false;

        /// <summary>
        /// Footer text appended to confirmations and error messages.
        /// </summary>
        [Category(Operation), Description("Footer appended to confirmations.")]
        public string Footer { get; set; } = "\n\n**Didn't get in?** Join our Discord: https://bidoof.net";

        /// <summary>
        /// Minimum number of seconds between sending replies or actions to
        /// Reddit.  Helps avoid hitting rate limits.
        /// </summary>
        [Category(Operation), Description("Minimum seconds between sending replies/actions to Reddit to avoid rate limits.")]
        public int MinSecondsBetweenActions { get; set; } = 2;
    }
}
