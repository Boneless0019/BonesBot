# Reddit Integration

The Reddit integration lets users request Pokémon trades by sending commands to the bot's Reddit account via DM or by posting in a configured megathread. It runs entirely in the background alongside the normal Discord/link trade queue and shares the same trade queue.

---

## How It Works

Once configured and enabled, the bot:

1. Polls the Reddit account's **unread inbox** every N seconds for DMs and comment replies
2. Polls configured **megathread posts** every N seconds for new top-level comments
3. Parses commands starting with the configured prefix (default `!`)
4. Responds to `!help`, `!check`, and `!trade`
5. For `!trade`, validates the Pokémon set for legality, generates a trade code, and adds the request to the shared SysBot queue
6. Sends the user a DM with their trade code when queued, and another DM when it's their turn

All replies go back through Reddit (DM or public comment reply). The Reddit integration and Discord integration run side by side — trades from both sources go into the same queue.

---

## Step 1: Create a Reddit Application

1. Go to [https://www.reddit.com/prefs/apps](https://www.reddit.com/prefs/apps) while logged in as your bot account
2. Click **"create another app"** at the bottom
3. Fill in:
   - **Name:** anything (e.g. `BonesBotTrade`)
   - **Type:** select **script**
   - **Description:** optional
   - **About URL:** leave blank
   - **Redirect URI:** `http://localhost` (required even for scripts)
4. Click **Create app**
5. Note the **client ID** (the short string under the app name) and the **client secret**

---

## Step 2: Get a Refresh Token (Recommended)

Using a refresh token is more reliable than username/password — Reddit doesn't expire refresh tokens unless you revoke them.

### Quick method using curl

```bash
curl -X POST https://www.reddit.com/api/v1/access_token \
  -u "YOUR_CLIENT_ID:YOUR_CLIENT_SECRET" \
  -d "grant_type=password&username=YOUR_BOT_USERNAME&password=YOUR_BOT_PASSWORD"
```

This returns an `access_token` but **not** a refresh token via password grant. To get a proper refresh token you need OAuth with `permanent` duration scope. Use the [PRAW docs](https://praw.readthedocs.io/en/stable/getting_started/authentication.html) or the Reddit OAuth flow.

### Easier: use PRAW to get a refresh token

```python
import praw

reddit = praw.Reddit(
    client_id="YOUR_CLIENT_ID",
    client_secret="YOUR_CLIENT_SECRET",
    redirect_uri="http://localhost:8080",
    user_agent="get_token"
)

print(reddit.auth.url(["identity", "read", "privatemessages", "submit", "history"], "state", "permanent"))
```

Visit the URL, authorize, copy the `code` param from the redirect, then:

```python
token = reddit.auth.authorize("THE_CODE")
print(token)  # this is your refresh token
```

If you don't want to deal with this, you can use username/password grant (less reliable — Reddit may ask for re-auth).

---

## Step 3: Configure the Bot

Open `config.json` (created by SysBot on first run) and find the `Reddit` section under `Hub`. Fill in your values:

```json
"Reddit": {
  "Enabled": true,
  "ClientId": "YOUR_CLIENT_ID",
  "ClientSecret": "YOUR_CLIENT_SECRET",
  "Username": "YourBotRedditUsername",
  "Password": "YourBotPassword",
  "RefreshToken": "YOUR_REFRESH_TOKEN",
  "UserAgent": "BonesBot/1.0 by u/YourUsername",
  "CommandPrefix": "!",
  "MegathreadPostIdsCsv": "",
  "AllowedSubredditsCsv": "",
  "RequireDmForTrades": true,
  "PollSeconds": 15,
  "DryRun": true,
  "StrictSetMatching": false,
  "Footer": "\n\n**Didn't get in?** Join our Discord!",
  "MinSecondsBetweenActions": 2
}
```

> **Start with `"DryRun": true`.** In dry-run mode, all Reddit API calls are skipped and replies are logged to the console instead. This lets you verify commands parse correctly before going live.

---

## Configuration Reference

| Setting | Type | Default | Description |
|---|---|---|---|
| `Enabled` | bool | `false` | Master switch — set to `true` to start the Reddit connector |
| `ClientId` | string | `""` | OAuth client ID from your Reddit app |
| `ClientSecret` | string | `""` | OAuth client secret from your Reddit app |
| `Username` | string | `""` | Bot Reddit account username |
| `Password` | string | `""` | Bot Reddit account password (only used if no RefreshToken) |
| `RefreshToken` | string | `""` | OAuth refresh token — if set, password is ignored |
| `UserAgent` | string | `"CrabBot/1.0 by u/CrabBot"` | Reddit requires a descriptive user agent — change this to your bot name |
| `CommandPrefix` | string | `"!"` | Character(s) that prefix commands |
| `MegathreadPostIdsCsv` | string | `""` | Comma-separated list of megathread post IDs to monitor (see below) |
| `AllowedSubredditsCsv` | string | `""` | If set, only commands from these subreddits are accepted |
| `RequireDmForTrades` | bool | `true` | If `true`, `!trade` in a megathread tells the user to DM instead; `!check` still works in the thread |
| `PollSeconds` | int | `15` | How often (seconds) to poll inbox and megathreads — minimum is 5 |
| `DryRun` | bool | `true` | Logs all actions without making real Reddit API calls |
| `StrictSetMatching` | bool | `false` | Rejects sets if AutoLegality had to change species/moves/ability to make them legal |
| `Footer` | string | (Discord link) | Text appended to every reply and confirmation |
| `MinSecondsBetweenActions` | int | `2` | Minimum seconds between outgoing Reddit actions (rate limit protection) |

---

## Megathread Setup

A megathread is a pinned Reddit post where users post `!check` or `!trade` commands as comments. The bot polls it and replies.

### Finding your post ID

The post ID is the string in the URL:
```
https://www.reddit.com/r/YourSub/comments/abc123/megathread_title/
                                          ^^^^^^
                                          this is the post ID
```

You can use either the bare ID (`abc123`) or the full fullname (`t3_abc123`) — both work.

### Configuring multiple megathreads

```json
"MegathreadPostIdsCsv": "abc123, def456, ghi789"
```

The bot polls all listed posts every `PollSeconds` seconds.

### Subreddit allowlist

If you want to restrict commands to posts from specific subreddits only:

```json
"AllowedSubredditsCsv": "BonesBotTrades, PokemonTrades"
```

Leave blank to allow commands from any subreddit.

---

## Commands

All commands start with the configured prefix (default `!`).

---

### `!help`

Returns a list of available commands with usage examples.

**Works in:** DM, megathread comment

**Example response:**
```
Commands:

`!check` + Showdown set (legality check)
`!trade` + code + Showdown set (queues if legal)

Trade format example (DM):
`!trade 1234-5678` then paste the Showdown set on the next lines.
```

---

### `!check`

Checks whether a Showdown Set is legal for the current bot game. Does not queue a trade.

**Works in:** DM, megathread comment (even if `RequireDmForTrades` is true)

**Format:**
```
!check
Pikachu @ Light Ball
Ability: Static
Level: 50
...
```

The Showdown set goes on the lines **after** the `!check` line.

**Example response (legal):**
```
✅ Legal for the current bot/game.
```

**Example response (illegal):**
```
❌ Pikachu cannot learn Volt Tackle in this game.
```

---

### `!trade`

Queues a trade. The bot validates the set, generates or uses a provided link code, and adds the request to the trade queue.

**Works in:** DM always. Megathread comments only if `RequireDmForTrades` is `false`.

#### Format with a code you provide:
```
!trade 1234-5678
Garchomp @ Choice Scarf
Ability: Rough Skin
Level: 100
...
```

The code goes on the **same line** as `!trade`. Accepted formats: `12345678` or `1234-5678`.

#### Format without a code (bot generates one):
```
!trade
Garchomp @ Choice Scarf
Ability: Rough Skin
Level: 100
...
```

If no code is given, the bot generates a random code using the hub's min/max trade code range and DMs it to you.

#### What happens after `!trade`:

1. If no code was provided:
   - Bot DMs you a generated code
   - If the command came from a megathread, bot also replies to your comment telling you to check DMs
2. Bot validates the Showdown Set for legality
3. If legal, bot adds you to the queue and replies with your position:
   ```
   ✅ Added to queue (position #3). I'll DM you when it's your turn.
   ```
4. When it's your turn, the trade notifier DMs you that the bot is ready

#### If `RequireDmForTrades` is `true` and the command comes from a megathread:
```
Trades are accepted via DM to keep the queue clean. Please DM me:

`!trade 1234-5678` + your Showdown set.
```

---

## How Trade Codes Work

- If you provide a code: `!trade 1234-5678` — that exact code is used
- If you omit a code: the bot generates one using the hub's `MinTradeCode`/`MaxTradeCode` range (same as Discord)
- The code is always DMed to the user so they know what to enter on their Switch
- Codes are formatted as `XXXX-XXXX` in messages

---

## Seen Message Tracking

The bot tracks which Reddit message/comment IDs it has already processed so it doesn't respond to the same message twice across restarts. This is stored in `reddit_seen.json` next to `SysBot.exe`.

- The seen set is capped at 2,000 entries (oldest removed first) to prevent unbounded memory growth
- The file is saved after every megathread poll cycle
- Delete `reddit_seen.json` to reset tracking (the bot may re-process recent messages once)

---

## Dry-Run Mode

`"DryRun": true` is the default. In this mode:

- The bot authenticates with a fake token (no network call)
- All replies, DMs, and mark-read actions are **logged to the console** instead of sent
- You can verify commands are parsed correctly, codes are generated correctly, and legality checks work — all without touching Reddit's API

**Enable dry-run when:**
- Setting up for the first time
- Testing new megathread IDs
- Debugging why a command isn't being recognized

**To go live:** set `"DryRun": false` and restart the bot.

---

## Rate Limiting

Reddit's API has rate limits. The bot protects against them with:

- `MinSecondsBetweenActions` — minimum wait between outgoing API calls (replies, DMs). Default is 2 seconds.
- `PollSeconds` — polling interval. Don't set this below 10 in production.

If the bot hits a rate limit, the API client logs the HTTP status code and the affected poll cycle is skipped gracefully — it won't crash.

---

## Troubleshooting

**Bot doesn't respond to commands**

1. Check that `Enabled` is `true` and `DryRun` is `false`
2. Check the bot log for `[RedditConnector] started` — if it's missing, the connector didn't start
3. Verify the command starts with the correct prefix (default `!`)
4. Make sure the command is on its own line before the Showdown set

**Authentication errors in log**

- Verify `ClientId`, `ClientSecret` are correct
- If using password auth, check `Username` and `Password`
- If using refresh token, regenerate it — Reddit may have revoked it
- Check that your Reddit app type is set to **script**

**"Queue is currently closed" reply**

The SysBot queue lock is active. Check the WinForms UI — the queue may be paused or at its threshold limit.

**Bot replies to old messages after restart**

`reddit_seen.json` may not have been saved before the crash. This is expected for the most recent poll window — messages won't be re-processed on the next poll cycle.

**Commands from megathread aren't picked up**

- Verify the post ID in `MegathreadPostIdsCsv` is correct (test in dry-run mode)
- Make sure the subreddit is in `AllowedSubredditsCsv` if that field is set
- The bot only reads top-level comments in megathreads, not replies to comments

**`!trade` in megathread says "DM me instead"**

`RequireDmForTrades` is `true`. DM the bot account directly with `!trade` to queue.
