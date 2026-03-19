using PKHeX.Core;
using SysBot.Base;
using System.Threading.Tasks;

namespace SysBot.Pokemon.Reddit
{
    /// <summary>
    /// Sends trade status updates back to a Reddit user.  The queue will
    /// invoke this notifier as the trade progresses.  All messages are
    /// delivered via DM to avoid cluttering public threads.
    /// </summary>
    internal sealed class RedditTradeNotifier<T> : IPokeTradeNotifier<T> where T : PKM, new()
    {
        private readonly RedditApiClient _api;
        private readonly string _username;

        public RedditTradeNotifier(RedditApiClient api, string username)
        {
            _api = api;
            _username = username;
        }

        public Action<PokeRoutineExecutor<T>>? OnFinish { private get; set; }

        public void SendNotification(PokeRoutineExecutor<T> routine, PokeTradeDetail<T> info, string message) =>
            _ = _api.SendDmAsync(_username, "Update", message, CancellationToken.None);

        public void SendNotification(PokeRoutineExecutor<T> routine, PokeTradeDetail<T> info, PokeTradeSummary message) =>
            _ = _api.SendDmAsync(_username, "Update", message.ToString(), CancellationToken.None);

        public void SendNotification(PokeRoutineExecutor<T> routine, PokeTradeDetail<T> info, T result, string message) =>
            _ = _api.SendDmAsync(_username, "Update", message, CancellationToken.None);

        public void TradeCanceled(PokeRoutineExecutor<T> routine, PokeTradeDetail<T> info, PokeTradeResult msg) =>
            _ = _api.SendDmAsync(_username, "Canceled", $"❌ Trade canceled: {msg}", CancellationToken.None);

        public void TradeFinished(PokeRoutineExecutor<T> routine, PokeTradeDetail<T> info, T result) =>
            _ = _api.SendDmAsync(_username, "Complete", "✅ Trade complete!", CancellationToken.None);

        public void TradeInitialize(PokeRoutineExecutor<T> routine, PokeTradeDetail<T> info) =>
            _ = _api.SendDmAsync(_username, "Starting", "🎮 Starting your trade. Please search with your link code now.", CancellationToken.None);

        public void TradeSearching(PokeRoutineExecutor<T> routine, PokeTradeDetail<T> info) =>
            _ = _api.SendDmAsync(_username, "Searching", "🔎 Searching for you…", CancellationToken.None);
    }
}