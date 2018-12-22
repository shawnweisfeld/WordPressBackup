using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace WordPressBackup
{
    public class Logger
    {
        public string Run { get; set; }
        public string BackupFile { get; set; }
        public TelemetryClient TelemetryClient { get; set; }

        public Logger(string run, string backup, string instrumentationKey)
        {
            Run = run;
            BackupFile = backup;

            if (!string.IsNullOrEmpty(instrumentationKey))
            {
                TelemetryConfiguration configuration = TelemetryConfiguration.Active;
                configuration.InstrumentationKey = instrumentationKey;
                TelemetryClient = new TelemetryClient();
            }
        }

        public void Log(string message, SeverityLevel sev = SeverityLevel.Information)
        {
            Console.Write(message);

            if (TelemetryClient != null)
            {
                TelemetryClient.TrackTrace(message, sev, new Dictionary<string, string>()
                {
                    {"Run", Run },
                    {"Backup", BackupFile}
                });
            }
        }

        public void Log(Exception ex)
        {
            Console.WriteLine($"ERROR: {ex.Message}");

            if (TelemetryClient != null)
            {
                TelemetryClient.TrackException(ex, new Dictionary<string, string>()
                {
                    {"Run", Run },
                    {"Backup", BackupFile}
                });
            }
        }

        public async Task Flush()
        {
            if (TelemetryClient != null)
            {
                // before exit, flush the remaining data
                TelemetryClient.Flush();

                // flush is not blocking so wait a bit
                await Task.Delay(5000);
            }
        }
    }
}
