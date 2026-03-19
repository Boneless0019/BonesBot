# BonesBot Features: No-Code Distribution & Surprise Trade While Idle

These two features are small additions to the standard distribution and idle behavior. Both are toggled in the **Distribution Settings** section of the bot config.

---

## No-Code Distribution

### What it does

When enabled, random distribution trades (the kind that go out from the distribution pool) open a trade room **without entering any link code**. Users on the other Switch just need to search with no code to find and accept the trade. No code entry is required on either side.

Without this option, every distribution trade uses a code (either the fixed `TradeCode` setting or a random code from the `RandomCode` range).

### How to enable

In the WinForms UI, go to: **Hub → Distribution → No-Code Distribution** — set to `True`.

Or in `config.json`:
```json
"Distribution": {
  "NoCodeDistribution": true
}
```

### Per-game behavior

| Game | Behavior |
|---|---|
| **SV** | After clearing the code with X, confirms immediately with PLUS (skips keyboard entry). The Poké Portal search starts with no link code restriction — any player searching open will match. |
| **SWSH** | Skips the DDOWN press that selects "Link Code" — stays on the "Anyone" option instead. Selects "Anyone" with A, which starts an open trade search with no code required. |
| **BDSP** | Skips the DDOWN presses that select "Link Code" room — goes directly into the open room. Skips code entry entirely. |
| **LGPE** | Logs "No-code distribution: selecting 3 Pikachu stamps" — the open-room equivalent in LGPE uses 3 Pikachu stamps as the link code. This is the default open-trade behavior in LGPE anyway, so this option primarily removes confusion from the logs. |
| **LA / PLZA** | Not applicable — no link code distribution in these games. |

### Notes

- Only applies to **distribution** (random) trades, identified by `PokeTradeType.Random`. Link trades requested by specific users are unaffected.
- Overrides both the fixed `TradeCode` and the `RandomCode` range setting for distribution trades.
- Works alongside all other distribution settings (shuffle folder, ledy, etc.)

---

## Surprise Trade While Idle

### What it does

When enabled, SWSH and SV bots will automatically start doing **Surprise Trades** whenever the link trade queue is empty, instead of sitting idle waiting for the next trade. Pokémon are sent from the same distribution folder used for normal distribution trades.

As soon as a real queued trade arrives, the bot finishes (or skips) the current Surprise Trade and returns to normal link trading.

### How to enable

In the WinForms UI, go to: **Hub → Distribution → Surprise Trade While Idle** — set to `True`.

Or in `config.json`:
```json
"Distribution": {
  "SurpriseTradeWhileIdle": true
}
```

You also need a populated distribution folder. Set the folder path in **Hub → Folder → DistributeFolder**.

### Per-game behavior

| Game | Behavior |
|---|---|
| **SV** | Bot enters the Surprise Trade menu and sends a Pokémon from the distribution pool. Loops until the queue has a trade waiting. Includes a 2-minute recovery timer — if the bot gets stuck (e.g., no one accepts), it restarts the game and tries again. |
| **SWSH** | Bot uses the existing `PerformSurpriseTrade` routine. Loops until the queue has a trade waiting. |
| **BDSP / LA / LGPE / PLZA** | Not supported — only SWSH and SV bots implement this idle loop. |

### Notes

- The distribution folder must have at least one valid PKM file for this to work. If the folder is empty, the bot logs a message and returns to idle.
- Surprise Trades do not use trade codes — they match randomly with any other player searching.
- The "Surprise Trade While Idle" loop checks the queue before each trade cycle. If a queued trade arrives mid-Surprise-Trade, the current cycle finishes first, then the bot switches back to link trading.
- This setting has no effect if `DistributeWhileIdle` is `false` — the distribution pool needs to be active for Surprise Trade to have Pokémon to send.

---

## Both Settings Together

You can run both at once. Example: BDSP bot with no-code distribution enabled, SV bot with Surprise Trade While Idle enabled. They operate independently on their respective bots and don't interfere with each other.
