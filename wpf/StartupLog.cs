using System;
using System.IO;

namespace ShadePilotWpf
{
    internal static class StartupLog
    {
        static readonly string PathValue = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ShadePilot", "logs", "startup.log");

        internal static void Write(string stage, Exception exception = null)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(PathValue)!);
                File.AppendAllText(PathValue,
                    $"{DateTimeOffset.Now:O} {stage}{(exception == null ? string.Empty : Environment.NewLine + exception)}{Environment.NewLine}");
            }
            catch
            {
                // Logging must never change feature or application availability.
            }
        }
    }
}
