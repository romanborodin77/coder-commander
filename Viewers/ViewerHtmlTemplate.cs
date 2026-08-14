using System.Globalization;
using CoderCommander.Services;

namespace CoderCommander.Viewers;

/// <summary>
/// Wraps an HTML body fragment in a full document styled to match the app's current theme - the
/// only formatting Markdown gets beyond Markdig's own output, and the shared look every Office
/// converter (<c>Viewers.Office</c>) renders its own generated markup into as well, since none of
/// it is Markdown-specific (bare <c>table</c>/<c>code</c>/<c>blockquote</c>/<c>img</c> selectors).
/// There is no live re-theming story for content already navigated into a <c>WebView2</c> (a theme
/// switch while a document is open re-renders on next load/reload, not in place - the same
/// limitation every other content already has for anything it can't cheaply repaint).
/// </summary>
internal static class ViewerHtmlTemplate
{
    /// <summary>Builds a self-contained document: no external stylesheet, font, or script
    /// reference - everything renders correctly even though <c>WebViewHost</c>'s security baseline
    /// blocks any fetch outside the file's own virtual-host folder.</summary>
    public static string WrapDocument(string bodyHtml)
    {
        var p = ThemeService.Current;
        return $$"""
            <!doctype html>
            <html>
            <head>
            <meta charset="utf-8">
            <style>
            html, body {
                margin: 0;
                padding: 20px 28px;
                background: {{Hex(p.Background)}};
                color: {{Hex(p.Foreground)}};
                font-family: "Segoe UI", sans-serif;
                font-size: 14px;
                line-height: 1.6;
            }
            h1, h2, h3, h4, h5, h6 { font-weight: 600; }
            h1 { border-bottom: 1px solid {{Hex(p.GridLine)}}; padding-bottom: 6px; }
            h2 { border-bottom: 1px solid {{Hex(p.GridLine)}}; padding-bottom: 4px; }
            a { color: {{Hex(p.Accent)}}; }
            code {
                font-family: Consolas, monospace;
                background: {{Hex(p.PanelBackground)}};
                padding: 1px 5px;
                border-radius: 3px;
                font-size: 13px;
            }
            pre {
                font-family: Consolas, monospace;
                background: {{Hex(p.PanelBackground)}};
                padding: 10px 14px;
                border-radius: 5px;
                overflow: auto;
                font-size: 13px;
            }
            pre code { background: none; padding: 0; }
            blockquote {
                margin: 0;
                padding: 2px 14px;
                border-left: 3px solid {{Hex(p.Accent)}};
                color: {{Hex(p.DimForeground)}};
            }
            table { border-collapse: collapse; margin: 8px 0; }
            th, td { border: 1px solid {{Hex(p.GridLine)}}; padding: 5px 10px; }
            th { background: {{Hex(p.PanelBackground)}}; }
            img { max-width: 100%; }
            hr { border: none; border-top: 1px solid {{Hex(p.GridLine)}}; margin: 16px 0; }
            ::selection { background: {{Hex(p.Selection)}}; color: {{Hex(p.SelectionForeground)}}; }
            </style>
            </head>
            <body>
            {{bodyHtml}}
            </body>
            </html>
            """;
    }

    private static string Hex(Color c) =>
        string.Create(CultureInfo.InvariantCulture, $"#{c.R:X2}{c.G:X2}{c.B:X2}");
}
