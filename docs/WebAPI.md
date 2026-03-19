# Web Trade Portal

The Web Trade Portal lets users request Pokémon trades directly from a browser — no Discord account required. It runs as an HTTP API server embedded inside SysBot and includes a built-in browser frontend.

---

## How It Works

When BonesBot starts, it automatically launches an ASP.NET HTTP server on **port 5000** (localhost). The server exposes a REST API that the frontend uses to submit trade requests, check queue status, and poll for trade progress. Because the server only binds to localhost, you need either:

- A **Cloudflare Worker** (recommended) to proxy requests from the public internet to your local server, or
- **ngrok** or another tunneling tool to expose port 5000 publicly, or
- Running the frontend locally on the same machine as the bot

---

## Setup: The API Server

No configuration is required to start the server — it starts automatically when the bot runs. You will see this in the bot log:

```
[WebAPI] Web API available at http://localhost:5000
```

### Member Key Authentication (optional)

The `/api/validate-key` endpoint lets you gate access to trades behind a key check. To use it:

1. Create a file named `member-keys.txt` in the same folder as `SysBot.exe`
2. Put one key per line — keys can be any string (UUIDs, Discord IDs, random strings, etc.)

```
abc123-member-key
xyz789-another-key
```

The frontend calls `/api/validate-key` with a key the user provides. If the key is in the file, the response is `{ "valid": true }`. Your frontend can use this to gate the trade form. Keys are case-insensitive.

If `member-keys.txt` does not exist, all key validation returns `{ "valid": false }`.

---

## Setup: Pokémon Catalog

The catalog lets you pre-load specific Pokémon files that users can browse and pick from instead of writing a Showdown Set.

1. Create a folder named `pokemon-catalog` in the same folder as `SysBot.exe`
2. Drop `.pk8`, `.pk9`, `.pb8`, `.pa8`, `.pb7` files into it

The bot reads each file on every `/api/catalog` request, determines the species name, display name, Dex number, and shiny status automatically. The catalog is served sorted by Dex number and then display name.

Files with invalid or unreadable data are silently skipped.

---

## Setup: Cloudflare Worker (Public Access)

The Cloudflare Worker in `SysBot.Pokemon.WebAPI/frontend/worker.js` proxies public browser requests to your local SysBot API. It adds CORS headers so browsers can talk to it.

### Requirements
- A Cloudflare account (free tier works)
- [Wrangler CLI](https://developers.cloudflare.com/workers/wrangler/install-and-update/) installed: `npm install -g wrangler`

### Steps

1. Open a terminal in `SysBot.Pokemon.WebAPI/frontend/`

2. Log in to Cloudflare:
   ```bash
   wrangler login
   ```

3. Set the secret URL — this is your SysBot's public address (ngrok URL, VPS IP, etc.):
   ```bash
   wrangler secret put SYSBOT_URL
   # When prompted, enter: http://YOUR_PUBLIC_IP:5000
   ```

4. Deploy the worker:
   ```bash
   wrangler deploy
   ```

5. Wrangler will give you a worker URL like `https://sysbot-proxy.yourname.workers.dev`. Point your frontend's `API` constant at this URL.

If you restart ngrok or your IP changes, update the secret:
```bash
wrangler secret put SYSBOT_URL
```

---

## Setup: Frontend

The frontend (`SysBot.Pokemon.WebAPI/frontend/index.html`) is a single HTML file. Open it in any browser.

By default it points to `http://localhost:5000` — this works if you're running the frontend on the same machine as the bot. If you're hosting it for others, change the `API` constant near the top of the `<script>` block to your Cloudflare Worker URL:

```js
const API = 'https://sysbot-proxy.yourname.workers.dev';
```

You can host `index.html` anywhere — GitHub Pages, Cloudflare Pages, or just share the file directly.

---

## API Reference

All endpoints return JSON. The server runs at `http://localhost:5000` by default.

---

### `GET /api/status`

Returns the current bot status.

**Response:**
```json
{
  "online": true,
  "canQueue": true,
  "queueCount": 3,
  "botCount": 1,
  "game": "SV"
}
```

| Field | Description |
|---|---|
| `online` | Always `true` when the server is running |
| `canQueue` | Whether the queue is currently accepting new trades |
| `queueCount` | Number of trades currently in the queue |
| `botCount` | Number of active bots |
| `game` | Game version string: `SWSH`, `BDSP`, `LA`, `SV`, `PLZA`, `LGPE` |

---

### `GET /api/catalog`

Returns the list of Pokémon available in the catalog folder.

**Response:**
```json
[
  {
    "fileName": "Pikachu.pk9",
    "displayName": "Pikachu",
    "species": "Pikachu",
    "isShiny": false,
    "dexNumber": 25
  },
  {
    "fileName": "Shiny_Charizard.pk9",
    "displayName": "Charizard ★",
    "species": "Charizard",
    "isShiny": true,
    "dexNumber": 6
  }
]
```

Returns an empty array `[]` if the catalog folder doesn't exist or contains no valid PKM files.

---

### `POST /api/trade`

Submit a trade request. Accepts either a Showdown Set or a catalog file name.

**Request body (Showdown Set):**
```json
{
  "trainerName": "Ash",
  "showdownSet": "Pikachu @ Light Ball\nAbility: Static\n..."
}
```

**Request body (catalog file):**
```json
{
  "trainerName": "Ash",
  "fileName": "Pikachu.pk9"
}
```

`trainerName` is optional — defaults to `"WebUser"` if omitted.

**Response (success):**
```json
{
  "success": true,
  "sessionId": "web-00000001",
  "tradeCode": 12345678,
  "position": 2,
  "message": "Added to queue at position 2! Trade code: 1234 5678"
}
```

**Response (failure):**
```json
{
  "success": false,
  "sessionId": null,
  "tradeCode": 0,
  "position": 0,
  "message": "This Pokémon is not legal: ..."
}
```

Save the `sessionId` — you need it to poll for trade status and to cancel the trade.

**HTTP status codes:**
- `200` — Request processed (check `success` field)
- `400` — Malformed JSON or missing required field
- `503` — Queue is closed or bot not ready

---

### `GET /api/trade/{sessionId}`

Poll the status of a queued trade.

**Example:** `GET /api/trade/web-00000001`

**Response:**
```json
{
  "status": "Queued",
  "message": "In queue at position 2. Your trade code is 1234 5678.",
  "position": 2,
  "tradeCode": 12345678
}
```

**Status values:**

| Status | Meaning |
|---|---|
| `Queued` | Waiting in the trade queue |
| `Trading` | Bot is actively trading with this user |
| `Completed` | Trade finished successfully |
| `Canceled` | Trade was canceled or timed out |
| `NotFound` | Session ID not recognized (may have expired) |

Poll this endpoint every 5–15 seconds to show the user their queue position and trade progress.

---

### `DELETE /api/trade/{sessionId}`

Cancel a queued trade and remove it from the queue.

**Example:** `DELETE /api/trade/web-00000001`

**Response:**
```json
{
  "success": true,
  "message": "Removed from queue."
}
```

**Possible messages:**
- `"Removed from queue."` — Successfully removed
- `"Trade is currently processing and cannot be removed."` — Bot is mid-trade, cannot cancel
- `"Removed while processing."` — Removed during processing
- `"Not currently in queue."` — Already completed or never queued

---

### `GET /api/queue`

Returns a list of the current queue (up to 20 entries) as formatted strings.

**Response:**
```json
[
  "Ash — Pikachu (#1)",
  "Misty — Psyduck (#2)"
]
```

---

### `POST /api/validate-key`

Checks whether a member key is valid against `member-keys.txt`.

**Request body:**
```json
{ "key": "abc123-member-key" }
```

**Response:**
```json
{ "valid": true }
```

Returns `{ "valid": false }` if the key is not found or if `member-keys.txt` doesn't exist.

---

## Showdown Set Format

The `showdownSet` field accepts standard Pokémon Showdown export format. Example:

```
Garchomp @ Choice Scarf
Ability: Rough Skin
Level: 100
Shiny: Yes
EVs: 252 Atk / 4 SpD / 252 Spe
Jolly Nature
- Earthquake
- Dragon Claw
- Fire Fang
- Stealth Rock
```

The bot runs this through PKHeX AutoLegality to generate a legal PKM file. If the set cannot be made legal for the current game, the trade is rejected with an explanation.

---

## Troubleshooting

**"Queue is currently closed" — 503 response**
The bot's queue lock is active. This happens when the bot is paused, stopped, or the queue threshold is hit. Check the WinForms UI.

**"Could not create a legal [species] from that set"**
The Showdown Set has a move, ability, or property not available in the current game. Double-check the set in Showdown or PKHeX.

**"This Pokémon file is not legal"**
A catalog PKM file failed legality. Remove or replace the file in `pokemon-catalog/`.

**CORS errors in browser**
You are running the frontend from a different origin than the API. Deploy the Cloudflare Worker and point the `API` constant at it.

**Worker returns "Bot unreachable"**
The `SYSBOT_URL` secret in your worker points to an address SysBot isn't listening on. Verify the bot is running and the IP/port is correct. Update the secret with `wrangler secret put SYSBOT_URL`.
