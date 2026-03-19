using PKHeX.Core;
using System;
using System.Collections.Concurrent;

namespace SysBot.Pokemon.WebAPI;

public class WebTradeNotifier<T> : IPokeTradeNotifier<T> where T : PKM, new()
{
    private readonly string _sessionId;
    private readonly ConcurrentDictionary<string, WebSessionData> _sessions;

    public Action<PokeRoutineExecutor<T>>? OnFinish { get; set; }

    public WebTradeNotifier(string sessionId, ConcurrentDictionary<string, WebSessionData> sessions)
    {
        _sessionId = sessionId;
        _sessions = sessions;
    }

    public void TradeInitialize(PokeRoutineExecutor<T> routine, PokeTradeDetail<T> info)
        => SetStatus("Initializing", "Bot is initializing your trade...", info.Code);

    public void TradeSearching(PokeRoutineExecutor<T> routine, PokeTradeDetail<T> info)
        => SetStatus("Searching", $"Enter trade code: {info.Code:0000 0000}", info.Code);

    public void TradeCanceled(PokeRoutineExecutor<T> routine, PokeTradeDetail<T> info, PokeTradeResult msg)
        => SetStatus("Canceled", $"Trade canceled: {msg}", info.Code);

    public void TradeFinished(PokeRoutineExecutor<T> routine, PokeTradeDetail<T> info, T result)
        => SetStatus("Finished", "Trade complete! Check your game.", info.Code);

    public void SendNotification(PokeRoutineExecutor<T> routine, PokeTradeDetail<T> info, string message)
    {
        // Prefix "RETRY:" signals the bot retried with a new code — show a distinct status.
        if (message.StartsWith("RETRY:", StringComparison.Ordinal))
            SetStatus("Retrying", message["RETRY:".Length..].Trim(), info.Code);
        else
            SetStatus("Active", message, info.Code);
    }

    public void SendNotification(PokeRoutineExecutor<T> routine, PokeTradeDetail<T> info, PokeTradeSummary message)
        => SetStatus("Active", message.Summary, info.Code);

    public void SendNotification(PokeRoutineExecutor<T> routine, PokeTradeDetail<T> info, T result, string message)
        => SetStatus("Active", message, info.Code);

    private void SetStatus(string status, string message, int code)
    {
        if (_sessions.TryGetValue(_sessionId, out var existing))
            _sessions[_sessionId] = existing with { Status = status, Message = message, TradeCode = code, Position = 0 };
    }
}
