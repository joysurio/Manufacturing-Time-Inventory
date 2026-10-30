using System.Net;

namespace ManufacturingTimeTracking.Services;

/// <summary>Builds the "Resumen del día" HTML (face icons) shown at clock-out and stored for reports.</summary>
public static class DailyEvaluationHtml
{
    public static string BuildEndOfDaySummaryHtml(
        double scoreEntry,
        double scoreExit,
        double scoreTasks,
        double scoreWorked,
        double scoreBreak,
        double averageScore,
        string entryLabel,
        string exitLabel,
        int taskSeconds,
        int workedSeconds,
        int breakSeconds)
    {
        const int FullDaySeconds = 7 * 3600 + 40 * 60; // 7:40
        double taskPercent = Math.Min(100.0, 100.0 * taskSeconds / (double)FullDaySeconds);
        var taskMinutes = (int)Math.Round(taskSeconds / 60.0);
        string SvgFace(string mood, int size)
        {
            var viewBox = "0 0 64 64";
            if (mood == "happy")
            {
                return $"<svg xmlns='http://www.w3.org/2000/svg' width='{size}' height='{size}' viewBox='{viewBox}' aria-hidden='true'>" +
                       "<circle cx='32' cy='32' r='30' fill='#10b981'/>" +
                       "<circle cx='22' cy='24' r='4' fill='#ffffff'/>" +
                       "<circle cx='42' cy='24' r='4' fill='#ffffff'/>" +
                       "<path d='M20 38 Q32 50 44 38' stroke='#053020' stroke-width='3' fill='none' stroke-linecap='round'/>" +
                       "</svg>";
            }
            if (mood == "neutral")
            {
                return $"<svg xmlns='http://www.w3.org/2000/svg' width='{size}' height='{size}' viewBox='{viewBox}' aria-hidden='true'>" +
                       "<circle cx='32' cy='32' r='30' fill='#f59e0b'/>" +
                       "<circle cx='22' cy='24' r='4' fill='#2a1200'/>" +
                       "<circle cx='42' cy='24' r='4' fill='#2a1200'/>" +
                       "<line x1='22' y1='40' x2='42' y2='40' stroke='#2a1200' stroke-width='3' stroke-linecap='round'/>" +
                       "</svg>";
            }
            return $"<svg xmlns='http://www.w3.org/2000/svg' width='{size}' height='{size}' viewBox='{viewBox}' aria-hidden='true'>" +
                   "<circle cx='32' cy='32' r='30' fill='#ef4444'/>" +
                   "<circle cx='22' cy='24' r='4' fill='#ffffff'/>" +
                   "<circle cx='42' cy='24' r='4' fill='#ffffff'/>" +
                   "<path d='M20 44 Q32 34 44 44' stroke='#6b021f' stroke-width='3' fill='none' stroke-linecap='round'/>" +
                   "</svg>";
        }

        string FaceHtmlForScore(double s, string smallLabel)
        {
            string mood = s >= 0.99 ? "happy" : (s >= 0.49 ? "neutral" : "angry");
            var svg = SvgFace(mood, 56);
            return $"<div style='display:flex;flex-direction:column;align-items:center;'><div style='width:56px;height:56px'>{svg}</div><div style='margin-top:6px;font-size:13px'>{WebUtility.HtmlEncode(smallLabel)}</div></div>";
        }

        string overallMood = averageScore >= 0.75 ? "happy" : (averageScore >= 0.4 ? "neutral" : "angry");
        var overallSvg = SvgFace(overallMood, 140);

        var html = new System.Text.StringBuilder();
        html.AppendLine("<div style='padding:18px;border-radius:14px;background:linear-gradient(135deg,#0f172a,#071029);color:#fff;border:2px solid rgba(255,255,255,0.04)'>");
        html.AppendLine("<h2 style='margin:0 0 10px 0;font-size:18px'>Resumen del día</h2>");
        html.AppendLine("<div style='display:flex;gap:18px;align-items:center'>");
        html.AppendLine("<div style='display:flex;gap:14px;align-items:center;flex-wrap:wrap'>");
        html.AppendLine(FaceHtmlForScore(scoreEntry, "Entrada: " + entryLabel));
        html.AppendLine(FaceHtmlForScore(scoreExit, "Salida: " + exitLabel));
        html.AppendLine(FaceHtmlForScore(scoreTasks, $"Tareas: {taskPercent:0.0}%"));
        html.AppendLine(FaceHtmlForScore(scoreWorked, $"Trabajo: {Math.Floor(workedSeconds / 3600.0)}h {Math.Floor((workedSeconds % 3600) / 60.0)}m"));
        html.AppendLine(FaceHtmlForScore(scoreBreak, $"Descanso: {Math.Floor(breakSeconds / 60.0)}m"));
        html.AppendLine("</div>");
        html.AppendLine("<div style='margin-left:18px;flex:1;display:flex;justify-content:center;align-items:center;min-width:140px'>");
        html.AppendLine($"<div style='width:140px;height:140px;border-radius:50%;display:flex;align-items:center;justify-content:center;background:linear-gradient(180deg,#111827,#0b1220);box-shadow:0 8px 30px rgba(0,0,0,0.6);'>{overallSvg}</div>");
        html.AppendLine("</div>");
        html.AppendLine("</div>");
        html.AppendLine("<p style='margin-top:12px;color:#cbd5e1;font-size:13px'>Media de puntuaciones: " + Math.Round(averageScore * 100) + "%</p>");
        html.AppendLine("<p style='margin-top:6px;color:#9ca3af;font-size:13px'>Objetivos: Entrada antes de 07:00, Salida después de 15:00, Tareas ≥90% tiempo en fábrica, Trabajo ≥7:40, Descanso ≤20m</p>");
        html.AppendLine("<p style='margin-top:2px;color:#64748b;font-size:11px'>Tareas % = max(MTI, API) / 7:40 · Trabajo = tiempo fichado − descansos</p>");
        // Add actual measured values
        var workedH = (int)(workedSeconds / 3600);
        var workedM = (int)((workedSeconds % 3600) / 60);
        var breakM = (int)Math.Round(breakSeconds / 60.0);
        html.AppendLine($"<p style='margin-top:6px;color:#cbd5e1;font-size:13px'><strong>Actual:</strong> Entrada: {WebUtility.HtmlEncode(entryLabel)}, Salida: {WebUtility.HtmlEncode(exitLabel)}, Trabajo: {workedH}h {workedM}m, Tareas: {taskMinutes}m ({taskPercent:0.0}%), Descanso: {breakM}m</p>");
        html.AppendLine("</div>");
        return html.ToString();
    }
}
