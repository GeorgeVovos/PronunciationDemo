using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using static PronunciationDemo.AIPronunciationAssessmentService;

namespace PronunciationDemo
{
    public class AIPronunciationHtmlReportGenerator
    {
        private readonly PronunciationResult _result;

        public AIPronunciationHtmlReportGenerator(PronunciationResult result)
        {
            _result = result ?? throw new ArgumentNullException(nameof(result), "Pronunciation result cannot be null");
        }

        private string HtmlEncode(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;
                
            return text.Replace("&", "&amp;")
                      .Replace("<", "&lt;")
                      .Replace(">", "&gt;")
                      .Replace("\"", "&quot;")
                      .Replace("'", "&#39;");
        }

        public string GenerateFullReport()
        {
            var errorCounts = GetErrorCounts();
            
            StringBuilder html = new StringBuilder();
            html.AppendLine("<!DOCTYPE html>");
            html.AppendLine("<html lang=\"en\">");
            html.AppendLine("<head>");
            html.AppendLine("    <meta charset=\"UTF-8\">");
            html.AppendLine("    <meta http-equiv=\"X-UA-Compatible\" content=\"IE=edge\">");
            html.AppendLine("    <meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">");
            html.AppendLine("    <title>Pronunciation Assessment Results</title>");
            html.AppendLine("    <style>");
            html.AppendLine(GetCssStyles());
            html.AppendLine("    </style>");
            html.AppendLine("</head>");
            html.AppendLine("<body>");
            
            // Header
            html.AppendLine("    <div class=\"container\">");

            // Main content area
            html.AppendLine("        <div class=\"content-area\">");
            
            // Text display with highlighted words
            html.AppendLine("            <div class=\"text-display\">");
            html.AppendLine("                <h3>Assessed Speech:</h3>");
            html.AppendLine(GenerateHighlightedText());
            html.AppendLine("            </div>");
            
            // Errors panel
            html.AppendLine("            <div class=\"errors-panel\">");
            html.AppendLine("                <h3>Errors</h3>");
            html.AppendLine($"                <div class=\"error-item\"><span class=\"error-count mispronunciation\">{errorCounts["Mispronunciation"]}</span> Mispronunciations <label class=\"switch\"><input type=\"checkbox\" checked><span class=\"slider\"></span></label></div>");
            html.AppendLine($"                <div class=\"error-item\"><span class=\"error-count omission\">{errorCounts["Omission"]}</span> Omissions <label class=\"switch\"><input type=\"checkbox\" checked><span class=\"slider\"></span></label></div>");
            html.AppendLine($"                <div class=\"error-item\"><span class=\"error-count insertion\">{errorCounts["Insertion"]}</span> Insertions <label class=\"switch\"><input type=\"checkbox\" checked><span class=\"slider\"></span></label></div>");
            html.AppendLine($"                <div class=\"error-item\"><span class=\"error-count unexpected-break\">{errorCounts["UnexpectedBreak"]}</span> Unexpected break <label class=\"switch\"><input type=\"checkbox\" checked><span class=\"slider\"></span></label></div>");
            html.AppendLine($"                <div class=\"error-item\"><span class=\"error-count missing-break\">{errorCounts["MissingBreak"]}</span> Missing break <label class=\"switch\"><input type=\"checkbox\" checked><span class=\"slider\"></span></label></div>");
            html.AppendLine($"                <div class=\"error-item\"><span class=\"error-count monotone\">{errorCounts["Monotone"]}</span> Monotone <label class=\"switch\"><input type=\"checkbox\" checked><span class=\"slider\"></span></label></div>");
            html.AppendLine("            </div>");
            
            html.AppendLine("        </div>");
            
            // Scores section
            html.AppendLine("        <div class=\"scores-section\">");
            
            // Circular score
            html.AppendLine("            <div class=\"circular-score\">");
            html.AppendLine("                <h3>Pronunciation score</h3>");
            html.AppendLine($"                <div class=\"circle-container\">");
            html.AppendLine($"                    <svg class=\"circle-progress\" width=\"160\" height=\"160\">\n                        <circle cx=\"80\" cy=\"80\" r=\"70\" class=\"circle-bg\"></circle>\n                        <circle cx=\"80\" cy=\"80\" r=\"70\" class=\"circle-fill\" style=\"stroke-dasharray: {(_result.PronunciationScore / 100.0 * 440):F1} 440\"></circle>\n                    </svg>");
            html.AppendLine($"                    <div class=\"score-text\">{(int)_result.PronunciationScore}</div>");
            html.AppendLine("                </div>");
            html.AppendLine("            </div>");
            
            // Score breakdown
            html.AppendLine("            <div class=\"score-breakdown\">");
            html.AppendLine("                <h3>Score breakdown</h3>");
            html.AppendLine("                <div class=\"score-bars\">");
            html.AppendLine($"                    <div class=\"score-bar\"><label>Accuracy score</label><div class=\"bar\"><div class=\"fill\" style=\"width: {_result.AccuracyScore}%\"></div></div><span>{_result.AccuracyScore:F0} / 100</span></div>");
            html.AppendLine($"                    <div class=\"score-bar\"><label>Fluency score</label><div class=\"bar\"><div class=\"fill\" style=\"width: {_result.FluencyScore}%\"></div></div><span>{_result.FluencyScore:F0} / 100</span></div>");
            html.AppendLine($"                    <div class=\"score-bar\"><label>Completeness score</label><div class=\"bar\"><div class=\"fill\" style=\"width: {_result.CompletenessScore}%\"></div></div><span>{_result.CompletenessScore:F0} / 100</span></div>");
            html.AppendLine($"                    <div class=\"score-bar\"><label>Prosody score</label><div class=\"bar\"><div class=\"fill\" style=\"width: {_result.ProsodyScore}%\"></div></div><span>{_result.ProsodyScore:F0} / 100</span></div>");
            html.AppendLine("                </div>");
            html.AppendLine("            </div>");
            
            html.AppendLine("        </div>");
            
            // Score legend
            html.AppendLine("        <div class=\"score-legend\">");
            html.AppendLine("            <span class=\"legend-item\"><span class=\"color-box red\"></span> 0 - 59</span>");
            html.AppendLine("            <span class=\"legend-item\"><span class=\"color-box yellow\"></span> 60 - 79</span>");
            html.AppendLine("            <span class=\"legend-item\"><span class=\"color-box green\"></span> 80 - 100</span>");
            html.AppendLine("        </div>");
            
            html.AppendLine("    </div>");
            html.AppendLine("    <script>");
            html.AppendLine(GetJavaScript());
            html.AppendLine("    </script>");
            html.AppendLine("</body>");
            html.AppendLine("</html>");
            
            return html.ToString();
        }
        
        private string GetCssStyles()
        {
            return @"
        body {
            font-family: 'Segoe UI', Arial, sans-serif;
            margin: 0;
            padding: 20px;
            background-color: #f8f9fa;
            color: #333;
        }
        
        .container {
            max-width: 1200px;
            margin: 0 auto;
            background: white;
            padding: 30px;
            border-radius: 8px;
            box-shadow: 0 2px 10px rgba(0,0,0,0.1);
        }
        
        h1 {
            margin: 0 0 20px 0;
            font-size: 24px;
            font-weight: 600;
        }
        
        h3 {
            margin: 0 0 15px 0;
            font-size: 16px;
            font-weight: 600;
            color: #495057;
        }
        
        .content-area {
            display: flex;
            gap: 30px;
            margin-bottom: 30px;
        }
        
        .text-display {
            flex: 2;
            line-height: 1.8;
            font-size: 16px;
        }
        
        .errors-panel {
            flex: 1;
            background: #f8f9fa;
            padding: 20px;
            border-radius: 6px;
        }
        
        .error-item {
            display: flex;
            align-items: center;
            justify-content: space-between;
            margin-bottom: 12px;
            font-size: 14px;
        }
        
        .error-count {
            display: inline-block;
            width: 20px;
            height: 20px;
            border-radius: 3px;
            text-align: center;
            color: white;
            font-weight: bold;
            font-size: 12px;
            line-height: 20px;
            margin-right: 8px;
        }
        
        .error-count.mispronunciation { background: #FFC107; color: #333; }
        .error-count.omission { background: #6C757D; }
        .error-count.insertion { background: #DC3545; }
        .error-count.unexpected-break { background: #FFB6C1; color: #333; }
        .error-count.missing-break { background: #A9A9A9; }
        .error-count.monotone { background: #800080; }
        
        .switch {
            position: relative;
            display: inline-block;
            width: 40px;
            height: 20px;
        }
        
        .switch input {
            opacity: 0;
            width: 0;
            height: 0;
        }
        
        .slider {
            position: absolute;
            cursor: pointer;
            top: 0;
            left: 0;
            right: 0;
            bottom: 0;
            background-color: #ccc;
            transition: .4s;
            border-radius: 20px;
        }
        
        .slider:before {
            position: absolute;
            content: '';
            height: 16px;
            width: 16px;
            left: 2px;
            bottom: 2px;
            background-color: white;
            transition: .4s;
            border-radius: 50%;
        }
        
        input:checked + .slider {
            background-color: #007bff;
        }
        
        input:checked + .slider:before {
            transform: translateX(20px);
        }
        
        .word {
            padding: 2px 4px;
            margin: 0 1px;
            border-radius: 3px;
            display: inline;
            color: #333;
        }
        
        .word.mispronunciation { background: #FF8C00; text-decoration: underline; }
        .word.omission { background: #C0C0C0; }
        .word.insertion { background: #DC3545; color: white; text-decoration: line-through; }
        .word.unexpected-break { 
            background: #FFE6F0;
            border: 1px solid #FF69B4;
            padding: 2px 6px;
            margin: 0 2px;
            font-family: monospace;
        }
        .word.missing-break { 
            background: transparent; 
        }
        .word.missing-break-indicator {
            background: #F5F5F5;
            color: #333;
            border: 1px solid #999;
            padding: 2px 6px;
            margin: 0 2px;
            font-family: monospace;
        }
        .word.punct { margin-left: 0; margin-right: 6px; }
        .word.monotone { background: #F0E6FF; }
        .word.good { }
        .word.fair { }
        .word.poor { }
        
        .scores-section {
            display: flex;
            gap: 40px;
            margin-bottom: 20px;
        }
        
        .circular-score {
            flex: 0 0 auto;
        }
        
        .circle-container {
            position: relative;
            width: 160px;
            height: 160px;
        }
        
        .circle-progress {
            transform: rotate(-90deg);
        }
        
        .circle-bg {
            fill: none;
            stroke: #e9ecef;
            stroke-width: 8;
        }
        
        .circle-fill {
            fill: none;
            stroke: #28a745;
            stroke-width: 8;
            stroke-linecap: round;
            transition: stroke-dasharray 0.5s ease;
        }
        
        .score-text {
            position: absolute;
            top: 50%;
            left: 50%;
            transform: translate(-50%, -50%);
            font-size: 32px;
            font-weight: bold;
            color: #333;
        }
        
        .score-breakdown {
            flex: 1;
        }
        
        .score-bars {
            display: flex;
            flex-direction: column;
            gap: 15px;
        }
        
        .score-bar {
            display: flex;
            align-items: center;
            gap: 15px;
        }
        
        .score-bar label {
            min-width: 140px;
            font-size: 14px;
        }
        
        .bar {
            flex: 1;
            height: 20px;
            background: #e9ecef;
            border-radius: 10px;
            overflow: hidden;
        }
        
        .bar .fill {
            height: 100%;
            background: #28a745;
            transition: width 0.5s ease;
        }
        
        .score-bar span {
            min-width: 50px;
            font-size: 14px;
            font-weight: 500;
        }
        
        .score-legend {
            display: flex;
            gap: 20px;
            font-size: 14px;
            margin-bottom: 20px;
        }
        
        .legend-item {
            display: flex;
            align-items: center;
            gap: 5px;
        }
        
        .color-box {
            width: 16px;
            height: 16px;
            border-radius: 2px;
        }
        
        .color-box.red { background: #dc3545; }
        .color-box.yellow { background: #ffc107; }
        .color-box.green { background: #28a745; }";
        }

        private string GenerateHighlightedText()
        {
            StringBuilder textHtml = new StringBuilder();

            // Build a punctuation map from reference text aligned to our word list
            var punctuationMap = BuildPunctuationMap(_result.ReferenceText, _result.Words);
            
            if (_result.Words != null && _result.Words.Count > 0)
            {
                for (int i = 0; i < _result.Words.Count; i++)
                {
                    var word = _result.Words[i];
                    string displayText = HtmlEncode(word.WordText ?? "Unknown");

                    // For rendering class on the word itself, ignore break error types so the word remains visible
                    string visualClassForWord = GetWordCssClass("None", word.AccuracyScore);
                    string wordClass = GetWordCssClass(word.ErrorType, word.AccuracyScore);
                    
                    if (word.ErrorType == "Omission")
                    {
                        // Omitted reference word placeholder
                        textHtml.Append($"<span class=\"word {wordClass}\">[{displayText}]</span> ");
                        continue;
                    }
                    
                    if (word.ErrorType == "UnexpectedBreak")
                    {
                        // Show an inserted break BEFORE the current spoken word, then the word itself
                        textHtml.Append($"<span class=\"word unexpected-break\" title=\"Unexpected break before: {displayText}\">[ ]</span> ");
                        textHtml.Append($"<span class=\"word {visualClassForWord}\">{displayText}</span>");
                    }
                    else
                    {
                        // Normal word display
                        textHtml.Append($"<span class=\"word {wordClass}\">{displayText}</span>");
                    }

                    // Append actual punctuation from reference, if any
                    if (punctuationMap.TryGetValue(i, out var punct) && !string.IsNullOrEmpty(punct))
                    {
                        textHtml.Append($"<span class=\"word punct\">{HtmlEncode(punct)}</span>");
                    }

                    // Add missing break indicator after the word if applicable (keep punctuation visible)
                    if (word.ErrorType == "MissingBreak")
                    {
                        textHtml.Append($"<span class=\"word missing-break-indicator\" title=\"Missing break after: {displayText}\">[ ]</span>");
                    }

                    textHtml.Append(" ");
                }
            }
            else
            {
                textHtml.Append(HtmlEncode(_result.ReferenceText ?? "No text available"));
            }
            
            return textHtml.ToString();
        }

        private string GetWordCssClass(string errorType, double accuracyScore)
        {
            // First check for specific error types
            switch (errorType?.ToLower())
            {
                case "mispronunciation":
                    return "mispronunciation";
                case "omission":
                    return "omission";
                case "insertion":
                    return "insertion";
                case "unexpectedbreak":
                    return "unexpected-break";
                case "missingbreak":
                    return "missing-break";
                case "monotone":
                    return "monotone";
                case "none":
                case "":
                case null:
                    // For words with no errors, classify based on accuracy score
                    if (accuracyScore >= 80) 
                        return "good";
                    else if (accuracyScore >= 60) 
                        return "fair";
                    else if (accuracyScore > 0) 
                        return "poor";
                    else 
                        return "";
                default:
                    // Fallback: classify based on accuracy score for unknown error types
                    if (accuracyScore >= 80) 
                        return "good";
                    else if (accuracyScore >= 60) 
                        return "fair";
                    else if (accuracyScore > 0) 
                        return "poor";
                    else 
                        return "";
            }
        }

        private Dictionary<int, string> BuildPunctuationMap(string referenceText, List<PronunciationWord> words)
        {
            var map = new Dictionary<int, string>();
            if (string.IsNullOrWhiteSpace(referenceText) || words == null || words.Count == 0)
                return map;

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
            for (int i = 0; i < words.Count && refIdx < cleanedRef.Count; i++)
            {
                var our = Regex.Replace(words[i].WordText ?? string.Empty, @"[^\w]", "").ToLower();
                if (our == cleanedRef[refIdx])
                {
                    string originalToken = referenceTokens[originalIndexMap[refIdx]];
                    var m = Regex.Match(originalToken, @"[\.!\?,;:]+$");
                    if (m.Success)
                    {
                        map[i] = m.Value;
                    }
                    refIdx++;
                }
            }

            return map;
        }

        private Dictionary<string, int> GetErrorCounts()
        {
            var counts = new Dictionary<string, int>
            {
                ["Mispronunciation"] = 0,
                ["Omission"] = 0,
                ["Insertion"] = 0,
                ["UnexpectedBreak"] = 0,
                ["MissingBreak"] = 0,
                ["Monotone"] = 0
            };

            if (_result.Words != null)
            {
                foreach (var word in _result.Words)
                {
                    switch (word.ErrorType?.ToLower())
                    {
                        case "mispronunciation":
                            counts["Mispronunciation"]++;
                            break;
                        case "omission":
                            counts["Omission"]++;
                            break;
                        case "insertion":
                            counts["Insertion"]++;
                            break;
                        case "unexpectedbreak":
                            counts["UnexpectedBreak"]++;
                            break;
                        case "missingbreak":
                            counts["MissingBreak"]++;
                            break;
                        case "monotone":
                            counts["Monotone"]++;
                            break;
                    }
                }
            }

            return counts;
        }

        private string GetJavaScript()
        {
            return @"
        // Toggle error highlighting - IE compatible version
        function initializeErrorToggles() {
            try {
                var switches = document.getElementsByTagName('input');
                var errorTypes = ['mispronunciation', 'omission', 'insertion', 'unexpected-break', 'missing-break', 'monotone'];
                var switchIndex = 0;
                
                for (var i = 0; i < switches.length; i++) {
                    if (switches[i].type === 'checkbox' && switchIndex < errorTypes.length) {
                        attachToggleHandler(switches[i], errorTypes[switchIndex]);
                        switchIndex++;
                    }
                }
            } catch (e) {
                // Silently handle any errors
            }
        }
        
        function attachToggleHandler(toggle, errorType) {
            try {
                toggle.onchange = function() {
                    toggleErrorVisibility(errorType, this.checked);
                };
            } catch (e) {
                // Silently handle any errors
            }
        }
        
        function toggleErrorVisibility(errorType, isVisible) {
            try {
                var elements = document.getElementsByTagName('span');
                
                for (var i = 0; i < elements.length; i++) {
                    var element = elements[i];
                    var className = element.className || '';
                    
                    // Handle unexpected breaks
                    if (errorType === 'unexpected-break' && className.indexOf('unexpected-break') !== -1) {
                        element.style.display = isVisible ? 'inline' : 'none';
                    }
                    // Handle missing breaks (both the word and the indicator)
                    else if (errorType === 'missing-break' && 
                            (className.indexOf('missing-break') !== -1 || className.indexOf('missing-break-indicator') !== -1)) {
                        element.style.display = isVisible ? 'inline' : 'none';
                    }
                    // Handle other error types
                    else if (errorType !== 'unexpected-break' && errorType !== 'missing-break' && 
                            className.indexOf(errorType) !== -1) {
                        element.style.display = isVisible ? 'inline' : 'none';
                    }
                }
            } catch (e) {
                // Silently handle any errors
            }
        }
        
        // Initialize when page loads
        if (document.readyState === 'complete') {
            initializeErrorToggles();
        } else {
            if (document.addEventListener) {
                document.addEventListener('DOMContentLoaded', initializeErrorToggles);
            } else if (document.attachEvent) {
                document.attachEvent('onreadystatechange', function() {
                    if (document.readyState === 'complete') {
                        initializeErrorToggles();
                    }
                });
            }
        }
        ";
        }

        public string GenerateTextReport()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("PRONUNCIATION ASSESSMENT RESULTS");
            sb.AppendLine("================================");
            sb.AppendLine($"Audio File: {_result.AudioFileName}");
            sb.AppendLine($"Assessment Date: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine();
            
            sb.AppendLine("REFERENCE TEXT:");
            sb.AppendLine(_result.ReferenceText);
            sb.AppendLine();
            
            sb.AppendLine("OVERALL SCORES:");
            sb.AppendLine($"Pronunciation Score: {_result.PronunciationScore:F1}/100");
            sb.AppendLine($"Accuracy Score: {_result.AccuracyScore:F1}/100");
            sb.AppendLine($"Fluency Score: {_result.FluencyScore:F1}/100");
            sb.AppendLine($"Completeness Score: {_result.CompletenessScore:F1}/100");
            sb.AppendLine($"Prosody Score: {_result.ProsodyScore:F1}/100");
            sb.AppendLine();
            
            sb.AppendLine("WORD-LEVEL RESULTS:");
            sb.AppendLine("-------------------");
            for (int i = 0; i < _result.Words.Count; i++)
            {
                var word = _result.Words[i];
                sb.AppendLine($"{i + 1:D3}: {word.WordText,-20} | Accuracy: {word.AccuracyScore:F1} | Error: {word.ErrorType}");
            }
            
            return sb.ToString();
        }
    }
}