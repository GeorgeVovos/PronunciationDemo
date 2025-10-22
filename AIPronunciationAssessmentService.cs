using DiffPlex;
using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;
using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;
using Microsoft.CognitiveServices.Speech.PronunciationAssessment;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace PronunciationDemo
{
    public class AIPronunciationAssessmentService
    {
        private readonly string subscriptionKey;
        private readonly string serviceRegion;
        private readonly string audioFilePath;
        private readonly string referenceText;

        public AIPronunciationAssessmentService(string subscriptionKey, string serviceRegion, string audioFilePath, string referenceText)
        {
            this.subscriptionKey = subscriptionKey;
            this.serviceRegion = serviceRegion;
            this.audioFilePath = audioFilePath;
            this.referenceText = referenceText;
        }

        public async Task<PronunciationResult> PronunciationAssessmentContinuousWithFile()
        {
            if (!File.Exists(audioFilePath))
                throw new Exception("Audio file not found:" + audioFilePath);

            Console.WriteLine($"Processing audio file: {audioFilePath}");

            string AllJsonFromAPI = "{[";
            var config = SpeechConfig.FromSubscription(subscriptionKey, serviceRegion);
            config.OutputFormat = OutputFormat.Detailed;

            using (var audioInput = AudioConfig.FromWavFileInput(audioFilePath))
            {
                var language = "en-US";
                bool enableMiscue = true;
                using (var recognizer = new SpeechRecognizer(config, language, audioInput))
                {
                    //var pronConfig = new PronunciationAssessmentConfig("Today was a beautiful day. we had a great time", GradingSystem.HundredMark, Granularity.Phoneme, enableMiscue);
                    var pronConfig = new PronunciationAssessmentConfig("", GradingSystem.HundredMark, Granularity.Phoneme, enableMiscue);
                  
                    pronConfig.EnableProsodyAssessment();
                    pronConfig.ApplyTo(recognizer);

                    var recognizedWords = new List<string>();
                    var pronWords = new List<PronunciationWord>();
                    var finalWords = new List<PronunciationWord>();
                    var fluency_scores = new List<double>();
                    var prosody_scores = new List<double>();
                    var durations = new List<int>();
                    var done = false;


                    var gapAfterMs = new List<double>();
                    var indexOfPronWord = new Dictionary<PronunciationWord, int>();
                    long lastEndTicks = -1;
                    int lastPronWordIndex = -1;

                    // Thresholds per Azure docs (0-1 range). Use JSON sample structure.
                    // https://learn.microsoft.com/azure/ai-services/speech-service/how-to-pronunciation-assessment#get-pronunciation-assessment-results
                    double unexpectedBreakThreshold = 5;
                    double missingBreakThreshold = 5;
                    // Pause threshold (ms) to consider a real audible break at punctuation
                    double breakPauseThresholdMs = 29.0; // Lowered to be more sensitive to actual pauses

                    recognizer.SessionStopped += (s, e) =>
                    {
                        done = true;
                    };

                    recognizer.Canceled += (s, e) =>
                    {
                        Console.WriteLine($"Recognition canceled: {e.Reason}");
                        if (e.Reason == CancellationReason.Error)
                        {
                            Console.WriteLine($"Error details: {e.ErrorDetails}");
                        }
                        done = true;
                    };

                    recognizer.Recognized += (s, e) =>
                    {
                        Console.WriteLine($"RECOGNIZED: Text={e.Result.Text}");
                        var pronResult = PronunciationAssessmentResult.FromResult(e.Result);
                        Console.WriteLine($"    Accuracy score: {pronResult.AccuracyScore}, pronunciation score: {pronResult.PronunciationScore}, completeness score: {pronResult.CompletenessScore}, fluency score: {pronResult.FluencyScore}, prosody score: {pronResult.ProsodyScore}");

                        fluency_scores.Add(pronResult.FluencyScore);
                        prosody_scores.Add(pronResult.ProsodyScore);

                        // Keep the base index before we append words from this event
                        int baseIndex = pronWords.Count;

                        foreach (var word in pronResult.Words)
                        {
                            int idx = pronWords.Count;
                            var newWord = new PronunciationWord(word.Word, word.ErrorType, word.AccuracyScore);
                            pronWords.Add(newWord);
                            indexOfPronWord[newWord] = idx;
                            gapAfterMs.Add(double.NaN); // placeholder for pause after this word
                        }

                        // Parse detailed JSON to get Prosody -> Break feedback per word
                        try
                        {
                            var json = e.Result.Properties.GetProperty(PropertyId.SpeechServiceResponse_JsonResult);
                            if (!string.IsNullOrEmpty(json))
                            {
                                AllJsonFromAPI = AllJsonFromAPI + json + "," + Environment.NewLine;
                                using (var doc = JsonDocument.Parse(json))
                                {
                                    var root = doc.RootElement;
                                    if (root.TryGetProperty("NBest", out var nbest) && nbest.ValueKind == JsonValueKind.Array && nbest.GetArrayLength() > 0)
                                    {
                                        var best = nbest[0];
                                        if (best.TryGetProperty("Words", out var wordsEl) && wordsEl.ValueKind == JsonValueKind.Array)
                                        {
                                            int wordsCount = wordsEl.GetArrayLength();
                                            int safeCount = Math.Min(wordsCount, pronWords.Count - baseIndex);

                                            for (int i = 0; i < safeCount; i++)
                                            {
                                                var wordEl = wordsEl[i];

                                                // Try multiple JSON paths since SDK structure varies
                                                double unexpectedConf = TryGetDouble(wordEl, "PronunciationAssessment", "Feedback", "Prosody", "Break", "UnexpectedBreak", "Confidence");
                                                double missingConf = TryGetDouble(wordEl, "PronunciationAssessment", "Feedback", "Prosody", "Break", "MissingBreak", "Confidence");

                                                // Fallback paths
                                                if (double.IsNaN(unexpectedConf))
                                                    unexpectedConf = TryGetDouble(wordEl, "Prosody", "Feedback", "Break", "UnexpectedBreak", "Confidence");
                                                if (double.IsNaN(missingConf))
                                                    missingConf = TryGetDouble(wordEl, "Prosody", "Feedback", "Break", "MissingBreak", "Confidence");

                                                if (unexpectedConf > 5)
                                                {
                                                    int x = 3;
                                                }
                                                // Check ErrorTypes array too
                                                bool sdkUnexpected = false;
                                                bool sdkMissing = false;
                                                bool sdkMonotone = false;

                                                // Try primary path first for Break errors
                                                JsonElement breakEl;
                                                if (TryGetElement(wordEl, out breakEl, "PronunciationAssessment", "Feedback", "Prosody", "Break") ||
                                                    TryGetElement(wordEl, out breakEl, "Prosody", "Feedback", "Break"))
                                                {
                                                    if (breakEl.TryGetProperty("ErrorTypes", out var errArr) && errArr.ValueKind == JsonValueKind.Array)
                                                    {
                                                        foreach (var et in errArr.EnumerateArray())
                                                        {
                                                            var etv = et.GetString();
                                                            if (string.Equals(etv, "MissingBreak", StringComparison.OrdinalIgnoreCase)) sdkMissing = true;
                                                            if (string.Equals(etv, "UnexpectedBreak", StringComparison.OrdinalIgnoreCase)) sdkUnexpected = true;
                                                        }
                                                    }
                                                }

                                                // Check for Monotone errors in Intonation section
                                                JsonElement intonationEl;
                                                if (TryGetElement(wordEl, out intonationEl, "PronunciationAssessment", "Feedback", "Prosody", "Intonation") ||
                                                    TryGetElement(wordEl, out intonationEl, "Prosody", "Feedback", "Intonation"))
                                                {
                                                    if (intonationEl.TryGetProperty("ErrorTypes", out var intonationErrArr) && intonationErrArr.ValueKind == JsonValueKind.Array)
                                                    {
                                                        foreach (var et in intonationErrArr.EnumerateArray())
                                                        {
                                                            var etv = et.GetString();
                                                            if (string.Equals(etv, "Monotone", StringComparison.OrdinalIgnoreCase)) sdkMonotone = true;
                                                        }
                                                    }
                                                }

                                                if (!double.IsNaN(unexpectedConf)) sdkUnexpected |= unexpectedConf >= unexpectedBreakThreshold;
                                                if (!double.IsNaN(missingConf)) sdkMissing |= missingConf >= missingBreakThreshold;

                                                var target = pronWords[baseIndex + i];
                                                var wordText = target.WordText ?? "unknown";

                                                // Attach errors to current word, prioritizing existing SDK errors
                                                if (sdkMonotone && (string.IsNullOrEmpty(target.ErrorType) || target.ErrorType == "None"))
                                                {
                                                    target.ErrorType = "Monotone";
                                                }
                                                else if (sdkMissing && (string.IsNullOrEmpty(target.ErrorType) || target.ErrorType == "None"))
                                                {
                                                    target.ErrorType = "MissingBreak";
                                                }
                                                else if (sdkUnexpected && (string.IsNullOrEmpty(target.ErrorType) || target.ErrorType == "None"))
                                                {
                                                    target.ErrorType = "UnexpectedBreak";
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Failed to parse prosody feedback JSON: {ex.Message}");
                        }

                        // Capture timing and compute gaps from the top alternative only
                        var topAlt = e.Result.Best().FirstOrDefault();
                        if (topAlt != null && topAlt.Words != null && topAlt.Words.Count() > 0)
                        {
                            var altWordsList = topAlt.Words.ToList();
                            int safeCount2 = Math.Min(altWordsList.Count, pronWords.Count - baseIndex);

                            // Cross-segment gap: compute gap for the previous last word using the first word start of current segment
                            if (lastPronWordIndex >= 0 && lastEndTicks > 0 && altWordsList.Count > 0)
                            {
                                long firstStart = altWordsList[0].Offset;
                                if (firstStart >= lastEndTicks)
                                {
                                    double crossSegmentGap = (firstStart - lastEndTicks) / 10000.0;
                                    gapAfterMs[lastPronWordIndex] = crossSegmentGap;
                                }
                            }

                            for (int i = 0; i < safeCount2; i++)
                            {
                                var current = altWordsList[i];
                                if (i < safeCount2 - 1)
                                {
                                    var next = altWordsList[i + 1];
                                    long currentEnd = current.Offset + current.Duration;
                                    double gap = (next.Offset - currentEnd) / 10000.0;
                                    gapAfterMs[baseIndex + i] = gap;
                                }
                            }

                            // Update tracking for next segment
                            if (safeCount2 > 0)
                            {
                                var last = altWordsList[safeCount2 - 1];
                                lastEndTicks = last.Offset + last.Duration;
                                lastPronWordIndex = baseIndex + (safeCount2 - 1);
                            }

                            // Also collect aggregate duration and words for reporting as before
                            durations.Add(altWordsList.Sum(item => (int)item.Duration));
                            recognizedWords.AddRange(altWordsList.Select(item => item.Word).ToList());
                        }
                        else
                        {
                            foreach (var recognitionResult in e.Result.Best())
                            {
                                durations.Add(recognitionResult.Words.Sum(item => item.Duration));
                                recognizedWords.AddRange(recognitionResult.Words.Select(item => item.Word).ToList());
                            }
                        }
                    };

                    // Starts continuous recognition.
                    await recognizer.StartContinuousRecognitionAsync().ConfigureAwait(false);

                    while (!done)
                    {
                        // Allow the program to run and process results continuously.
                        await Task.Delay(1000); // Adjust the delay as needed.
                    }

                    // Waits for completion.
                    await recognizer.StopContinuousRecognitionAsync().ConfigureAwait(false);

                    // For continuous pronunciation assessment mode,
                    // the service won't return the words with `Insertion` or `Omission`
                    // even if miscue is enabled.

                    // We need to compare with the reference text after received all recognized words to get these error words.
                    string[] referenceWords = referenceText.ToLower().Split(' ');
                    for (int j = 0; j < referenceWords.Length; j++)
                    {
                        referenceWords[j] = Regex.Replace(referenceWords[j], "^[\\p{P}\\s]+|[\\p{P}\\s]+$", "");
                    }

                    if (enableMiscue)
                    {
                        var differ = new Differ();
                        var inlineBuilder = new InlineDiffBuilder(differ);
                        var diffModel = inlineBuilder.BuildDiffModel(string.Join("\n", referenceWords), string.Join("\n", recognizedWords));

                        int currentIdx = 0;

                        foreach (var delta in diffModel.Lines)
                        {
                            if (delta.Type == ChangeType.Unchanged)
                            {
                                finalWords.Add(pronWords[currentIdx]);
                                currentIdx += 1;
                            }

                            if (delta.Type == ChangeType.Deleted || delta.Type == ChangeType.Modified)
                            {
                                var word = new PronunciationWord(delta.Text, "Omission");
                                finalWords.Add(word);
                            }

                            if (delta.Type == ChangeType.Inserted || delta.Type == ChangeType.Modified)
                            {
                                PronunciationWord w = pronWords[currentIdx];
                                // Preserve any existing break/monotone errors; only mark as Insertion when None/empty
                                if (w.ErrorType == "None" || string.IsNullOrEmpty(w.ErrorType))
                                {
                                    w.ErrorType = "Insertion";
                                }
                                finalWords.Add(w);
                                currentIdx += 1;
                            }
                        }
                    }
                    else
                    {
                        finalWords = pronWords;
                    }

                    // Expected punctuation positions based on reference text (break expected AFTER word index i)
                    var expectedBreakPositions = GetExpectedBreakPositions(referenceText, finalWords);

                    // Enhanced heuristic detection when SDK doesn't provide reliable break feedback
                    //if (!HasMeaningfulBreakData(finalWords)) //This if doesn't work weel with breaks
                    {
                        ApplyHeuristicBreakDetection(finalWords, expectedBreakPositions, gapAfterMs, indexOfPronWord, breakPauseThresholdMs);
                    }
                    // Refine SDK MissingBreak using actual gaps and expected punctuation
                    for (int i = 0; i < finalWords.Count; i++)
                    {
                        var w = finalWords[i];
                        if (string.Equals(w.ErrorType, "MissingBreak", StringComparison.OrdinalIgnoreCase))
                        {
                            bool expectedHere = expectedBreakPositions.Contains(i);
                            double gap = double.NaN;
                            if (indexOfPronWord.TryGetValue(w, out int origIdx) && origIdx >= 0 && origIdx < gapAfterMs.Count)
                                gap = gapAfterMs[origIdx];

                            bool hasRealPause = (!double.IsNaN(gap)) && gap >= breakPauseThresholdMs;

                            // Be more aggressive about removing false positives for MissingBreak
                            // Remove if: 1) No punctuation expected, OR 2) Any meaningful pause detected
                            if (!expectedHere || hasRealPause)
                            {
                                w.ErrorType = "None"; // false positive
                            }
                        }
                    }

                    // Refine SDK UnexpectedBreak similarly  
                    for (int i = 0; i < finalWords.Count; i++)
                    {
                        var w = finalWords[i];
                        if (string.Equals(w.ErrorType, "UnexpectedBreak", StringComparison.OrdinalIgnoreCase))
                        {
                            bool expectedHere = expectedBreakPositions.Contains(i);
                            double gap = double.NaN;
                            if (indexOfPronWord.TryGetValue(w, out int origIdx) && origIdx >= 0 && origIdx < gapAfterMs.Count)
                                gap = gapAfterMs[origIdx];

                            bool hasRealPause = (!double.IsNaN(gap)) && gap >= breakPauseThresholdMs;

                            // Only remove UnexpectedBreak if there's clear evidence it's wrong:
                            // 1. Punctuation expected AND reasonable pause (actually correct behavior)
                            // 2. No pause detected at all (no actual break occurred)
                            if ((expectedHere && hasRealPause) || (!hasRealPause && double.IsNaN(gap)))
                            {
                                w.ErrorType = "None"; // clear false positive
                            }
                        }
                    }

                    //We can calculate whole accuracy by averaging
                    var filteredWords = finalWords.Where(item => item.ErrorType != "Insertion");
                    var accuracyScore = filteredWords.Any() ? filteredWords.Sum(item => item.AccuracyScore) / filteredWords.Count() : 0;
                    var prosodyScore = prosody_scores.Any() ? Sum(prosody_scores) / prosody_scores.Count() : 0;

                    //Re-calculate fluency score
                    var fluencyScore = durations.Any() ? fluency_scores.Zip(durations, (x, y) => x * y).Sum() / durations.Sum() : 0;

                    //Calculate whole completeness score
                    var completenessScore = (double)pronWords.Count(item => item.ErrorType == "None") / referenceWords.Length * 100;
                    completenessScore = completenessScore <= 100 ? completenessScore : 100;

                    var pronScore = accuracyScore * 0.4 + prosodyScore * 0.2 + fluencyScore * 0.2 + completenessScore * 0.2;

                    Console.WriteLine("Paragraph pronunciation score: {0}, accuracy score: {1}, completeness score: {2}, fluency score: {3}, prosody score: {4}", pronScore, accuracyScore, completenessScore, fluencyScore, prosodyScore);

                    // for (int idx = 0; idx < finalWords.Count(); idx++)
                    // {
                    //     PronunciationWord word = finalWords[idx];
                    //     Console.WriteLine("{0}: word: {1}\taccuracy score: {2}\terror type: {3}",
                    //         idx + 1, word.WordText, word.AccuracyScore, word.ErrorType);
                    // }

                    var result = new PronunciationResult
                    {
                        AudioFileName = Path.GetFileName(audioFilePath),
                        ReferenceText = referenceText,
                        PronunciationScore = pronScore,
                        AccuracyScore = accuracyScore,
                        FluencyScore = fluencyScore,
                        CompletenessScore = completenessScore,
                        ProsodyScore = prosodyScore,
                        Words = finalWords
                    };
                    Console.WriteLine("====================================================FINAL=============================");
                    // serialize result to json
                    //string newtonsofo = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });

                    //Console.WriteLine(newtonsofo);

                    AllJsonFromAPI = AllJsonFromAPI + "]}";
                    return result;
                }
            }
        }

        // Check if we have meaningful break detection data from SDK
        private static bool HasMeaningfulBreakData(List<PronunciationWord> words)
        {
            return words.Any(w => string.Equals(w.ErrorType, "MissingBreak", StringComparison.OrdinalIgnoreCase) ||
                                 string.Equals(w.ErrorType, "UnexpectedBreak", StringComparison.OrdinalIgnoreCase));
        }

        // Apply heuristic break detection when SDK data is insufficient
        private static void ApplyHeuristicBreakDetection(List<PronunciationWord> finalWords,
            HashSet<int> expectedBreakPositions,
            List<double> gapAfterMs,
            Dictionary<PronunciationWord, int> indexOfPronWord,
            double breakPauseThresholdMs)
        {
            // Heuristic 1: Missing breaks - expected punctuation with short or no pause
            // Be more conservative: only flag if we have clear evidence of no pause
            foreach (var expectedPos in expectedBreakPositions)
            {
                if (expectedPos < finalWords.Count)
                {
                    var word = finalWords[expectedPos];
                    if (string.IsNullOrEmpty(word.ErrorType) || word.ErrorType == "None")
                    {
                        double gap = double.NaN;
                        if (indexOfPronWord.TryGetValue(word, out int origIdx) && origIdx >= 0 && origIdx < gapAfterMs.Count)
                            gap = gapAfterMs[origIdx];

                        // More conservative: only flag if we have clear gap data AND it's very short
                        bool hasClearlyShortPause = (!double.IsNaN(gap)) && gap < (breakPauseThresholdMs * 0.5); // 50% of threshold

                        if (hasClearlyShortPause)
                        {
                            word.ErrorType = "MissingBreak";
                        }
                    }
                }
            }

            // Heuristic 2: Unexpected breaks - long pauses where no punctuation expected
            // Keep this more conservative too
            for (int i = 0; i < finalWords.Count; i++)
            {
                var word = finalWords[i];
                if (string.IsNullOrEmpty(word.ErrorType) || word.ErrorType == "None")
                {
                    bool expectedHere = expectedBreakPositions.Contains(i);
                    if (!expectedHere) // No punctuation expected here
                    {
                        double gap = double.NaN;
                        if (indexOfPronWord.TryGetValue(word, out int origIdx) && origIdx >= 0 && origIdx < gapAfterMs.Count)
                            gap = gapAfterMs[origIdx];

                        bool hasClearlyLongPause = (!double.IsNaN(gap)) && gap >= (breakPauseThresholdMs * 2.0); // 2x threshold for unexpected

                        if (hasClearlyLongPause)
                        {
                            word.ErrorType = "UnexpectedBreak";
                        }
                    }
                }
            }

            // Heuristic 3: Detect filler words as unexpected breaks (keep this as-is, it's reliable)
            var fillerWords = new HashSet<string> { "uh", "um", "er", "ah", "eh", "hmm" };
            for (int i = 0; i < finalWords.Count; i++)
            {
                var word = finalWords[i];
                if ((string.IsNullOrEmpty(word.ErrorType) || word.ErrorType == "None") &&
                    fillerWords.Contains(word.WordText?.ToLower()?.Trim()))
                {
                    word.ErrorType = "UnexpectedBreak";
                }
            }
        }

        // Compute indices in finalWords where a break is expected (i.e., the word ends with punctuation in the reference)
        private static HashSet<int> GetExpectedBreakPositions(string referenceText, List<PronunciationWord> finalWords)
        {
            var positions = new HashSet<int>();
            if (string.IsNullOrWhiteSpace(referenceText) || finalWords == null || finalWords.Count == 0)
                return positions;

            var referenceTokens = referenceText.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            var cleanedRef = new List<string>();
            var originalIndexMap = new List<int>();

            for (int i = 0; i < referenceTokens.Length; i++)
            {
                var cleaned = Regex.Replace(referenceTokens[i], @"[^\w]", "").ToLower();
                if (!string.IsNullOrEmpty(cleaned))
                {
                    cleanedRef.Add(cleaned);
                    originalIndexMap.Add(i);
                }
            }

            int refIdx = 0;
            for (int i = 0; i < finalWords.Count && refIdx < cleanedRef.Count; i++)
            {
                var our = Regex.Replace(finalWords[i].WordText ?? string.Empty, @"[^\w]", "").ToLower();
                if (our == cleanedRef[refIdx])
                {
                    int originalRefIdx = originalIndexMap[refIdx];
                    var originalToken = referenceTokens[originalRefIdx];
                    if (Regex.IsMatch(originalToken, @"[.!?]+$") || Regex.IsMatch(originalToken, @"[,;:]+$"))
                    {
                        positions.Add(i);
                    }
                    refIdx++;
                }
            }
            return positions;
        }

        // Helper to safely traverse nested JSON properties and return a double; returns double.NaN if not found/parsable
        private static double TryGetDouble(JsonElement element, params string[] path)
        {
            try
            {
                JsonElement current = element;
                foreach (var segment in path)
                {
                    if (!current.TryGetProperty(segment, out var next))
                        return double.NaN;
                    current = next;
                }

                if (current.ValueKind == JsonValueKind.Number && current.TryGetDouble(out var d))
                    return d;

                // Sometimes confidence might be a string
                if (current.ValueKind == JsonValueKind.String)
                {
                    var s = current.GetString();
                    if (double.TryParse(s, out var parsed))
                        return parsed;
                }
            }
            catch
            {
                // ignore
            }
            return double.NaN;
        }

        // Helper to safely get a nested element
        private static bool TryGetElement(JsonElement element, out JsonElement result, params string[] path)
        {
            result = element;
            foreach (var segment in path)
            {
                if (!result.TryGetProperty(segment, out result))
                {
                    result = default(JsonElement);
                    return false;
                }
            }
            return true;
        }

        public static double Sum(List<double> values)
        {
            if (values == null || values.Count == 0)
                return 0.0;

            return values.Sum();
        }

        public static PronunciationResult CreateTestData()
        {
            var result = new PronunciationResult
            {
                AudioFileName = "pronunciation-assessment.wav",
                ReferenceText = "today was a beautiful day. we had a great time taking a long walk outside in the morning. the countryside was in full bloom, yet the air was crisp and cold. towards the end of the day, clouds came in, forecasting the much needed rain.",
                PronunciationScore = 85.0,
                AccuracyScore = 84.0,
                FluencyScore = 88.0,
                CompletenessScore = 91.0,
                ProsodyScore = 80.0,
                Words = new List<PronunciationWord>
                {
                    new PronunciationWord("today", "Monotone", 85.0),
                    new PronunciationWord("was", "Mispronunciation", 45.0),
                    new PronunciationWord("a", "None", 90.0),
                    new PronunciationWord("beautiful", "None", 95.0),
                    new PronunciationWord("day", "MissingBreak", 88.0), // Missing break after "day."
                    new PronunciationWord("we", "None", 92.0),
                    new PronunciationWord("had", "None", 87.0),
                    new PronunciationWord("a", "None", 90.0),
                    new PronunciationWord("great", "None", 89.0),
                    new PronunciationWord("time", "None", 86.0),
                    new PronunciationWord("taking", "None", 91.0),
                    new PronunciationWord("a", "None", 90.0),
                    new PronunciationWord("long", "None", 88.0),
                    new PronunciationWord("walk", "None", 85.0),
                    new PronunciationWord("outside", "None", 87.0),
                    new PronunciationWord("in", "None", 92.0),
                    new PronunciationWord("the", "Omission", 0.0),
                    new PronunciationWord("morning", "MissingBreak", 89.0), // Missing break after "morning."
                    new PronunciationWord("the", "None", 90.0),
                    new PronunciationWord("countryside", "None", 83.0),
                    new PronunciationWord("was", "Mispronunciation", 38.0),
                    new PronunciationWord("in", "None", 91.0),
                    new PronunciationWord("full", "None", 87.0),
                    new PronunciationWord("bloom", "MissingBreak", 84.0), // Missing break after "bloom,"
                    new PronunciationWord("yet", "None", 88.0),
                    new PronunciationWord("the", "None", 90.0),
                    new PronunciationWord("air", "None", 86.0),
                    new PronunciationWord("uh", "UnexpectedBreak", 25.0), // Unexpected break - hesitation
                    new PronunciationWord("was", "None", 89.0),
                    new PronunciationWord("crisp", "None", 85.0),
                    new PronunciationWord("and", "Omission", 0.0),
                    new PronunciationWord("cold", "MissingBreak", 87.0), // Missing break after "cold."
                    new PronunciationWord("towards", "None", 82.0),
                    new PronunciationWord("the", "None", 90.0),
                    new PronunciationWord("end", "None", 88.0),
                    new PronunciationWord("of", "None", 92.0),
                    new PronunciationWord("the", "Insertion", 75.0),
                    new PronunciationWord("day", "MissingBreak", 89.0), // Missing break after "day,"
                    new PronunciationWord("clouds", "None", 86.0),
                    new PronunciationWord("came", "None", 87.0),
                    new PronunciationWord("um", "UnexpectedBreak", 20.0), // Unexpected break - filler word
                    new PronunciationWord("in", "MissingBreak", 91.0), // Missing break after "in,"
                    new PronunciationWord("forecasting", "None", 80.0),
                    new PronunciationWord("the", "None", 90.0),
                    new PronunciationWord("much", "None", 85.0),
                    new PronunciationWord("needed", "None", 88.0),
                    new PronunciationWord("rain", "Monotone", 89.0)
                }
            };
            return result;
        }

        [DebuggerDisplay("{WordText}  {ErrorType}  {AccuracyScore}")]
        public class PronunciationWord
        {
            public string WordText { get; set; }
            public string ErrorType { get; set; }
            public double AccuracyScore { get; set; }

            public PronunciationWord(string word, string errorType, double accuracyScore = 100.0)
            {
                WordText = word;
                ErrorType = errorType;
                AccuracyScore = accuracyScore;
            }

            public PronunciationWord(string word, string errorType)
            {
                WordText = word;
                ErrorType = errorType;
                AccuracyScore = 0.0; // Default to 0 for error words
            }
        }

        public class PronunciationResult
        {
            public string AudioFileName { get; set; }
            public string ReferenceText { get; set; }
            public double PronunciationScore { get; set; }
            public double AccuracyScore { get; set; }
            public double FluencyScore { get; set; }
            public double CompletenessScore { get; set; }
            public double ProsodyScore { get; set; }
            public List<PronunciationWord> Words { get; set; } = new List<PronunciationWord>();
        }
    }
}