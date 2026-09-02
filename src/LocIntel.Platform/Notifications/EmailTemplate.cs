using System.Net;
using System.Text;

namespace LocIntel.Platform.Notifications;

/// <summary>
/// The template's one email template, versioned in-repo as code (ADR 32):
/// a branded heading (the ORG is the sender the recipient recognizes, not
/// the platform), body lines, and one action link - rendered as both plain
/// text (the source of truth; /dev/mail and text-only clients read it) and
/// a minimal inline-styled HTML alternative. Forks that need richer mail
/// replace this class, not the call sites.
/// </summary>
public static class EmailTemplate
{
    public static EmailMessage Render(
        string to,
        string subject,
        string brandName,
        IReadOnlyList<string> bodyLines,
        (string Url, string Label)? action = null,
        string? footer = null
    )
    {
        var text = new StringBuilder();
        foreach (var line in bodyLines)
            text.AppendLine(line);
        if (action is { } cta)
            text.AppendLine().AppendLine($"{cta.Label}: {cta.Url}");
        if (footer is not null)
            text.AppendLine().AppendLine(footer);

        var html = new StringBuilder();
        html.Append(
            "<div style=\"font-family:-apple-system,'Segoe UI',Roboto,sans-serif;max-width:36rem;margin:0 auto;padding:24px;color:#222\">"
        );
        html.Append(
            $"<h2 style=\"font-size:18px;margin:0 0 16px\">{WebUtility.HtmlEncode(brandName)}</h2>"
        );
        foreach (var line in bodyLines)
            html.Append($"<p style=\"margin:0 0 12px\">{WebUtility.HtmlEncode(line)}</p>");
        if (action is { } link)
            html.Append(
                $"<p style=\"margin:20px 0\"><a href=\"{WebUtility.HtmlEncode(link.Url)}\" "
                    + "style=\"background:#222;color:#fff;padding:10px 18px;border-radius:6px;text-decoration:none;display:inline-block\">"
                    + $"{WebUtility.HtmlEncode(link.Label)}</a></p>"
            );
        if (footer is not null)
            html.Append(
                $"<p style=\"margin:16px 0 0;font-size:12px;color:#888\">{WebUtility.HtmlEncode(footer)}</p>"
            );
        html.Append("</div>");

        return new EmailMessage(to, subject, text.ToString(), html.ToString());
    }
}
