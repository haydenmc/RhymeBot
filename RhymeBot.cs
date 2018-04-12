using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Bot;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Schema;
using Newtonsoft.Json;

namespace RhymeBot
{
    public class RhymeBot : IBot
    {
        private const string rhymeApiBaseUrl = "http://rhymebrain.com/talk";
        private const string wordMatchPattern = @"[a-zA-Z']+";
        private readonly int minRhymeScore = 250;

        private class RhymeMatch
        {
            public int Index;
            public string Word;
        }

        private class RhymeModel
        {
            [JsonProperty("flags")]
            public string Flags { get; set; }
            
            [JsonProperty("freq")]
            public int Frequency { get; set; }
            
            [JsonProperty("score")]
            public int Score { get; set; }
            
            [JsonProperty("syllables")]
            public int Syllables { get; set; }
            
            [JsonProperty("word")]
            public string Word { get; set; }
        }

        async Task IBot.OnTurn(ITurnContext turnContext)
        {
            // Initial prompt
            if (turnContext.Activity.Type == ActivityTypes.Invoke)
            {
                await turnContext.SendActivity("Hello! What would you like rhymed?");
            }
            // When the user sends text
            else if (turnContext.Activity.Type == ActivityTypes.Message)
            {
                await turnContext.SendActivity(await getRhyme(turnContext.Activity.Text));
            }
            else
            {
                await turnContext.SendActivity("Test!");
            }
        }

        private async Task<string> getRhyme(string input)
        {
            // Split up sentence into words
            var originalWordMatches = Regex
                .Matches(input, wordMatchPattern)
                .Cast<Match>()
                .Select(m => new RhymeMatch() { Index = m.Index, Word = m.Value })
                .ToList();

            // Generate a query string for the API
            var queryString = new StringBuilder("");
            for (var i = 0; i < originalWordMatches.Count; i++)
            {
                if (i > 0)
                {
                    queryString.Append("&next&function=getRhymes");
                }
                else
                {
                    queryString.Append("?function=getRhymes");
                }
                queryString.Append("&word=").Append(Uri.EscapeUriString(originalWordMatches[i].Word));
            }

            // Run the query
            using (var httpClient = new HttpClient())
            {
                httpClient.BaseAddress = new Uri(rhymeApiBaseUrl);
                var result = await httpClient.GetAsync(queryString.ToString());
                var resultStr = await result.Content.ReadAsStringAsync();
                List<List<RhymeModel>> resultRhymes;
                if (originalWordMatches.Count > 1)
                {
                    resultRhymes = JsonConvert.DeserializeObject<List<List<RhymeModel>>>(resultStr);
                }
                else
                {
                    resultRhymes = new List<List<RhymeModel>>()
                    {
                        JsonConvert.DeserializeObject<List<RhymeModel>>(resultStr)
                    };
                }
                var newMessage = input;
                int wordsRhymed = 0;
                for (var i = 0; i < resultRhymes.Count; i++)
                {
                    var wordRhymes = resultRhymes[i];
                    var syllables = CountSyllables(originalWordMatches[i].Word);
                    var rhymingWord = wordRhymes
                        .Where(w => w.Syllables == syllables)
                        .Where(w => w.Score > minRhymeScore)
                        .OrderBy(r => Guid.NewGuid())
                        .FirstOrDefault();
                    if (rhymingWord != null)
                    {
                        // Get length delta
                        int lengthDelta = rhymingWord.Word.Length - originalWordMatches[i].Word.Length;
                        // Replace it!
                        newMessage = newMessage
                            .Remove(originalWordMatches[i].Index, originalWordMatches[i].Word.Length)
                            .Insert(originalWordMatches[i].Index, rhymingWord.Word);
                        // Bump indexes of other words
                        for (var j = i + 1; j < originalWordMatches.Count; j++)
                        {
                            originalWordMatches[j].Index += lengthDelta;
                        }
                        wordsRhymed++;
                    }
                }
                return newMessage;
            }
        }

        /// <summary>
        /// Guesses number of syllables in a given word
        /// Thanks to Joe Basirico
        /// http://stackoverflow.com/a/5615724/2874534
        /// </summary>
        /// <param name="word">Word</param>
        /// <returns>Syllable count</returns>
        private int CountSyllables(string word)
        {
            char[] vowels = { 'a', 'e', 'i', 'o', 'u', 'y' };
            string currentWord = word.ToLowerInvariant();
            int numVowels = 0;
            bool lastWasVowel = false;
            foreach (char wc in currentWord)
            {
                bool foundVowel = false;
                foreach (char v in vowels)
                {
                    //don't count diphthongs
                    if (v == wc && lastWasVowel)
                    {
                        foundVowel = true;
                        lastWasVowel = true;
                        break;
                    }
                    else if (v == wc && !lastWasVowel)
                    {
                        numVowels++;
                        foundVowel = true;
                        lastWasVowel = true;
                        break;
                    }
                }

                //if full cycle and no vowel found, set lastWasVowel to false;
                if (!foundVowel)
                {
                    lastWasVowel = false;
                }
            }
            //remove es, it's _usually? silent
            if (currentWord.Length > 2 && 
                currentWord.Substring(currentWord.Length - 2) == "es")
            {
                numVowels--;
            }
                
            // remove silent e
            else if (currentWord.Length > 1 &&
                currentWord.Substring(currentWord.Length - 1) == "e")
            {
                numVowels--;
            }   

            return numVowels;
        }
    }
}
