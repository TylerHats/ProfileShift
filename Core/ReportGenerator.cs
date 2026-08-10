using System;
using System.IO;
using System.Text;
using ProfileShift.Models;

namespace ProfileShift.Core
{
    public static class ReportGenerator
    {
        public static string GenerateHtmlReport(MigrationConfig config, string matchedUsername)
        {
            var sb = new StringBuilder();
            sb.AppendLine("<!DOCTYPE html>");
            sb.AppendLine("<html lang=\"en\">");
            sb.AppendLine("<head>");
            sb.AppendLine("<meta charset=\"UTF-8\">");
            sb.AppendLine("<title>ProfileShift Migration Summary</title>");
            sb.AppendLine("<style>");
            sb.AppendLine("  body { font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif; background-color: #2f3136; color: #d9d9d9; margin: 0; padding: 20px; }");
            sb.AppendLine("  .container { max-width: 800px; margin: 0 auto; background-color: #36393f; border-radius: 8px; padding: 24px; box-shadow: 0 4px 12px rgba(0,0,0,0.3); }");
            sb.AppendLine("  h1 { color: #43b581; font-size: 24px; border-bottom: 2px solid #40444b; padding-bottom: 10px; }");
            sb.AppendLine("  h2 { color: #7289da; font-size: 18px; margin-top: 20px; }");
            sb.AppendLine("  table { width: 100%; border-collapse: collapse; margin-top: 10px; }");
            sb.AppendLine("  th, td { text-align: left; padding: 10px; border-bottom: 1px solid #40444b; }");
            sb.AppendLine("  th { background-color: #202225; color: #ffffff; }");
            sb.AppendLine("  .badge { background-color: #43b581; color: #ffffff; padding: 4px 8px; border-radius: 4px; font-size: 12px; }");
            sb.AppendLine("</style>");
            sb.AppendLine("</head>");
            sb.AppendLine("<body>");
            sb.AppendLine("<div class=\"container\">");
            sb.AppendLine("<h1>ProfileShift Migration Summary Report</h1>");
            sb.AppendLine($"<p><strong>Migrated Profile:</strong> {matchedUsername}</p>");
            sb.AppendLine($"<p><strong>Source Computer:</strong> {config.SourceMachineName} ({config.SourceDomain})</p>");
            sb.AppendLine($"<p><strong>Migration Date:</strong> {config.MigrationTime:yyyy-MM-dd HH:mm:ss}</p>");

            if (config.UserSelections.TryGetValue(matchedUsername, out var selection))
            {
                sb.AppendLine("<h2>Migrated Folders</h2>");
                sb.AppendLine("<ul>");
                foreach (var folder in selection.Folders)
                {
                    sb.AppendLine($"<li>{folder}</li>");
                }
                sb.AppendLine("</ul>");

                sb.AppendLine("<h2>Migrated Browser Profiles</h2>");
                sb.AppendLine("<ul>");
                foreach (var browser in selection.Browsers)
                {
                    sb.AppendLine($"<li>{browser}</li>");
                }
                sb.AppendLine("</ul>");
            }

            if (config.Printers != null && config.Printers.Count > 0)
            {
                sb.AppendLine("<h2>Migrated Printers</h2>");
                sb.AppendLine("<table><tr><th>Printer Name</th><th>Driver</th><th>Port</th></tr>");
                foreach (var printer in config.Printers)
                {
                    sb.AppendLine($"<tr><td>{printer.Name}</td><td>{printer.DriverName}</td><td>{printer.PortName}</td></tr>");
                }
                sb.AppendLine("</table>");
            }

            sb.AppendLine("</div>");
            sb.AppendLine("</body>");
            sb.AppendLine("</html>");

            return sb.ToString();
        }

        public static void SaveReportToDesktop(MigrationConfig config, string matchedUsername)
        {
            try
            {
                string desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                string htmlPath = Path.Combine(desktop, "ProfileShift_Summary.html");
                string htmlContent = GenerateHtmlReport(config, matchedUsername);
                File.WriteAllText(htmlPath, htmlContent, Encoding.UTF8);
            }
            catch { }
        }
    }
}
