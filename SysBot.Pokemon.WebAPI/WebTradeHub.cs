using PKHeX.Core;
using SysBot.Base;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading;

namespace SysBot.Pokemon.WebAPI;

public record WebSessionData(ulong UserId, string Status, string Message, int Position, int TradeCode);

public record WebTradeSubmitResult(bool Success, string? SessionId, int TradeCode, int Position, string Message);

public record CatalogEntry(string FileName, string DisplayName, string Species, bool IsShiny, int DexNumber);

public interface IWebTradeHub
{
    bool CanQueue { get; }
    int TradeQueueCount { get; }
    int BotCount { get; }
    string GameVersion { get; }
    WebTradeSubmitResult SubmitShowdownTrade(string trainerName, string showdownSet);
    WebTradeSubmitResult SubmitFileTrade(string trainerName, string fileName);
    CatalogEntry[] GetCatalog();
    bool RemoveTrade(string sessionId, out string message);
    WebSessionData GetStatus(string sessionId);
    string[] GetQueueList();
}

public class WebTradeHub<T> : IWebTradeHub where T : PKM, new()
{
    private readonly PokeTradeHub<T> _hub;
    private readonly ConcurrentDictionary<string, WebSessionData> _sessions = new();
    private static long _sessionCounter = 0;
    private static long _userIdBase = 9_000_000_000_000_000L;

    public static string CatalogFolder => Path.Combine(AppContext.BaseDirectory, "pokemon-catalog");

    public WebTradeHub(PokeTradeHub<T> hub) => _hub = hub;

    public bool CanQueue => _hub.Queues.Info.GetCanQueue();
    public int TradeQueueCount => _hub.Queues.Info.Count;
    public int BotCount => _hub.Bots.Count;
    public string GameVersion => typeof(T).Name switch
    {
        "PK8" => "SWSH",
        "PA8" => "PLA",
        "PB8" => "BDSP",
        "PK9" => "SV",
        "PA9" => "PLZA",
        "PB7" => "LGPE",
        _ => "Unknown",
    };

    public CatalogEntry[] GetCatalog()
    {
        if (!Directory.Exists(CatalogFolder))
            return [];

        return Directory.GetFiles(CatalogFolder)
            .Select(f =>
            {
                try
                {
                    var bytes = File.ReadAllBytes(f);
                    var prefer = EntityFileExtension.GetContextFromExtension(f);
                    var pkm = EntityFormat.GetFromBytes(bytes, prefer);
                    if (pkm == null) return null;
                    var speciesName = GameInfo.Strings.Species[pkm.Species];
                    var displayName = !string.IsNullOrWhiteSpace(pkm.Nickname) && pkm.Nickname != speciesName
                        ? $"{pkm.Nickname} ({speciesName})"
                        : speciesName;
                    if (pkm.IsShiny) displayName += " ★";
                    return new CatalogEntry(Path.GetFileName(f), displayName, speciesName, pkm.IsShiny, pkm.Species);
                }
                catch { return null; }
            })
            .OfType<CatalogEntry>()
            .OrderBy(e => e.DexNumber)
            .ThenBy(e => e.DisplayName)
            .ToArray();
    }

    public WebTradeSubmitResult SubmitFileTrade(string trainerName, string fileName)
    {
        try
        {
            // Sanitize: strip any path components to prevent traversal
            var safeName = Path.GetFileName(fileName);
            var filePath = Path.Combine(CatalogFolder, safeName);

            if (!File.Exists(filePath))
                return Fail("Pokémon file not found in catalog.");

            var bytes = File.ReadAllBytes(filePath);
            var prefer = EntityFileExtension.GetContextFromExtension(filePath);
            var pkm = EntityFormat.GetFromBytes(bytes, prefer);
            if (pkm == null)
                return Fail("Could not read Pokémon file.");

            var converted = EntityConverter.ConvertToType(pkm, typeof(T), out _);
            if (converted is not T pk)
                return Fail("Could not convert this Pokémon to the correct game format.");

            var la = new LegalityAnalysis(pk);
            if (!la.Valid)
                return Fail($"This Pokémon file is not legal: {la.Report()}");

            var enc = la.EncounterOriginal;
            if (!pk.CanBeTraded(enc))
                return Fail("This Pokémon cannot be traded.");

            var cfg = _hub.Config.Trade;
            if (cfg.DisallowNonNatives && (enc.Context != pk.Context || pk.GO))
                return Fail($"{typeof(T).Name} is not native to this game.");
            if (cfg.DisallowTracked && pk is IHomeTrack { HasTracker: true })
                return Fail("This Pokémon already has a HOME Tracker and cannot be traded.");

            pk.ResetPartyStats();

            return EnqueuePokemon(pk, trainerName);
        }
        catch (Exception ex)
        {
            LogUtil.LogSafe(ex, "WebTradeHub");
            return Fail($"Error: {ex.Message}");
        }
    }

    public WebTradeSubmitResult SubmitShowdownTrade(string trainerName, string showdownSet)
    {
        try
        {
            var set = new ShowdownSet(showdownSet);
            if (set.Species is 0)
                return Fail("Could not identify Pokémon species. Check your Showdown Set.");

            if (set.InvalidLines.Count > 0)
            {
                var lines = string.Join(", ", set.InvalidLines.Take(3).Select(l => l.ToString()));
                return Fail($"Invalid lines in Showdown Set: {lines}");
            }

            var template = AutoLegalityWrapper.GetTemplate(set);
            var sav = AutoLegalityWrapper.GetTrainerInfo<T>();
            var pkm = sav.GetLegal(template, out var result);

            var la = new LegalityAnalysis(pkm);
            pkm = EntityConverter.ConvertToType(pkm, typeof(T), out _) ?? pkm;

            if (pkm is not T pk || !la.Valid)
            {
                var specName = GameInfo.Strings.Species[template.Species];
                var reason = result switch
                {
                    "Timeout" => $"That {specName} set took too long to generate.",
                    "VersionMismatch" => "PKHeX and Auto-Legality Mod version mismatch.",
                    _ => $"Could not create a legal {specName} from that set.",
                };
                if (result == "Failed")
                    reason += " " + AutoLegalityWrapper.GetLegalizationHint(template, sav, pkm);
                return Fail(reason);
            }

            var enc = la.EncounterOriginal;
            if (!pk.CanBeTraded(enc))
                return Fail("This Pokémon cannot be traded.");

            var cfg = _hub.Config.Trade;
            if (cfg.DisallowNonNatives && (enc.Context != pk.Context || pk.GO))
                return Fail($"{typeof(T).Name} is not native to this game.");
            if (cfg.DisallowTracked && pk is IHomeTrack { HasTracker: true })
                return Fail("This Pokémon already has a HOME Tracker and cannot be traded.");

            pk.ResetPartyStats();

            return EnqueuePokemon(pk, trainerName);
        }
        catch (Exception ex)
        {
            LogUtil.LogSafe(ex, "WebTradeHub");
            return Fail($"Error: {ex.Message}");
        }
    }

    private WebTradeSubmitResult EnqueuePokemon(T pk, string trainerName)
    {
        var uid = (ulong)Interlocked.Increment(ref _userIdBase);
        var sessionNum = Interlocked.Increment(ref _sessionCounter);
        var sessionId = $"web-{sessionNum:X8}";
        var code = _hub.Config.Trade.GetRandomTradeCode();

        var notifier = new WebTradeNotifier<T>(sessionId, _sessions);
        var trainer = new PokeTradeTrainerInfo(trainerName, uid);
        var detail = new PokeTradeDetail<T>(pk, trainer, notifier, PokeTradeType.Specific, code);
        var entry = new TradeEntry<T>(detail, uid, PokeRoutineType.LinkTrade, trainerName);

        var info = _hub.Queues.Info;
        var addResult = info.AddToTradeQueue(entry, uid);

        if (addResult == QueueResultAdd.AlreadyInQueue)
            return Fail("You are already in the queue.");

        var position = info.CheckPosition(uid, PokeRoutineType.LinkTrade);
        var posNum = position.InQueue ? position.Position : 1;

        _sessions[sessionId] = new WebSessionData(uid, "Queued",
            $"In queue at position {posNum}. Your trade code is {code:0000 0000}.", posNum, code);

        return new WebTradeSubmitResult(true, sessionId, code, posNum,
            $"Added to queue at position {posNum}! Trade code: {code:0000 0000}");
    }

    public bool RemoveTrade(string sessionId, out string message)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
        {
            message = "Session not found.";
            return false;
        }

        var result = _hub.Queues.Info.ClearTrade(session.UserId);
        _sessions.TryRemove(sessionId, out _);

        message = result switch
        {
            QueueResultRemove.Removed => "Removed from queue.",
            QueueResultRemove.CurrentlyProcessing => "Trade is currently processing and cannot be removed.",
            QueueResultRemove.CurrentlyProcessingRemoved => "Removed while processing.",
            QueueResultRemove.NotInQueue => "Not currently in queue.",
            _ => "Unknown result.",
        };

        return result is QueueResultRemove.Removed or QueueResultRemove.CurrentlyProcessingRemoved;
    }

    public WebSessionData GetStatus(string sessionId)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
            return new WebSessionData(0, "NotFound", "Session not found.", 0, 0);

        if (session.Status == "Queued")
        {
            var pos = _hub.Queues.Info.CheckPosition(session.UserId, PokeRoutineType.LinkTrade);
            if (pos.InQueue && pos.Position != session.Position)
            {
                var updated = session with
                {
                    Position = pos.Position,
                    Message = $"In queue at position {pos.Position}. Your trade code is {session.TradeCode:0000 0000}.",
                };
                _sessions[sessionId] = updated;
                return updated;
            }
        }

        return session;
    }

    public string[] GetQueueList()
        => _hub.Queues.Info.GetUserList("{3} — {4} (#{0})")
            .Take(20)
            .ToArray();

    private static WebTradeSubmitResult Fail(string msg)
        => new(false, null, 0, 0, msg);
}
