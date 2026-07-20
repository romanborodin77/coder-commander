using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Xml;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
using ICSharpCode.AvalonEdit.Rendering;

namespace CoderCommander.Services;

/// <summary>
/// Статический класс для применения подсветки синтаксиса AvalonEdit.
/// Поддерживает 19+ языков через встроенные XSHD-определения с VS Code-палитрой.
/// Цвета токенов разрешаются из словаря ресурсов темы (кисти Vs*/Diff*Brush) в момент
/// отрисовки, поэтому подсветка автоматически адаптируется к тёмной/светлой теме.
/// Static class for applying AvalonEdit syntax highlighting.
/// Supports 19+ languages via built-in XSHD definitions with the VS Code palette.
/// Token colors are resolved from the theme resource dictionary (Vs*/Diff*Brush brushes)
/// at render time, so highlighting automatically adapts to dark/light themes.
/// </summary>
public static class SyntaxHighlighter
{
    private static readonly ConcurrentDictionary<string, IHighlightingDefinition> _cache = new();
    private static readonly Dictionary<string, string> _customByName = new()
    {
        ["CSharp"] = CSharpXshd,
        ["JavaScript"] = JavaScriptXshd,
        ["Python"] = PythonXshd,
        ["Java"] = JavaXshd,
        ["Cpp"] = CppXshd,
        ["Json"] = JsonXshd,
        ["Sql"] = SqlXshd,
        ["Html"] = HtmlXshd,
        ["Xml"] = XmlXshd,
        ["MarkDown"] = MarkDownXshd,
        ["GitDiff"] = GitDiffXshd,
        ["Dockerfile"] = DockerfileXshd,
        ["CSS"] = CssXshd,
        ["YAML"] = YamlXshd,
        ["Bash"] = BashXshd,
        ["PowerShell"] = PowerShellXshd,
        ["Go"] = GoXshd,
        ["Rust"] = RustXshd,
        ["PHP"] = PhpXshd
    };

    /// <summary>
    /// Применяет подсветку синтаксиса к редактору на основе расширения файла.
    /// Applies syntax highlighting to the editor based on the file extension.
    /// </summary>
    /// <param name="ed">Экземпляр TextEditor AvalonEdit. AvalonEdit TextEditor instance.</param>
    /// <param name="path">Путь к файлу (используется расширение). File path (extension is used for detection).</param>
    public static void Apply(TextEditor ed, string path)
    {
        ed.SyntaxHighlighting = Resolve(path, ed.Text);
    }

    /// <summary>
    /// Применяет подсветку синтаксиса с возможностью передать содержимое для анализа (например, для GitDiff по содержанию).
    /// Applies syntax highlighting with optional content analysis (e.g. GitDiff detection by content).
    /// </summary>
    /// <param name="ed">Экземпляр TextEditor AvalonEdit. AvalonEdit TextEditor instance.</param>
    /// <param name="path">Путь к файлу. File path.</param>
    /// <param name="content">Содержимое файла для расширенного определения языка. File content for advanced language detection.</param>
    public static void ApplyByContent(TextEditor ed, string path, string? content)
    {
        ed.SyntaxHighlighting = Resolve(path, content);
    }

    /// <summary>
    /// Определяет название языка подсветки по пути файла и/или содержимому.
    /// Detects the highlighting language name by file path and/or content.
    /// </summary>
    /// <param name="path">Путь к файлу. File path.</param>
    /// <param name="content">Содержимое файла (опционально). File content (optional).</param>
    /// <returns>Название языка подсветки или null, если язык не определён. Highlighting language name or null if undetected.</returns>
    public static string? DetectLanguage(string path, string? content) =>
        Resolve(path, content)?.Name;

    private static IHighlightingDefinition? Resolve(string path, string? content)
    {
        var ext = (Path.GetExtension(path) ?? "").ToLowerInvariant();
        var name = Path.GetFileName(path);
        return ext switch
        {
            ".cs" => Custom("CSharp"),
            ".xml" or ".xaml" or ".csproj" or ".vcxproj" or ".config" or ".resx" or ".manifest" => Custom("Xml"),
            ".html" or ".htm" => Custom("Html"),
            ".js" or ".ts" or ".mjs" or ".cjs" or ".jsx" or ".tsx" => Custom("JavaScript"),
            ".json" => Custom("Json"),
            ".py" or ".pyw" => Custom("Python"),
            ".java" => Custom("Java"),
            ".cpp" or ".c" or ".h" or ".hpp" or ".cc" or ".cxx" or ".hh" => Custom("Cpp"),
            ".sql" => Custom("Sql"),
            ".md" or ".markdown" => Custom("MarkDown"),
            ".css" => Custom("CSS"),
            ".yml" or ".yaml" => Custom("YAML"),
            ".dockerfile" => Custom("Dockerfile"),
            ".diff" or ".patch" => Custom("GitDiff"),
            ".sh" or ".bash" => Custom("Bash"),
            ".ps1" or ".psm1" => Custom("PowerShell"),
            ".go" => Custom("Go"),
            ".rs" => Custom("Rust"),
            ".php" or ".phtml" => Custom("PHP"),
            ".vue" or ".svelte" => Custom("Html"),
            _ => ResolveByNameOrContent(name, content)
        };
    }

        private static IHighlightingDefinition? ResolveByNameOrContent(string? name, string? content)
        {
            if (name is not null)
            {
                if (name.Equals("Dockerfile", System.StringComparison.OrdinalIgnoreCase) ||
                    name.Equals(".dockerignore", System.StringComparison.OrdinalIgnoreCase))
                    return Custom("Dockerfile");

                if (name.Equals(".gitignore", System.StringComparison.OrdinalIgnoreCase) ||
                    name.Equals(".gitattributes", System.StringComparison.OrdinalIgnoreCase))
                    return Custom("GitDiff");
            }

            if (content != null && (content.StartsWith("diff --git") || content.StartsWith("@@") || content.StartsWith("index ")))
                return Custom("GitDiff");

            return null;
        }

        private static IHighlightingDefinition? Custom(string name)
        {
            if (_cache.TryGetValue(name, out var def)) return def;
            if (!_customByName.TryGetValue(name, out var xshd)) return null;
            def = _cache.GetOrAdd(name, _ =>
            {
                using var sr = new StringReader(xshd);
                using var xml = XmlReader.Create(sr);
                // ВНИМАНИЕ: передаём null (а не HighlightingManager.Instance). Иначе при совпадении
                // имени (CSharp, Java, JavaScript, Html, Xml, Css, Json, …) загрузчик подменяет
                // наше определение встроенным из AvalonEdit, и темозависимая подсветка не применяется.
                // WARNING: pass null (not HighlightingManager.Instance). Otherwise, for names that
                // collide with AvalonEdit built-ins, the loader returns the built-in definition and
                // our theme-aware highlighting is never used.
                var loaded = HighlightingLoader.Load(xml, null);
                // Заменяем захардкоженные hex-цвета на темозависимые кисти (Vs*/Diff*Brush),
                // разрешаемые из словаря ресурсов в момент отрисовки.
                // Replaces hardcoded hex colors with theme-aware brushes (Vs*/Diff*Brush)
                // resolved from the resource dictionary at render time.
                ApplyThemeBrushes(loaded);
                return loaded;
            });
            return def;
        }

        /// <summary>
        /// Заменяет кисть переднего плана каждого именованного цвета подсветки на кисть,
        /// разрешаемую из словаря ресурсов активной темы по имени токена.
        /// Сохраняет шрифт/стиль (курсив, жирность), заданные в XSHD.
        /// Replaces each named highlighting color's foreground brush with a brush that
        /// resolves from the active theme's resource dictionary by token name.
        /// Preserves the font style/weight declared in the XSHD.
        /// </summary>
        /// <param name="def">Загруженное определение подсветки. Loaded highlighting definition.</param>
        private static void ApplyThemeBrushes(IHighlightingDefinition def)
        {
            foreach (var color in def.NamedHighlightingColors)
            {
                if (ColorToResource.TryGetValue(color.Name, out var key))
                {
                    // Передаём исходную (hex) кисть как запасной вариант, если ресурс темы недоступен.
                    // Pass the original (hex) brush as a fallback in case the theme resource is missing.
                    color.Foreground = new ThemeResourceHighlightingBrush(key, color.Foreground);
                }
            }
        }

        /// <summary>
        /// Сопоставление логических имён цветов токенов (из XSHD) с ключами кистей темы.
        /// Mapping of logical token color names (from XSHD) to theme brush resource keys.
        /// </summary>
        private static readonly Dictionary<string, string> ColorToResource = new()
        {
            ["Comment"] = "VsComment",
            ["DocComment"] = "VsComment",
            ["Quote"] = "VsComment",
            ["String"] = "VsString",
            ["Code"] = "VsString",
            ["Value"] = "VsString",
            ["Important"] = "VsString",
            ["Keyword"] = "VsKeyword",
            ["Preproc"] = "VsPreproc",
            ["Entity"] = "VsPreproc",
            ["Header"] = "VsPreproc",
            ["AtRule"] = "VsPreproc",
            ["Anchor"] = "VsPreproc",
            ["Alias"] = "VsPreproc",
            ["Macro"] = "VsPreproc",
            ["Parameter"] = "VsPreproc",
            ["Type"] = "VsType",
            ["Pseudo"] = "VsType",
            ["Number"] = "VsNumber",
            ["Unit"] = "VsNumber",
            ["Method"] = "VsFunction",
            ["Function"] = "VsFunction",
            ["Emph"] = "VsFunction",
            ["Arg"] = "VsFunction",
            ["Command"] = "VsFunction",
            ["Cmdlet"] = "VsFunction",
            ["CssVar"] = "VsFunction",
            ["Punct"] = "VsPunct",
            ["Var"] = "VsVariable",
            ["Variable"] = "VsVariable",
            ["Splat"] = "VsVariable",
            ["Property"] = "VsVariable",
            ["Key"] = "VsVariable",
            ["Lifetime"] = "VsVariable",
            ["Bool"] = "VsBool",
            ["Boolean"] = "VsBool",
            ["Const"] = "VsConst",
            ["Tag"] = "VsTag",
            ["Instr"] = "VsTag",
            ["Selector"] = "VsTag",
            ["Dash"] = "VsTag",
            ["Link"] = "VsTag",
            ["Attr"] = "VsAttr",
            ["Prop"] = "VsAttr",
            ["Operator"] = "VsOperator",
            // Цвета git/diff берутся из темозависимых кистей Diff*Brush.
            // Git/diff colors are taken from theme-aware Diff*Brush brushes.
            ["Added"] = "DiffAddBrush",
            ["Removed"] = "DiffDelBrush",
            ["Hunk"] = "DiffHunkBrush",
            ["Meta"] = "DiffMetaBrush",
            ["File"] = "DiffFileBrush"
        };

        /// <summary>
        /// Кисть подсветки AvalonEdit, разрешающая цвет из словаря ресурсов активной темы
        /// в момент отрисовки строки. Благодаря этому подсветка синтаксиса автоматически
        /// перекрашивается при смене тёмной/светлой темы (словарь ресурсов заменяется
        /// «на месте» в App.ApplyTheme, и AvalonEdit при каждой отрисовке получает
        /// актуальную кисть через Application.Current.TryFindResource).
        /// AvalonEdit highlighting brush that resolves its color from the active theme's
        /// resource dictionary at render time. This makes syntax highlighting automatically
        /// recolor when switching dark/light themes (the resource dictionary is replaced
        /// in-place in App.ApplyTheme, and AvalonEdit fetches the current brush on every
        /// redraw via Application.Current.TryFindResource).
        /// </summary>
        private sealed class ThemeResourceHighlightingBrush : HighlightingBrush
        {
            private readonly string _resourceKey;
            private readonly HighlightingBrush? _fallback;

            public ThemeResourceHighlightingBrush(string resourceKey, HighlightingBrush? fallback)
            {
                _resourceKey = resourceKey;
                _fallback = fallback;
            }

            public override Brush GetBrush(ITextRunConstructionContext context)
            {
                if (Application.Current?.TryFindResource(_resourceKey) is Brush brush)
                    return brush;
                return _fallback?.GetBrush(context) ?? Brushes.Transparent;
            }

            public override string ToString() => "Theme:" + _resourceKey;
        }

    // ===================== VS Code-style tokens (цвета разрешаются из кистей темы Vs*/Diff*) =====================

    // C#
    private const string CSharpXshd = @"<?xml version=""1.0"" encoding=""utf-8""?>
<SyntaxDefinition name=""CSharp"" xmlns=""http://icsharpcode.net/sharpdevelop/syntaxdefinition/2008"">
    <Color name=""Comment"" foreground=""#6A9955"" fontStyle=""italic"" />
    <Color name=""String"" foreground=""#CE9178"" />
    <Color name=""Keyword"" foreground=""#569CD6"" />
    <Color name=""Preproc"" foreground=""#C586C0"" />
    <Color name=""Type"" foreground=""#4EC9B0"" />
    <Color name=""Number"" foreground=""#B5CEA8"" />
    <Color name=""Method"" foreground=""#DCDCAA"" />
    <Color name=""Punct"" foreground=""#D4D4D4"" />
    <RuleSet>
        <Span color=""Comment""><Begin>//</Begin><End>$</End></Span>
        <Span color=""Comment""><Begin>/\*</Begin><End>\*/</End></Span>
        <Span color=""String""><Begin>""</Begin><End>""</End></Span>
        <Span color=""String""><Begin>@""</Begin><End>""</End></Span>
        <Span color=""String""><Begin>'</Begin><End>'</End></Span>
        <Rule color=""Preproc"">^\s*\#.*$</Rule>
        <Rule color=""Number"">\b\d+(\.\d+)?[fFdDmMlLuU]?\b</Rule>
        <Rule color=""Keyword"">\b(abstract|as|base|break|case|catch|checked|class|const|continue|default|delegate|do|else|enum|event|explicit|extern|false|file|fixed|for|foreach|goto|if|implicit|in|init|interface|internal|is|lock|new|null|object|operator|out|override|params|private|protected|public|readonly|record|ref|required|return|scoped|sealed|sizeof|stackalloc|static|switch|this|throw|true|try|typeof|uint|ulong|unchecked|unsafe|ushort|using|virtual|void|volatile|while|add|alias|ascending|async|await|by|descending|dynamic|equals|from|get|global|group|into|join|let|orderby|partial|remove|select|set|value|var|where|yield)\b</Rule>
        <Rule color=""Type"">\b(bool|byte|char|decimal|double|float|int|long|nint|nuint|sbyte|short|string|uint|ulong|ushort)\b</Rule>
        <Rule color=""Method"">\b[A-Za-z_]\w*(?=\s*\()</Rule>
    </RuleSet>
</SyntaxDefinition>";

    // JavaScript / TypeScript
    private const string JavaScriptXshd = @"<?xml version=""1.0"" encoding=""utf-8""?>
<SyntaxDefinition name=""JavaScript"" xmlns=""http://icsharpcode.net/sharpdevelop/syntaxdefinition/2008"">
    <Color name=""Comment"" foreground=""#6A9955"" fontStyle=""italic"" />
    <Color name=""String"" foreground=""#CE9178"" />
    <Color name=""Keyword"" foreground=""#569CD6"" />
    <Color name=""Number"" foreground=""#B5CEA8"" />
    <Color name=""Function"" foreground=""#DCDCAA"" />
    <Color name=""Type"" foreground=""#4EC9B0"" />
    <Color name=""Bool"" foreground=""#569CD6"" />
    <RuleSet>
        <Span color=""Comment""><Begin>//</Begin><End>$</End></Span>
        <Span color=""Comment""><Begin>/\*</Begin><End>\*/</End></Span>
        <Span color=""String""><Begin>""</Begin><End>""</End></Span>
        <Span color=""String""><Begin>'</Begin><End>'</End></Span>
        <Span color=""String""><Begin>`</Begin><End>`</End></Span>
        <Rule color=""Number"">\b\d+(\.\d+)?\b</Rule>
        <Rule color=""Keyword"">\b(asserts|as|async|await|break|case|catch|class|const|continue|debugger|default|delete|do|else|enum|export|extends|finally|for|from|function|get|if|implements|import|in|infer|instanceof|interface|keyof|let|namespace|new|of|override|private|protected|public|readonly|return|satisfies|set|static|super|switch|this|throw|try|type|typeof|var|void|while|with|yield)\b</Rule>
        <Rule color=""Bool"">\b(true|false|null|undefined|NaN)\b</Rule>
        <Rule color=""Function"">\b[A-Za-z_$]\w*(?=\s*\()</Rule>
        <Rule color=""Type"">\b[A-Z][A-Za-z0-9_]*\b</Rule>
    </RuleSet>
</SyntaxDefinition>";

    // Python
    private const string PythonXshd = @"<?xml version=""1.0"" encoding=""utf-8""?>
<SyntaxDefinition name=""Python"" xmlns=""http://icsharpcode.net/sharpdevelop/syntaxdefinition/2008"">
    <Color name=""Comment"" foreground=""#6A9955"" fontStyle=""italic"" />
    <Color name=""String"" foreground=""#CE9178"" />
    <Color name=""Keyword"" foreground=""#569CD6"" />
    <Color name=""Number"" foreground=""#B5CEA8"" />
    <Color name=""Function"" foreground=""#DCDCAA"" />
    <Color name=""Bool"" foreground=""#569CD6"" />
    <Color name=""Type"" foreground=""#4EC9B0"" />
    <RuleSet>
        <Span color=""Comment""><Begin>\#</Begin><End>$</End></Span>
        <Span color=""String""><Begin>""&quot;</Begin><End>&quot;&quot;</End></Span>
        <Span color=""String""><Begin>f""</Begin><End>""</End></Span>
        <Span color=""String""><Begin>f'</Begin><End>'</End></Span>
        <Span color=""String""><Begin>F""</Begin><End>""</End></Span>
        <Span color=""String""><Begin>F'</Begin><End>'</End></Span>
        <Span color=""String""><Begin>'''</Begin><End>'''</End></Span>
        <Span color=""String""><Begin>'</Begin><End>'</End></Span>
        <Rule color=""Number"">\b\d+(\.\d+)?[jJlL]?\b</Rule>
        <Rule color=""Keyword"">\b(and|as|assert|async|await|break|class|continue|def|del|elif|else|except|finally|for|from|global|if|import|in|is|lambda|nonlocal|not|or|pass|raise|return|try|type|while|with|yield|match|case)\b</Rule>
        <Rule color=""Bool"">\b(True|False|None)\b</Rule>
        <Rule color=""Function"">\b[A-Za-z_]\w*(?=\s*\()</Rule>
        <Rule color=""Type"">\b[A-Z][A-Za-z0-9_]*\b</Rule>
    </RuleSet>
</SyntaxDefinition>";

    // Java
    private const string JavaXshd = @"<?xml version=""1.0"" encoding=""utf-8""?>
<SyntaxDefinition name=""Java"" xmlns=""http://icsharpcode.net/sharpdevelop/syntaxdefinition/2008"">
    <Color name=""Comment"" foreground=""#6A9955"" fontStyle=""italic"" />
    <Color name=""String"" foreground=""#CE9178"" />
    <Color name=""Keyword"" foreground=""#569CD6"" />
    <Color name=""Number"" foreground=""#B5CEA8"" />
    <Color name=""Method"" foreground=""#DCDCAA"" />
    <Color name=""Type"" foreground=""#4EC9B0"" />
    <RuleSet>
        <Span color=""Comment""><Begin>//</Begin><End>$</End></Span>
        <Span color=""Comment""><Begin>/\*</Begin><End>\*/</End></Span>
        <Span color=""String""><Begin>""</Begin><End>""</End></Span>
        <Span color=""String""><Begin>'</Begin><End>'</End></Span>
        <Rule color=""Number"">\b\d+(\.\d+)?[fFdDlL]?\b</Rule>
        <Rule color=""Keyword"">\b(abstract|assert|boolean|break|byte|case|catch|char|class|const|continue|default|do|double|else|enum|extends|final|finally|float|for|goto|if|implements|import|instanceof|int|interface|long|native|new|package|private|protected|public|return|short|static|strictfp|super|switch|synchronized|this|throw|throws|transient|try|void|volatile|while)\b</Rule>
        <Rule color=""Type"">\b(bool|byte|char|double|float|int|long|short|String|void|boolean)\b</Rule>
        <Rule color=""Method"">\b[A-Za-z_]\w*(?=\s*\()</Rule>
    </RuleSet>
</SyntaxDefinition>";

    // C / C++
    private const string CppXshd = @"<?xml version=""1.0"" encoding=""utf-8""?>
<SyntaxDefinition name=""Cpp"" xmlns=""http://icsharpcode.net/sharpdevelop/syntaxdefinition/2008"">
    <Color name=""Comment"" foreground=""#6A9955"" fontStyle=""italic"" />
    <Color name=""String"" foreground=""#CE9178"" />
    <Color name=""Keyword"" foreground=""#569CD6"" />
    <Color name=""Preproc"" foreground=""#C586C0"" />
    <Color name=""Number"" foreground=""#B5CEA8"" />
    <Color name=""Method"" foreground=""#DCDCAA"" />
    <Color name=""Type"" foreground=""#4EC9B0"" />
    <RuleSet>
        <Span color=""Comment""><Begin>//</Begin><End>$</End></Span>
        <Span color=""Comment""><Begin>/\*</Begin><End>\*/</End></Span>
        <Span color=""String""><Begin>""</Begin><End>""</End></Span>
        <Span color=""String""><Begin>'</Begin><End>'</End></Span>
        <Rule color=""Preproc"">^\s*\#.*$</Rule>
        <Rule color=""Number"">\b\d+(\.\d+)?[fFuUlL]?\b</Rule>
        <Rule color=""Keyword"">\b(alignas|alignof|asm|auto|bool|break|case|catch|char|char8_t|char16_t|char32_t|class|co_await|co_return|co_yield|const|consteval|constexpr|constinit|continue|decltype|default|delete|do|double|dynamic_cast|else|enum|explicit|export|extern|false|final|float|for|friend|goto|if|inline|int|long|mutable|namespace|new|noexcept|nullptr|operator|override|private|protected|public|register|reinterpret_cast|return|short|signed|sizeof|static|static_assert|static_cast|struct|switch|template|this|thread_local|throw|true|try|typedef|typeid|typename|union|unsigned|using|virtual|void|volatile|wchar_t|while)\b</Rule>
        <Rule color=""Type"">\b[A-Z][A-Za-z0-9_]*\b</Rule>
        <Rule color=""Method"">\b[A-Za-z_]\w*(?=\s*\()</Rule>
    </RuleSet>
</SyntaxDefinition>";

    // JSON
    private const string JsonXshd = @"<?xml version=""1.0"" encoding=""utf-8""?>
<SyntaxDefinition name=""Json"" xmlns=""http://icsharpcode.net/sharpdevelop/syntaxdefinition/2008"">
    <Color name=""Property"" foreground=""#9CDCFE"" />
    <Color name=""String"" foreground=""#CE9178"" />
    <Color name=""Number"" foreground=""#B5CEA8"" />
    <Color name=""Bool"" foreground=""#569CD6"" />
    <Color name=""Punct"" foreground=""#D4D4D4"" />
    <RuleSet>
        <Rule color=""Property"">""(\\.|[^""\\])*""(?=\s*:)</Rule>
        <Rule color=""String"">""(\\.|[^""\\])*""</Rule>
        <Rule color=""Number"">-?\b\d+(\.\d+)?([eE][+-]?\d+)?\b</Rule>
        <Rule color=""Bool"">\b(true|false|null)\b</Rule>
        <Rule color=""Punct"">[{}\[\]:,]</Rule>
    </RuleSet>
</SyntaxDefinition>";

    // SQL
    private const string SqlXshd = @"<?xml version=""1.0"" encoding=""utf-8""?>
<SyntaxDefinition name=""Sql"" xmlns=""http://icsharpcode.net/sharpdevelop/syntaxdefinition/2008"">
    <Color name=""Keyword"" foreground=""#C586C0"" />
    <Color name=""String"" foreground=""#CE9178"" />
    <Color name=""Comment"" foreground=""#6A9955"" fontStyle=""italic"" />
    <Color name=""Number"" foreground=""#B5CEA8"" />
    <Color name=""Function"" foreground=""#DCDCAA"" />
    <RuleSet>
        <Span color=""Comment""><Begin>--</Begin><End>$</End></Span>
        <Span color=""Comment""><Begin>/\*</Begin><End>\*/</End></Span>
        <Span color=""String""><Begin>''</Begin><End>''</End></Span>
        <Rule color=""Number"">\b\d+(\.\d+)?\b</Rule>
        <Rule color=""Keyword"">\b(SELECT|FROM|WHERE|INSERT|INTO|VALUES|UPDATE|SET|DELETE|CREATE|TABLE|DROP|ALTER|ADD|COLUMN|INDEX|VIEW|TRIGGER|PROCEDURE|FUNCTION|JOIN|INNER|LEFT|RIGHT|FULL|OUTER|CROSS|ON|USING|GROUP|BY|ORDER|HAVING|LIMIT|OFFSET|DISTINCT|AS|AND|OR|NOT|NULL|IS|IN|EXISTS|BETWEEN|LIKE|CASE|WHEN|THEN|ELSE|END|UNION|ALL|PRIMARY|FOREIGN|KEY|REFERENCES|CONSTRAINT|DEFAULT|UNIQUE|CHECK|GRANT|REVOKE|BEGIN|COMMIT|ROLLBACK|TRANSACTION|WITH|RETURNING|CAST|COALESCE)\b</Rule>
        <Rule color=""Function"">\b[A-Za-z_]\w*(?=\s*\()</Rule>
    </RuleSet>
</SyntaxDefinition>";

    // HTML
    private const string HtmlXshd = @"<?xml version=""1.0"" encoding=""utf-8""?>
<SyntaxDefinition name=""Html"" xmlns=""http://icsharpcode.net/sharpdevelop/syntaxdefinition/2008"">
    <Color name=""Tag"" foreground=""#569CD6"" />
    <Color name=""Attr"" foreground=""#9CDCFE"" />
    <Color name=""String"" foreground=""#CE9178"" />
    <Color name=""Comment"" foreground=""#6A9955"" fontStyle=""italic"" />
    <Color name=""Entity"" foreground=""#C586C0"" />
    <RuleSet>
        <Span color=""Comment""><Begin>&lt;!--</Begin><End>--&gt;</End></Span>
        <Rule color=""Tag"">&lt;/?</Rule>
        <Rule color=""Tag"">&gt;</Rule>
        <Rule color=""Attr"">\b[a-zA-Z-]+(?==)</Rule>
        <Span color=""String""><Begin>""</Begin><End>""</End></Span>
        <Span color=""String""><Begin>'</Begin><End>'</End></Span>
        <Rule color=""Entity"">&amp;\#[0-9a-zA-Z]+;</Rule>
    </RuleSet>
</SyntaxDefinition>";

    // XML
    private const string XmlXshd = @"<?xml version=""1.0"" encoding=""utf-8""?>
<SyntaxDefinition name=""Xml"" xmlns=""http://icsharpcode.net/sharpdevelop/syntaxdefinition/2008"">
    <Color name=""Tag"" foreground=""#569CD6"" />
    <Color name=""Attr"" foreground=""#9CDCFE"" />
    <Color name=""String"" foreground=""#CE9178"" />
    <Color name=""Comment"" foreground=""#6A9955"" fontStyle=""italic"" />
    <RuleSet>
        <Span color=""Comment""><Begin>&lt;!--</Begin><End>--&gt;</End></Span>
        <Rule color=""Tag"">&lt;/?</Rule>
        <Rule color=""Tag"">&gt;</Rule>
        <Rule color=""Tag"">/&gt;</Rule>
        <Rule color=""Attr"">\b[a-zA-Z-]+(?==)</Rule>
        <Span color=""String""><Begin>""</Begin><End>""</End></Span>
        <Span color=""String""><Begin>'</Begin><End>'</End></Span>
    </RuleSet>
</SyntaxDefinition>";

    // Markdown
    private const string MarkDownXshd = @"<?xml version=""1.0"" encoding=""utf-8""?>
<SyntaxDefinition name=""MarkDown"" xmlns=""http://icsharpcode.net/sharpdevelop/syntaxdefinition/2008"">
    <Color name=""Header"" foreground=""#C586C0"" fontWeight=""bold"" />
    <Color name=""Emph"" foreground=""#DCDCAA"" />
    <Color name=""Code"" foreground=""#CE9178"" />
    <Color name=""Link"" foreground=""#569CD6"" />
    <Color name=""Quote"" foreground=""#6A9955"" fontStyle=""italic"" />
    <RuleSet>
        <Rule color=""Header"">^\#{1,6}\s.*$</Rule>
        <Rule color=""Quote"">^&gt;\s.*$</Rule>
        <Rule color=""Code"">`[^`]*`</Rule>
        <Rule color=""Emph"">\*\*[^*]+\*\*</Rule>
        <Rule color=""Emph"">\*[^*]+\*</Rule>
        <Rule color=""Link"">\[[^\]]*\]\([^)]*\)</Rule>
        <Rule color=""Emph"">^[-*+]\s.*$</Rule>
        <Rule color=""Emph"">^\d+\.\s.*$</Rule>
    </RuleSet>
</SyntaxDefinition>";

    // ===================== Custom (already VS Code tokenized) =====================

    private const string GitDiffXshd = @"<?xml version=""1.0"" encoding=""utf-8""?>
<SyntaxDefinition name=""GitDiff"" xmlns=""http://icsharpcode.net/sharpdevelop/syntaxdefinition/2008"">
    <Color name=""Added"" foreground=""#4EC9B0"" fontWeight=""normal"" />
    <Color name=""Removed"" foreground=""#CE9178"" fontWeight=""normal"" />
    <Color name=""Hunk"" foreground=""#569CD6"" fontWeight=""bold"" />
    <Color name=""Meta"" foreground=""#C586C0"" fontWeight=""normal"" />
    <Color name=""File"" foreground=""#DCDCAA"" fontWeight=""bold"" />
    <Color name=""Comment"" foreground=""#6A9955"" fontStyle=""italic"" />
    <RuleSet>
        <Rule color=""File"">^diff .*$</Rule>
        <Rule color=""File"">^\+\+\+ .*$|^--- .*$</Rule>
        <Rule color=""Meta"">^index .*$|^new file .*$|^deleted file .*$|^similarity .*$|^rename .*$|^old mode .*$|^new mode .*$</Rule>
        <Rule color=""Hunk"">^@@ .*@@.*$</Rule>
        <Rule color=""Added"">^\+.*$</Rule>
        <Rule color=""Removed"">^-.*$</Rule>
        <Rule color=""Comment"">^\\ No newline.*$</Rule>
    </RuleSet>
</SyntaxDefinition>";

    private const string DockerfileXshd = @"<?xml version=""1.0"" encoding=""utf-8""?>
<SyntaxDefinition name=""Dockerfile"" xmlns=""http://icsharpcode.net/sharpdevelop/syntaxdefinition/2008"">
    <Color name=""Instr"" foreground=""#569CD6"" fontWeight=""bold"" />
    <Color name=""Arg"" foreground=""#DCDCAA"" fontWeight=""normal"" />
    <Color name=""String"" foreground=""#CE9178"" fontWeight=""normal"" />
    <Color name=""Comment"" foreground=""#6A9955"" fontStyle=""italic"" />
    <Color name=""Var"" foreground=""#9CDCFE"" fontWeight=""normal"" />
    <RuleSet>
        <Rule color=""Comment"">\#.*$</Rule>
        <Rule color=""Instr"">^\s*(FROM|RUN|CMD|LABEL|EXPOSE|ENV|ADD|COPY|ENTRYPOINT|VOLUME|USER|WORKDIR|ARG|ONBUILD|STOPSIGNAL|HEALTHCHECK|SHELL|MAINTAINER)\b</Rule>
        <Rule color=""Var"">\$\{?[A-Za-z_][A-Za-z0-9_]*\}?</Rule>
        <Rule color=""String"">&quot;.*?&quot;|'.*?'</Rule>
    </RuleSet>
</SyntaxDefinition>";

    private const string CssXshd = @"<?xml version=""1.0"" encoding=""utf-8""?>
<SyntaxDefinition name=""CSS"" xmlns=""http://icsharpcode.net/sharpdevelop/syntaxdefinition/2008"">
    <Color name=""Selector"" foreground=""#569CD6"" fontWeight=""normal"" />
    <Color name=""Prop"" foreground=""#9CDCFE"" fontWeight=""normal"" />
    <Color name=""Value"" foreground=""#CE9178"" fontWeight=""normal"" />
    <Color name=""Comment"" foreground=""#6A9955"" fontStyle=""italic"" />
    <Color name=""AtRule"" foreground=""#C586C0"" fontWeight=""bold"" />
    <Color name=""Important"" foreground=""#CE9178"" fontWeight=""bold"" />
    <Color name=""Pseudo"" foreground=""#4EC9B0"" fontWeight=""normal"" />
    <Color name=""CssVar"" foreground=""#DCDCAA"" fontWeight=""normal"" />
    <Color name=""Number"" foreground=""#B5CEA8"" fontWeight=""normal"" />
    <Color name=""Unit"" foreground=""#B5CEA8"" fontWeight=""normal"" />
    <RuleSet>
        <Rule color=""Comment"">/\*.*?\*/</Rule>
        <Rule color=""AtRule"">@[\w-]+\b</Rule>
        <Rule color=""Important"">!important</Rule>
        <Rule color=""Prop"">[\w-]+(?=\s*:)</Rule>
        <Rule color=""Pseudo"">::?[\w-]+</Rule>
        <Rule color=""CssVar"">--[\w-]+</Rule>
        <Rule color=""Number"">\b\d+(\.\d+)?</Rule>
        <Rule color=""Unit"">(?&lt;=\d)(%|px|rem|em|vh|vw|vmin|vmax|cm|mm|in|pt|pc|ch|ex|deg|rad|grad|ms|s|Hz|kHz|dpi|dpcm|dppx)\b</Rule>
        <Rule color=""Value"">:\s*[^;{}]+</Rule>
    </RuleSet>
</SyntaxDefinition>";

    private const string YamlXshd = @"<?xml version=""1.0"" encoding=""utf-8""?>
<SyntaxDefinition name=""YAML"" xmlns=""http://icsharpcode.net/sharpdevelop/syntaxdefinition/2008"">
    <Color name=""Key"" foreground=""#9CDCFE"" fontWeight=""normal"" />
    <Color name=""Val"" foreground=""#CE9178"" fontWeight=""normal"" />
    <Color name=""Comment"" foreground=""#6A9955"" fontStyle=""italic"" />
    <Color name=""Dash"" foreground=""#569CD6"" fontWeight=""normal"" />
    <Color name=""Anchor"" foreground=""#C586C0"" fontWeight=""normal"" />
    <Color name=""Alias"" foreground=""#C586C0"" fontWeight=""bold"" />
    <Color name=""Boolean"" foreground=""#569CD6"" fontWeight=""bold"" />
    <Color name=""Number"" foreground=""#B5CEA8"" fontWeight=""normal"" />
    <RuleSet>
        <Rule color=""Comment"">\#.*$</Rule>
        <Rule color=""Boolean"">\b(true|false|yes|no|on|off|null|~)\b</Rule>
        <Rule color=""Number"">\b\d+(\.\d+)?([eE][+-]?\d+)?\b</Rule>
        <Rule color=""Key"">^[\t ]*-?\s*[\w.\-/]+:(?=\s|$)</Rule>
        <Rule color=""Anchor"">&amp;[\w-]+</Rule>
        <Rule color=""Alias"">\*[\w-]+</Rule>
        <Rule color=""Dash"">^[\t ]*-\s</Rule>
        <Rule color=""Val"">:\s+.+$</Rule>
        <Rule color=""Val"">^\s*[|&gt;][+-]?\s*$</Rule>
    </RuleSet>
</SyntaxDefinition>";

    private const string BashXshd = @"<?xml version=""1.0"" encoding=""utf-8""?>
<SyntaxDefinition name=""Bash"" xmlns=""http://icsharpcode.net/sharpdevelop/syntaxdefinition/2008"">
    <Color name=""Keyword"" foreground=""#569CD6"" fontWeight=""bold"" />
    <Color name=""String"" foreground=""#CE9178"" fontWeight=""normal"" />
    <Color name=""Comment"" foreground=""#6A9955"" fontStyle=""italic"" />
    <Color name=""Var"" foreground=""#9CDCFE"" fontWeight=""normal"" />
    <Color name=""Command"" foreground=""#DCDCAA"" fontWeight=""normal"" />
    <Color name=""Number"" foreground=""#B5CEA8"" fontWeight=""normal"" />
    <Color name=""Operator"" foreground=""#D4D4D4"" fontWeight=""normal"" />
    <RuleSet>
        <Rule color=""Comment"">\#.*$</Rule>
        <Rule color=""Keyword"">\b(if|then|else|elif|fi|for|while|do|done|case|esac|in|function|return|export|source|local|declare|readonly|typeset|select|until|shift|continue|break|exec|eval|trap|exit|unset|let)\b</Rule>
        <Rule color=""Command"">\b(echo|cd|pwd|exit|ls|cp|mv|rm|mkdir|rmdir|chmod|chown|chgrp|grep|egrep|fgrep|sed|awk|cat|find|sudo|apt|apt-get|git|curl|wget|ssh|scp|kill|ps|top|htop|df|du|tar|gzip|gunzip|bzip2|unzip|zip|make|cmake|diff|patch|head|tail|less|more|sort|uniq|wc|tee|xargs|test|printf|read|sleep|which|whereis|basename|dirname|realpath|touch|ln|mount|umount|systemctl|journalctl|docker|pip|npm|yarn)\b</Rule>
        <Rule color=""Var"">\$\{?[\w]+\}?</Rule>
        <Rule color=""Number"">\b\d+\b</Rule>
        <Rule color=""Operator"">\[\[|\]\]</Rule>
        <Rule color=""String"">&quot;.*?&quot;|'.*?'</Rule>
        <Span color=""String""><Begin>&lt;&lt;[-]?\w+</Begin><End>^\w+$</End></Span>
    </RuleSet>
</SyntaxDefinition>";

    private const string PowerShellXshd = @"<?xml version=""1.0"" encoding=""utf-8""?>
<SyntaxDefinition name=""PowerShell"" xmlns=""http://icsharpcode.net/sharpdevelop/syntaxdefinition/2008"">
    <Color name=""Keyword"" foreground=""#569CD6"" fontWeight=""bold"" />
    <Color name=""String"" foreground=""#CE9178"" fontWeight=""normal"" />
    <Color name=""Comment"" foreground=""#6A9955"" fontStyle=""italic"" />
    <Color name=""Var"" foreground=""#9CDCFE"" fontWeight=""normal"" />
    <Color name=""Splat"" foreground=""#9CDCFE"" fontWeight=""bold"" />
    <Color name=""Cmdlet"" foreground=""#DCDCAA"" fontWeight=""normal"" />
    <Color name=""Parameter"" foreground=""#C586C0"" fontWeight=""normal"" />
    <Color name=""Number"" foreground=""#B5CEA8"" fontWeight=""normal"" />
    <Color name=""Operator"" foreground=""#D4D4D4"" fontWeight=""normal"" />
    <RuleSet>
        <Rule color=""Comment"">\#.*$</Rule>
        <Rule color=""Keyword"">\b(if|else|elseif|switch|foreach|for|while|do|until|break|continue|return|function|filter|param|begin|process|end|try|catch|finally|throw|data|inlinescript|workflow|class|enum|define|from|in|inlinescript|parallel|sequence|using|var)\b</Rule>
        <Rule color=""Cmdlet"">\b(Get|Set|New|Remove|Add|Update|Test|Invoke|Start|Stop|Restart|Write|Read|Select|Where|ForEach|Sort|Group|Measure|Compare|Out|Export|Import|ConvertTo|ConvertFrom|Convert|Clear|Copy|Move|Rename|Enter|Exit|Push|Pop|Join|Split|Resolve|Format|Register|Unregister|Enable|Disable|Mount|Dismount|Debug|Watch|Send|Receive|Block|Unblock|Grant|Revoke|Deny|Allow)-[\w-]+\b</Rule>
        <Rule color=""Operator"">-eq|-ne|-gt|-ge|-lt|-le|-like|-notlike|-match|-notmatch|-contains|-notcontains|-in|-notin|-replace|-and|-or|-not|-band|-bor|-bxor|-shl|-shr|-as|-is|-join|-split|\+=|-=|\*=|/=|%=|=>|\.\.</Rule>
        <Rule color=""Parameter"">-[\w]+</Rule>
        <Rule color=""Var"">\$[\w]+</Rule>
        <Rule color=""Splat"">@[\w]+</Rule>
        <Rule color=""Number"">\b\d+(\.\d+)?\b</Rule>
        <Rule color=""String"">&quot;.*?&quot;|'.*?'</Rule>
        <Span color=""String""><Begin>@&quot;</Begin><End>&quot;@</End></Span>
        <Span color=""String""><Begin>@'</Begin><End>'@</End></Span>
    </RuleSet>
</SyntaxDefinition>";

// Go
private const string GoXshd = @"<?xml version=""1.0"" encoding=""utf-8""?>
<SyntaxDefinition name=""Go"" xmlns=""http://icsharpcode.net/sharpdevelop/syntaxdefinition/2008"">
    <Color name=""Comment"" foreground=""#6A9955"" fontStyle=""italic"" />
    <Color name=""String"" foreground=""#CE9178"" />
    <Color name=""Keyword"" foreground=""#569CD6"" fontWeight=""bold"" />
    <Color name=""Type"" foreground=""#4EC9B0"" />
    <Color name=""Number"" foreground=""#B5CEA8"" />
    <Color name=""Function"" foreground=""#DCDCAA"" />
    <Color name=""Preproc"" foreground=""#C586C0"" />
    <Color name=""Const"" foreground=""#4FC1FF"" fontWeight=""bold"" />
    <RuleSet>
        <Span color=""Comment""><Begin>//</Begin><End>$</End></Span>
        <Span color=""Comment""><Begin>/\*</Begin><End>\*/</End></Span>
        <Span color=""String""><Begin>""</Begin><End>""</End></Span>
        <Span color=""String""><Begin>`</Begin><End>`</End></Span>
        <Span color=""String""><Begin>'</Begin><End>'</End></Span>
        <Rule color=""Keyword"">\b(break|case|chan|const|continue|default|defer|else|fallthrough|for|func|go|goto|if|import|interface|map|package|range|return|select|struct|switch|type|var)\b</Rule>
        <Rule color=""Const"">\b(true|false|nil|iota)\b</Rule>
        <Rule color=""Type"">\b(bool|byte|complex64|complex128|error|float32|float64|int|int8|int16|int32|int64|rune|string|uint|uint8|uint16|uint32|uint64|uintptr)\b</Rule>
        <Rule color=""Number"">\b\d+(\.\d+)?[i]?\b</Rule>
        <Rule color=""Function"">\b[A-Za-z_]\w*(?=\s*\()</Rule>
        <Rule color=""Preproc"">\b(append|cap|close|complex|copy|delete|imag|len|make|new|panic|print|println|real|recover)\b</Rule>
    </RuleSet>
</SyntaxDefinition>";

// Rust
private const string RustXshd = @"<?xml version=""1.0"" encoding=""utf-8""?>
<SyntaxDefinition name=""Rust"" xmlns=""http://icsharpcode.net/sharpdevelop/syntaxdefinition/2008"">
    <Color name=""Comment"" foreground=""#6A9955"" fontStyle=""italic"" />
    <Color name=""DocComment"" foreground=""#6A9955"" fontWeight=""bold"" />
    <Color name=""String"" foreground=""#CE9178"" />
    <Color name=""Keyword"" foreground=""#569CD6"" fontWeight=""bold"" />
    <Color name=""Type"" foreground=""#4EC9B0"" />
    <Color name=""Number"" foreground=""#B5CEA8"" />
    <Color name=""Function"" foreground=""#DCDCAA"" />
    <Color name=""Macro"" foreground=""#C586C0"" fontWeight=""bold"" />
    <Color name=""Lifetime"" foreground=""#9CDCFE"" fontStyle=""italic"" />
    <Color name=""Attr"" foreground=""#C586C0"" fontStyle=""italic"" />
    <Color name=""Const"" foreground=""#4FC1FF"" fontWeight=""bold"" />
    <RuleSet>
        <Span color=""Comment""><Begin>//!</Begin><End>$</End></Span>
        <Span color=""DocComment""><Begin>///</Begin><End>$</End></Span>
        <Span color=""Comment""><Begin>//</Begin><End>$</End></Span>
        <Span color=""Comment""><Begin>/\*</Begin><End>\*/</End></Span>
        <Span color=""String""><Begin>""</Begin><End>""</End></Span>
        <Span color=""String""><Begin>'</Begin><End>'</End></Span>
        <Rule color=""Keyword"">\b(as|async|await|break|const|crate|dyn|else|enum|extern|fn|for|if|impl|in|let|loop|match|mod|move|mut|priv|pub|ref|return|self|Self|static|struct|super|trait|type|union|unsafe|unsized|use|where|while|yield)\b</Rule>
        <Rule color=""Const"">\b(true|false)\b</Rule>
        <Rule color=""Type"">\b(bool|char|f32|f64|i8|i16|i32|i64|i128|isize|str|u8|u16|u32|u64|u128|usize|String|Vec|Option|Result|Box|Rc|Arc|Cell|RefCell|HashMap|HashSet|BTreeMap|BTreeSet|Iterator)\b</Rule>
        <Rule color=""Number"">\b\d+(\.\d+)?[fF]?\b</Rule>
        <Rule color=""Macro"">\b[A-Za-z_]\w*!</Rule>
        <Rule color=""Lifetime"">'[a-zA-Z_]\w*</Rule>
        <Rule color=""Attr"">\#!?\[.*?\]</Rule>
        <Rule color=""Function"">\b[A-Za-z_]\w*(?=\s*\()</Rule>
    </RuleSet>
</SyntaxDefinition>";

// PHP
private const string PhpXshd = @"<?xml version=""1.0"" encoding=""utf-8""?>
<SyntaxDefinition name=""PHP"" xmlns=""http://icsharpcode.net/sharpdevelop/syntaxdefinition/2008"">
    <Color name=""Comment"" foreground=""#6A9955"" fontStyle=""italic"" />
    <Color name=""String"" foreground=""#CE9178"" />
    <Color name=""Keyword"" foreground=""#569CD6"" fontWeight=""bold"" />
    <Color name=""Number"" foreground=""#B5CEA8"" />
    <Color name=""Function"" foreground=""#DCDCAA"" />
    <Color name=""Variable"" foreground=""#9CDCFE"" />
    <Color name=""Tag"" foreground=""#C586C0"" fontWeight=""bold"" />
    <Color name=""Type"" foreground=""#4EC9B0"" />
    <Color name=""Const"" foreground=""#4FC1FF"" fontWeight=""bold"" />
    <RuleSet>
        <Span color=""Comment""><Begin>//</Begin><End>$</End></Span>
        <Span color=""Comment""><Begin>/\*</Begin><End>\*/</End></Span>
        <Span color=""Comment""><Begin>\#</Begin><End>$</End></Span>
        <Span color=""String""><Begin>""</Begin><End>""</End></Span>
        <Span color=""String""><Begin>'</Begin><End>'</End></Span>
        <Span color=""String""><Begin>&lt;&lt;&lt;['&quot;]?\w+</Begin><End>^\w+;?\s*$</End></Span>
        <Rule color=""Tag"">&lt;\?php|\?&gt;</Rule>
        <Rule color=""Keyword"">\b(abstract|and|array|as|break|callable|case|catch|class|clone|const|continue|declare|default|die|do|echo|else|elseif|empty|enddeclare|endfor|endforeach|endif|endswitch|endwhile|enum|eval|exit|extends|final|finally|fn|for|foreach|function|global|goto|if|implements|include|include_once|instanceof|insteadof|interface|isset|list|match|namespace|new|or|print|private|protected|public|readonly|require|require_once|return|static|switch|throw|trait|try|unset|use|var|while|xor|yield|__CLASS__|__DIR__|__FILE__|__FUNCTION__|__LINE__|__METHOD__|__NAMESPACE__|__TRAIT__)\b</Rule>
        <Rule color=""Const"">\b(true|false|null)\b</Rule>
        <Rule color=""Type"">\b(bool|boolean|int|integer|float|double|string|array|object|mixed|void|never|iterable|self|parent)\b</Rule>
        <Rule color=""Variable"">\$[A-Za-z_]\w*</Rule>
        <Rule color=""Number"">\b\d+(\.\d+)?\b</Rule>
        <Rule color=""Function"">\b[A-Za-z_]\w*(?=\s*\()</Rule>
    </RuleSet>
</SyntaxDefinition>";
}
