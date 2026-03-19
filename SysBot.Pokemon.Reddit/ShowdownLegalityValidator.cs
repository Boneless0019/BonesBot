using PKHeX.Core;
using SysBot.Base;

namespace SysBot.Pokemon.Reddit
{
    /// <summary>
    /// Validates a Showdown-format set against the legality rules of the
    /// running SysBot instance.  This validator leverages Auto-Legality Mod
    /// (ALM) to attempt to legalize the provided set.  If successful the
    /// resulting PKM is returned; otherwise an error message explains why
    /// the request is illegal.
    /// </summary>
    internal static class ShowdownLegalityValidator
    {
        /// <summary>
        /// Represents the result of a legality validation.
        /// </summary>
        public sealed record ValidationResult(bool Ok, string Message, PKM? Pokemon);

        /// <summary>
        /// Validate a Showdown set for the specified PKM type.
        /// </summary>
        /// <typeparam name="T">The PKM type corresponding to the current game.</typeparam>
        /// <param name="showdownText">The Showdown set as pasted by the user.</param>
        /// <param name="strict">If true, reject when ALM modifies species, ability, or moves.</param>
        public static ValidationResult Validate<T>(string showdownText, bool strict) where T : PKM, new()
        {
            try
            {
                showdownText = showdownText.Replace("`\n", "").Replace("\n`", "").Replace("`", "").Trim();
                var set = new ShowdownSet(showdownText);
                if (set.Species <= 0)
                    return new(false, "I couldn't parse that Showdown set. Make sure it's a valid set block.", null);

                var sav = AutoLegalityWrapper.GetTrainerInfo<T>();
                var template = AutoLegalityWrapper.GetTemplate(set);
                PKM pkm;
                string result;
                pkm = sav.GetLegal(template, out result);

                var la = new LegalityAnalysis(pkm);
                if (!la.Valid)
                {
                    // Provide user-friendly reasons for common errors
                    var reason =
                        result == "Timeout" ? "That set took too long to generate (timeout)." :
                        result == "VersionMismatch" ? "PKHeX and Auto-Legality Mod version mismatch." :
                        result == "Failed" ? AutoLegalityWrapper.GetLegalizationHint(template, sav, pkm) :
                        "I wasn't able to generate a legal result from that set.";
                    return new(false, $"Illegal request: {reason}", null);
                }

                if (strict)
                {
                    // When strict mode is enabled, ensure the legalized result matches the requested
                    // species, ability, and moves.  Reject if any are changed.
                    var repaired = new ShowdownSet(ShowdownParsing.GetShowdownText(pkm));
                    if (repaired.Species != set.Species)
                        return new(false, "Strict mode: legalization changed the species. Please fix the set.", null);
                    if (repaired.Ability != 0 && set.Ability != 0 && repaired.Ability != set.Ability)
                        return new(false, "Strict mode: legalization changed the ability. Please fix the set.", null);
                    for (int i = 0; i < 4; i++)
                    {
                        if (set.Moves[i] != 0 && repaired.Moves[i] != set.Moves[i])
                            return new(false, "Strict mode: legalization changed one or more moves. Please fix the set.", null);
                    }
                }

                return new(true, "Legal", pkm);
            }
            catch (Exception ex)
            {
                LogUtil.LogSafe(ex, nameof(ShowdownLegalityValidator));
                return new(false, "Unexpected error while checking legality. Try again, or simplify the set.", null);
            }
        }
    }
}