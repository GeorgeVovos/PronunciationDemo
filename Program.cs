using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace PronunciationDemo
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            await MarkAudioPronunciation();

            Console.WriteLine("Finished");
            Console.ReadLine();
        }

        private static async Task MarkAudioPronunciation()
        {
            AIPronunciationAssessmentService.PronunciationResult result = null;

            string subscriptionKey = Environment.GetEnvironmentVariable("HackathonAzureSpeechApiKey", EnvironmentVariableTarget.Machine);
            string serviceRegion = "centralus";
            string audioFileName = @"AudioSamples\RecordingTodayWasABeautifulDay4ManyPauses.wav";
            //string audioFileName = @"RecordingTodayWasABeautifulDay2.wav";
            // string audioFileName = @"RecordingTodayWasABeautifulDay.wav";
            var referenceText = "Today was a beautiful day. We had a great time taking a long walk outside in the morning. The countryside was in full bloom, yet the air was crisp and cold. Towards the end of the day, clouds came in, forecasting much needed rain.";

            AIPronunciationAssessmentService service = new AIPronunciationAssessmentService(subscriptionKey, serviceRegion, audioFileName, referenceText);

            result = await service.PronunciationAssessmentContinuousWithFile();

            ShowAudioAssessmentResults(result);
        }

        private static void ShowAudioAssessmentResults(AIPronunciationAssessmentService.PronunciationResult result)
        {
            string htmlContent = new AIPronunciationHtmlReportGenerator(result).GenerateFullReport();
            string tempHtmlFile = $"pronunciation_results_{DateTime.Now:yyyyMMdd_HHmmss}.html";
            File.WriteAllText(tempHtmlFile, htmlContent);
            Process.Start(tempHtmlFile);
        }
    }
}
