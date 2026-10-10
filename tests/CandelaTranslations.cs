using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Vintagestory.API.Config;
using VsTestkit.Testing;
using static VsTestkit.Testing.Vs;

namespace Candela.Tests
{
    /// <summary>
    /// Every language's Candela text, as the game loaded it and formatted as the code
    /// formats it. A plural form {pN:...} naming an argument the code does not pass
    /// throws when the line is shown, so a slip in one translation is a crash for that
    /// language's players and nobody else's.
    /// </summary>
    public class CandelaTranslations
    {
        // Arguments the code passes that the English leaves unused: the number of
        // candles CandleInfo.Append gives the four status lines.
        static readonly Dictionary<string, int> PassedBeyondEnglish = new()
        {
            ["candela:candles-burning"] = 2, ["candela:candles-snuffed"] = 2,
            ["candela:candles-guttering"] = 1, ["candela:candles-out"] = 1,
        };

        static readonly Regex Argument = new(@"\{p?(\d+)[:}]");

        [VsTest]
        public void EveryLanguageHasAllOfCandelasText()
        {
            var english = English().Keys.ToHashSet();
            foreach (var (code, language) in Lang.AvailableLanguages)
            {
                var missing = english.Where(k => !language.HasTranslation(k, false)).ToList();
                Assert.True(missing.Count == 0, code + " lacks " + string.Join(", ", missing));
            }
        }

        [VsTest]
        public void EveryLineFormatsInEveryLanguage()
        {
            var failures = new List<string>();
            foreach (var (key, english) in English())
            {
                int passed = Math.Max(
                    Argument.Matches(english).Select(m => int.Parse(m.Groups[1].Value) + 1).DefaultIfEmpty(0).Max(),
                    PassedBeyondEnglish.GetValueOrDefault(key));

                foreach (string code in Lang.AvailableLanguages.Keys)
                foreach (int n in new[] { 0, 1, 2, 5, 21 })
                {
                    try
                    {
                        // A {p...} the engine could not parse is left in the text as it was.
                        string text = Lang.GetL(code, key, Enumerable.Repeat((object)n, passed).ToArray());
                        if (text.Contains("{p")) failures.Add($"{code} {key}: {text}");
                    }
                    catch (Exception e)
                    {
                        failures.Add($"{code} {key} with {n}: {e.Message}");
                    }
                }
            }
            Assert.True(failures.Count == 0, failures.Count + " lines fail:\n" + string.Join("\n", failures.Take(20)));
        }

        [VsTest]
        public void OneHourLeftIsOneHour()
        {
            Assert.Equal("Burning - about 1 hour left", Lang.GetL("en", "candela:candles-burning", 1, 1));
            Assert.Equal("Burning - about 2 hours left", Lang.GetL("en", "candela:candles-burning", 2, 1));
        }

        static Dictionary<string, string> English() =>
            Lang.AvailableLanguages["en"].GetAllEntries().Where(e => e.Key.StartsWith("candela:")).ToDictionary(e => e.Key, e => e.Value);
    }
}
