using System.Text.RegularExpressions;

namespace ManufacturingTimeTracking.Helpers;

public static class InstructionFormatter
{
    /// <summary>
    /// Formats instructions text to HTML, converting markdown-style links and preserving line breaks
    /// </summary>
    public static string FormatInstructions(string instructions)
    {
        if (string.IsNullOrEmpty(instructions))
        {
            return string.Empty;
        }

        var html = instructions;

        // Convert markdown-style links [text](url) to HTML links
        html = Regex.Replace(html, @"\[([^\]]+)\]\(([^\)]+)\)", 
            match => $"<a href=\"{match.Groups[2].Value}\" target=\"_blank\" rel=\"noopener noreferrer\">{match.Groups[1].Value}</a>");

        // Convert plain URLs to links (if not already formatted)
        html = Regex.Replace(html, @"(?<!href=[""'])(https?://[^\s]+)", 
            match => $"<a href=\"{match.Value}\" target=\"_blank\" rel=\"noopener noreferrer\">{match.Value}</a>");

        // Convert list items (lines starting with - or *)
        html = Regex.Replace(html, @"^[\-\*]\s+(.+)$", "<li>$1</li>", RegexOptions.Multiline);
        
        // Wrap consecutive list items in <ul> tags
        html = Regex.Replace(html, @"(<li>.*?</li>(?:\s*<li>.*?</li>)*)", "<ul>$1</ul>", RegexOptions.Singleline);

        // Convert double line breaks to paragraphs
        html = Regex.Replace(html, @"\n\s*\n", "</p><p>");
        
        // Convert single line breaks to <br>
        html = html.Replace("\n", "<br/>");

        // Wrap in paragraph tags if not already wrapped
        if (!html.StartsWith("<p>") && !html.StartsWith("<ul>"))
        {
            html = "<p>" + html + "</p>";
        }

        return html;
    }

    /// <summary>
    /// Formats instructions as plain text with line breaks preserved
    /// </summary>
    public static string FormatInstructionsPlain(string instructions)
    {
        if (string.IsNullOrEmpty(instructions))
        {
            return string.Empty;
        }

        // Just preserve line breaks
        return instructions.Replace("\n", "<br/>");
    }
}
