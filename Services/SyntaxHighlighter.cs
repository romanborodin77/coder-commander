using System.Drawing;
using System.Text.RegularExpressions;

namespace CoderCommander.Services;

/// <summary>
/// Token types for syntax highlighting.
/// </summary>
public enum TokenType
{
    Plain,
    Keyword,
    String,
    Comment,
    Number,
    Operator,
    Type,
    Function,
    Preprocessor,
    Attribute,
    Tag,
    TagName,
    TagAttribute,
    PropertyValue,
    Selector,
    JsonKey,
    JsonValue,
    SqlKeyword,
    SqlFunction,
    MarkdownHeader,
    MarkdownBold,
    MarkdownItalic,
    MarkdownCode,
    MarkdownLink,
    MarkdownList
}

/// <summary>
/// Represents a token with position and type.
/// </summary>
public sealed class SyntaxToken
{
    public int Start { get; }
    public int Length { get; }
    public TokenType Type { get; }
    public string Text { get; }

    public SyntaxToken(int start, int length, TokenType type, string text)
    {
        Start = start;
        Length = length;
        Type = type;
        Text = text;
    }
}

/// <summary>
/// Supported programming languages.
/// </summary>
public enum LanguageId
{
    PlainText,
    CSharp,
    C,
    Cpp,
    Java,
    JavaScript,
    TypeScript,
    Python,
    Html,
    Xml,
    Css,
    Json,
    Sql,
    Markdown,
    Php,
    Ruby,
    Go,
    Rust,
    Swift,
    Kotlin,
    Shell,
    PowerShell,
    Yaml,
    Ini,
    Dockerfile,
    Makefile
}

/// <summary>
/// Detects language from file extension.
/// </summary>
public static class LanguageDetector
{
    public static LanguageId Detect(string? filePath)
    {
        if (string.IsNullOrEmpty(filePath)) return LanguageId.PlainText;

        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        var fileName = Path.GetFileName(filePath).ToLowerInvariant();

        if (fileName is "dockerfile" || fileName.StartsWith("dockerfile.", StringComparison.Ordinal))
            return LanguageId.Dockerfile;
        if (fileName is "makefile" or "gnumakefile")
            return LanguageId.Makefile;

        return ext switch
        {
            ".cs" => LanguageId.CSharp,
            ".c" => LanguageId.C,
            ".cpp" or ".cc" or ".cxx" or ".h" or ".hpp" => LanguageId.Cpp,
            ".java" => LanguageId.Java,
            ".js" or ".mjs" or ".cjs" => LanguageId.JavaScript,
            ".ts" or ".tsx" => LanguageId.TypeScript,
            ".jsx" => LanguageId.JavaScript,
            ".py" or ".pyw" => LanguageId.Python,
            ".html" or ".htm" => LanguageId.Html,
            ".xml" or ".xsl" or ".xslt" or ".svg" => LanguageId.Xml,
            ".css" or ".scss" or ".less" => LanguageId.Css,
            ".json" => LanguageId.Json,
            ".sql" => LanguageId.Sql,
            ".md" or ".markdown" => LanguageId.Markdown,
            ".php" => LanguageId.Php,
            ".rb" => LanguageId.Ruby,
            ".go" => LanguageId.Go,
            ".rs" => LanguageId.Rust,
            ".swift" => LanguageId.Swift,
            ".kt" or ".kts" => LanguageId.Kotlin,
            ".sh" or ".bash" or ".zsh" => LanguageId.Shell,
            ".ps1" or ".psm1" or ".psd1" => LanguageId.PowerShell,
            ".yaml" or ".yml" => LanguageId.Yaml,
            ".ini" or ".cfg" or ".conf" => LanguageId.Ini,
            ".dockerfile" => LanguageId.Dockerfile,
            ".mak" or ".mk" => LanguageId.Makefile,
            _ => LanguageId.PlainText
        };
    }

    public static string GetDisplayName(LanguageId language) => language switch
    {
        LanguageId.PlainText => Services.LocalizationService.Current.GetString("Lang.PlainText"),
        LanguageId.CSharp => "C#",
        LanguageId.C => "C",
        LanguageId.Cpp => "C++",
        LanguageId.Java => "Java",
        LanguageId.JavaScript => "JavaScript",
        LanguageId.TypeScript => "TypeScript",
        LanguageId.Python => "Python",
        LanguageId.Html => "HTML",
        LanguageId.Xml => "XML",
        LanguageId.Css => "CSS",
        LanguageId.Json => "JSON",
        LanguageId.Sql => "SQL",
        LanguageId.Markdown => "Markdown",
        LanguageId.Php => "PHP",
        LanguageId.Ruby => "Ruby",
        LanguageId.Go => "Go",
        LanguageId.Rust => "Rust",
        LanguageId.Swift => "Swift",
        LanguageId.Kotlin => "Kotlin",
        LanguageId.Shell => "Shell",
        LanguageId.PowerShell => "PowerShell",
        LanguageId.Yaml => "YAML",
        LanguageId.Ini => "INI",
        LanguageId.Dockerfile => "Dockerfile",
        LanguageId.Makefile => "Makefile",
        _ => "Unknown"
    };
}

/// <summary>
/// Tokenizes source code into syntax tokens for highlighting.
/// </summary>
public static class SyntaxHighlighter
{
    /// <summary>
    /// Tokenize text based on language.
    /// </summary>
    public static List<SyntaxToken> Tokenize(string text, LanguageId language)
    {
        return language switch
        {
            LanguageId.CSharp or LanguageId.C or LanguageId.Cpp or LanguageId.Java or
            LanguageId.JavaScript or LanguageId.TypeScript or LanguageId.Go or
            LanguageId.Rust or LanguageId.Swift or LanguageId.Kotlin =>
                TokenizeCLike(text, language),

            LanguageId.Python => TokenizePython(text),
            LanguageId.Html or LanguageId.Xml => TokenizeHtmlXml(text),
            LanguageId.Css => TokenizeCss(text),
            LanguageId.Json => TokenizeJson(text),
            LanguageId.Sql => TokenizeSql(text),
            LanguageId.Markdown => TokenizeMarkdown(text),
            LanguageId.Php => TokenizePhp(text),
            LanguageId.Ruby => TokenizeRuby(text),
            LanguageId.Shell or LanguageId.PowerShell => TokenizeShell(text),
            LanguageId.Yaml => TokenizeYaml(text),
            LanguageId.Ini => TokenizeIni(text),
            LanguageId.Dockerfile => TokenizeDockerfile(text),
            LanguageId.Makefile => TokenizeMakefile(text),
            _ => new List<SyntaxToken> { new(0, text.Length, TokenType.Plain, text) }
        };
    }

    private static List<SyntaxToken> TokenizeCLike(string text, LanguageId language)
    {
        var tokens = new List<SyntaxToken>();
        var keywords = GetCLikeKeywords(language);
        var types = GetCLikeTypes(language);

        var i = 0;
        while (i < text.Length)
        {
            // Line comment
            if (i + 1 < text.Length && text[i] == '/' && text[i + 1] == '/')
            {
                var start = i;
                while (i < text.Length && text[i] != '\n') i++;
                tokens.Add(new SyntaxToken(start, i - start, TokenType.Comment, text[start..i]));
                continue;
            }

            // Block comment
            if (i + 1 < text.Length && text[i] == '/' && text[i + 1] == '*')
            {
                var start = i;
                i += 2;
                while (i + 1 < text.Length && !(text[i] == '*' && text[i + 1] == '/')) i++;
                if (i + 1 < text.Length) i += 2;
                tokens.Add(new SyntaxToken(start, i - start, TokenType.Comment, text[start..i]));
                continue;
            }

            // String
            if (text[i] == '"' || text[i] == '\'')
            {
                var start = i;
                var quote = text[i];
                i++;
                while (i < text.Length && text[i] != quote)
                {
                    if (text[i] == '\\') i++;
                    i++;
                }
                if (i < text.Length) i++;
                tokens.Add(new SyntaxToken(start, i - start, TokenType.String, text[start..i]));
                continue;
            }

            // Preprocessor
            if (text[i] == '#')
            {
                var start = i;
                while (i < text.Length && text[i] != '\n') i++;
                tokens.Add(new SyntaxToken(start, i - start, TokenType.Preprocessor, text[start..i]));
                continue;
            }

            // Number
            if (char.IsDigit(text[i]) || (text[i] == '.' && i + 1 < text.Length && char.IsDigit(text[i + 1])))
            {
                var start = i;
                while (i < text.Length && (char.IsDigit(text[i]) || text[i] == '.' || text[i] == 'x' || text[i] == 'X' ||
                       "abcdefABCDEF".Contains(text[i]))) i++;
                tokens.Add(new SyntaxToken(start, i - start, TokenType.Number, text[start..i]));
                continue;
            }

            // Identifier or keyword
            if (char.IsLetter(text[i]) || text[i] == '_')
            {
                var start = i;
                while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '_')) i++;
                var word = text[start..i];

                if (keywords.Contains(word))
                    tokens.Add(new SyntaxToken(start, word.Length, TokenType.Keyword, word));
                else if (types.Contains(word))
                    tokens.Add(new SyntaxToken(start, word.Length, TokenType.Type, word));
                else if (i < text.Length && text[i] == '(')
                    tokens.Add(new SyntaxToken(start, word.Length, TokenType.Function, word));
                else
                    tokens.Add(new SyntaxToken(start, word.Length, TokenType.Plain, word));
                continue;
            }

            // Operator - combine consecutive operators
            if ("+-*/%=<>!&|^~.,;:()[]{}?".Contains(text[i]))
            {
                var start = i;
                while (i < text.Length && "+-*/%=<>!&|^~.,;:()[]{}?".Contains(text[i]))
                    i++;
                tokens.Add(new SyntaxToken(start, i - start, TokenType.Operator, text[start..i]));
                continue;
            }

            // Whitespace or other
            tokens.Add(new SyntaxToken(i, 1, TokenType.Plain, text[i].ToString()));
            i++;
        }

        return tokens;
    }

    private static HashSet<string> GetCLikeKeywords(LanguageId language) => language switch
    {
        LanguageId.CSharp => ["abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked",
            "class", "const", "continue", "decimal", "default", "delegate", "do", "double", "else", "enum", "event",
            "explicit", "extern", "false", "finally", "fixed", "float", "for", "foreach", "goto", "if", "implicit",
            "in", "int", "interface", "internal", "is", "lock", "long", "namespace", "new", "null", "object",
            "operator", "out", "override", "params", "private", "protected", "public", "readonly", "ref", "return",
            "sbyte", "sealed", "short", "sizeof", "stackalloc", "static", "string", "struct", "switch", "this",
            "throw", "true", "try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort", "using", "var",
            "virtual", "void", "volatile", "while", "yield", "async", "await", "dynamic", "nameof", "when", "where"],

        LanguageId.JavaScript or LanguageId.TypeScript => ["abstract", "arguments", "await", "boolean", "break", "byte",
            "case", "catch", "char", "class", "const", "continue", "debugger", "default", "delete", "do", "double",
            "else", "enum", "export", "extends", "false", "final", "finally", "float", "for", "function", "goto",
            "if", "implements", "import", "in", "instanceof", "int", "interface", "let", "long", "native", "new",
            "null", "package", "private", "protected", "public", "return", "short", "static", "super", "switch",
            "synchronized", "this", "throw", "throws", "transient", "true", "try", "typeof", "undefined", "var",
            "void", "volatile", "while", "with", "yield", "async", "await", "from", "of", "type", "keyof", "readonly"],

        LanguageId.C => ["auto", "break", "case", "char", "const", "continue", "default", "do", "double", "else",
            "enum", "extern", "float", "for", "goto", "if", "inline", "int", "long", "register", "restrict",
            "return", "short", "signed", "sizeof", "static", "struct", "switch", "typedef", "union", "unsigned",
            "void", "volatile", "while", "_Bool", "_Complex", "_Imaginary"],

        LanguageId.Cpp => ["alignas", "alignof", "and", "and_eq", "asm", "auto", "bitand", "bitor", "bool", "break",
            "case", "catch", "char", "char16_t", "char32_t", "class", "compl", "const", "constexpr", "const_cast",
            "continue", "decltype", "default", "delete", "do", "double", "dynamic_cast", "else", "enum", "explicit",
            "export", "extern", "false", "float", "for", "friend", "goto", "if", "inline", "int", "long", "mutable",
            "namespace", "new", "noexcept", "not", "not_eq", "nullptr", "operator", "or", "or_eq", "private",
            "protected", "public", "register", "reinterpret_cast", "return", "short", "signed", "sizeof", "static",
            "static_assert", "static_cast", "struct", "switch", "template", "this", "throw", "true", "try", "typedef",
            "typeid", "typename", "union", "unsigned", "using", "virtual", "void", "volatile", "wchar_t", "while",
            "xor", "xor_eq"],

        LanguageId.Java => ["abstract", "assert", "boolean", "break", "byte", "case", "catch", "char", "class",
            "const", "continue", "default", "do", "double", "else", "enum", "extends", "final", "finally", "float",
            "for", "goto", "if", "implements", "import", "instanceof", "int", "interface", "long", "native", "new",
            "package", "private", "protected", "public", "return", "short", "static", "strictfp", "super", "switch",
            "synchronized", "this", "throw", "throws", "transient", "try", "void", "volatile", "while"],

        LanguageId.Go => ["break", "case", "chan", "const", "continue", "default", "defer", "else", "fallthrough",
            "for", "func", "go", "goto", "if", "import", "interface", "map", "package", "range", "return", "select",
            "struct", "switch", "type", "var"],

        LanguageId.Rust => ["as", "async", "await", "break", "const", "continue", "crate", "dyn", "else", "enum",
            "extern", "false", "fn", "for", "if", "impl", "in", "let", "loop", "match", "mod", "move", "mut", "pub",
            "ref", "return", "self", "Self", "static", "struct", "super", "trait", "true", "type", "unsafe", "use",
            "where", "while"],

        LanguageId.Swift => ["associatedtype", "class", "deinit", "enum", "extension", "fileprivate", "func", "import",
            "init", "inout", "internal", "let", "open", "operator", "private", "protocol", "public", "rethrows",
            "static", "struct", "subscript", "typealias", "var", "break", "case", "continue", "default", "defer",
            "do", "else", "fallthrough", "for", "guard", "if", "in", "repeat", "return", "switch", "where", "while",
            "as", "Any", "catch", "false", "is", "nil", "super", "self", "Self", "throw", "throws", "true", "try"],

        LanguageId.Kotlin => ["as", "break", "class", "continue", "do", "else", "false", "for", "fun", "if", "in",
            "interface", "is", "null", "object", "package", "return", "super", "this", "throw", "true", "try", "typealias",
            "typeof", "val", "var", "when", "while"],

        _ => new HashSet<string>()
    };

    private static HashSet<string> GetCLikeTypes(LanguageId language) => language switch
    {
        LanguageId.CSharp => ["bool", "byte", "char", "decimal", "double", "float", "int", "long", "object", "sbyte",
            "short", "string", "uint", "ulong", "ushort", "void", "dynamic", "var"],

        LanguageId.JavaScript or LanguageId.TypeScript => ["Array", "Boolean", "Date", "Error", "Function", "JSON",
            "Math", "Number", "Object", "Promise", "RegExp", "String", "Symbol", "undefined", "null", "any", "void",
            "never", "unknown", "bigint"],

        LanguageId.C => ["char", "double", "float", "int", "long", "short", "signed", "unsigned", "void", "_Bool",
            "_Complex", "_Imaginary", "size_t", "ptrdiff_t"],

        LanguageId.Cpp => ["bool", "char", "char16_t", "char32_t", "double", "float", "int", "long", "short",
            "signed", "unsigned", "void", "wchar_t", "auto", "decltype"],

        LanguageId.Java => ["boolean", "byte", "char", "double", "float", "int", "long", "short", "void"],

        LanguageId.Go => ["bool", "byte", "complex64", "complex128", "error", "float32", "float64", "int", "int8",
            "int16", "int32", "int64", "rune", "string", "uint", "uint8", "uint16", "uint32", "uint64", "uintptr"],

        LanguageId.Rust => ["bool", "char", "f32", "f64", "i8", "i16", "i32", "i64", "i128", "isize", "str", "u8",
            "u16", "u32", "u64", "u128", "usize", "Self"],

        LanguageId.Swift => ["Bool", "Character", "Double", "Float", "Int", "Int8", "Int16", "Int32", "Int64",
            "String", "UInt", "UInt8", "UInt16", "UInt32", "UInt64"],

        LanguageId.Kotlin => ["Boolean", "Byte", "Char", "Double", "Float", "Int", "Long", "Short", "String", "Unit", "Nothing", "Any"],

        _ => new HashSet<string>()
    };

    private static List<SyntaxToken> TokenizePython(string text)
    {
        var tokens = new List<SyntaxToken>();
        var keywords = new HashSet<string> { "False", "None", "True", "and", "as", "assert", "async", "await",
            "break", "class", "continue", "def", "del", "elif", "else", "except", "finally", "for", "from",
            "global", "if", "import", "in", "is", "lambda", "nonlocal", "not", "or", "pass", "raise", "return",
            "try", "while", "with", "yield" };
        var types = new HashSet<string> { "int", "float", "str", "bool", "list", "dict", "tuple", "set", "bytes",
            "bytearray", "memoryview", "complex", "range", "slice", "object", "type", "NoneType" };

        var i = 0;
        while (i < text.Length)
        {
            // Comment
            if (text[i] == '#')
            {
                var start = i;
                while (i < text.Length && text[i] != '\n') i++;
                tokens.Add(new SyntaxToken(start, i - start, TokenType.Comment, text[start..i]));
                continue;
            }

            // Triple-quoted string
            if (i + 2 < text.Length && text[i] == '"' && text[i + 1] == '"' && text[i + 2] == '"')
            {
                var start = i;
                i += 3;
                while (i + 2 < text.Length && !(text[i] == '"' && text[i + 1] == '"' && text[i + 2] == '"')) i++;
                if (i + 2 < text.Length) i += 3;
                tokens.Add(new SyntaxToken(start, i - start, TokenType.String, text[start..i]));
                continue;
            }

            // String
            if (text[i] == '"' || text[i] == '\'')
            {
                var start = i;
                var quote = text[i];
                i++;
                while (i < text.Length && text[i] != quote)
                {
                    if (text[i] == '\\') i++;
                    i++;
                }
                if (i < text.Length) i++;
                tokens.Add(new SyntaxToken(start, i - start, TokenType.String, text[start..i]));
                continue;
            }

            // Number
            if (char.IsDigit(text[i]))
            {
                var start = i;
                while (i < text.Length && (char.IsDigit(text[i]) || text[i] == '.' || text[i] == 'x' ||
                       text[i] == 'X' || text[i] == 'o' || text[i] == 'O' || text[i] == 'b' || text[i] == 'B' ||
                       "abcdefABCDEF".Contains(text[i]))) i++;
                tokens.Add(new SyntaxToken(start, i - start, TokenType.Number, text[start..i]));
                continue;
            }

            // Decorator
            if (text[i] == '@')
            {
                var start = i;
                i++;
                while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '_' || text[i] == '.')) i++;
                tokens.Add(new SyntaxToken(start, i - start, TokenType.Attribute, text[start..i]));
                continue;
            }

            // Identifier
            if (char.IsLetter(text[i]) || text[i] == '_')
            {
                var start = i;
                while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '_')) i++;
                var word = text[start..i];

                if (keywords.Contains(word))
                    tokens.Add(new SyntaxToken(start, word.Length, TokenType.Keyword, word));
                else if (types.Contains(word))
                    tokens.Add(new SyntaxToken(start, word.Length, TokenType.Type, word));
                else if (i < text.Length && text[i] == '(')
                    tokens.Add(new SyntaxToken(start, word.Length, TokenType.Function, word));
                else
                    tokens.Add(new SyntaxToken(start, word.Length, TokenType.Plain, word));
                continue;
            }

            tokens.Add(new SyntaxToken(i, 1, TokenType.Plain, text[i].ToString()));
            i++;
        }

        return tokens;
    }

    private static List<SyntaxToken> TokenizeHtmlXml(string text)
    {
        var tokens = new List<SyntaxToken>();
        var i = 0;

        while (i < text.Length)
        {
            // Comment
            if (i + 3 < text.Length && text.AsSpan(i, 4).SequenceEqual("<!--"))
            {
                var start = i;
                while (i + 2 < text.Length && !text.AsSpan(i, 3).SequenceEqual("-->")) i++;
                if (i + 2 < text.Length) i += 3;
                tokens.Add(new SyntaxToken(start, i - start, TokenType.Comment, text[start..i]));
                continue;
            }

            // Tag
            if (text[i] == '<')
            {
                var start = i;
                i++;

                // Closing tag
                if (i < text.Length && text[i] == '/')
                {
                    tokens.Add(new SyntaxToken(i, 1, TokenType.Operator, "/"));
                    i++;
                }

                // Tag name
                var nameStart = i;
                while (i < text.Length && !char.IsWhiteSpace(text[i]) && text[i] != '>' && text[i] != '/') i++;
                if (i > nameStart)
                    tokens.Add(new SyntaxToken(nameStart, i - nameStart, TokenType.TagName, text[nameStart..i]));

                // Attributes
                while (i < text.Length && text[i] != '>')
                {
                    if (char.IsWhiteSpace(text[i]))
                    {
                        tokens.Add(new SyntaxToken(i, 1, TokenType.Plain, " "));
                        i++;
                        continue;
                    }

                    if (text[i] == '/')
                    {
                        tokens.Add(new SyntaxToken(i, 1, TokenType.Operator, "/"));
                        i++;
                        continue;
                    }

                    // Attribute name
                    var attrStart = i;
                    while (i < text.Length && text[i] != '=' && !char.IsWhiteSpace(text[i]) && text[i] != '>') i++;
                    if (i > attrStart)
                        tokens.Add(new SyntaxToken(attrStart, i - attrStart, TokenType.TagAttribute, text[attrStart..i]));

                    if (i < text.Length && text[i] == '=')
                    {
                        tokens.Add(new SyntaxToken(i, 1, TokenType.Operator, "="));
                        i++;

                        // Attribute value
                        if (i < text.Length && (text[i] == '"' || text[i] == '\''))
                        {
                            var valStart = i;
                            var quote = text[i];
                            i++;
                            while (i < text.Length && text[i] != quote) i++;
                            if (i < text.Length) i++;
                            tokens.Add(new SyntaxToken(valStart, i - valStart, TokenType.String, text[valStart..i]));
                        }
                    }
                }

                if (i < text.Length && text[i] == '>')
                {
                    tokens.Add(new SyntaxToken(i, 1, TokenType.Operator, ">"));
                    i++;
                }

                continue;
            }

            // Plain text
            var textStart = i;
            while (i < text.Length && text[i] != '<') i++;
            if (i > textStart)
                tokens.Add(new SyntaxToken(textStart, i - textStart, TokenType.Plain, text[textStart..i]));
        }

        return tokens;
    }

    private static List<SyntaxToken> TokenizeCss(string text)
    {
        var tokens = new List<SyntaxToken>();
        var i = 0;

        while (i < text.Length)
        {
            // Comment
            if (i + 1 < text.Length && text[i] == '/' && text[i + 1] == '*')
            {
                var start = i;
                i += 2;
                while (i + 1 < text.Length && !(text[i] == '*' && text[i + 1] == '/')) i++;
                if (i + 1 < text.Length) i += 2;
                tokens.Add(new SyntaxToken(start, i - start, TokenType.Comment, text[start..i]));
                continue;
            }

            // String
            if (text[i] == '"' || text[i] == '\'')
            {
                var start = i;
                var quote = text[i];
                i++;
                while (i < text.Length && text[i] != quote) i++;
                if (i < text.Length) i++;
                tokens.Add(new SyntaxToken(start, i - start, TokenType.String, text[start..i]));
                continue;
            }

            // Selector
            if (char.IsLetter(text[i]) || text[i] == '.' || text[i] == '#' || text[i] == '[' || text[i] == ':')
            {
                var start = i;
                while (i < text.Length && text[i] != '{' && text[i] != '}') i++;
                tokens.Add(new SyntaxToken(start, i - start, TokenType.Selector, text[start..i]));
                continue;
            }

            // Property name
            if (i > 0 && text[i - 1] == '{' || (i > 1 && text[i - 1] == ';' && char.IsWhiteSpace(text[i - 2])))
            {
                var start = i;
                while (i < text.Length && text[i] != ':' && text[i] != '}') i++;
                if (i > start)
                    tokens.Add(new SyntaxToken(start, i - start, TokenType.TagAttribute, text[start..i]));
                continue;
            }

            // Property value
            if (i > 0 && text[i - 1] == ':')
            {
                var start = i;
                while (i < text.Length && text[i] != ';' && text[i] != '}') i++;
                if (i > start)
                    tokens.Add(new SyntaxToken(start, i - start, TokenType.PropertyValue, text[start..i]));
                continue;
            }

            // Number
            if (char.IsDigit(text[i]))
            {
                var start = i;
                while (i < text.Length && (char.IsDigit(text[i]) || text[i] == '.' || text[i] == '%' ||
                       text[i] == 'p' || text[i] == 'x' || text[i] == 'e' || text[i] == 'm' || text[i] == 'r')) i++;
                tokens.Add(new SyntaxToken(start, i - start, TokenType.Number, text[start..i]));
                continue;
            }

            tokens.Add(new SyntaxToken(i, 1, TokenType.Plain, text[i].ToString()));
            i++;
        }

        return tokens;
    }

    private static List<SyntaxToken> TokenizeJson(string text)
    {
        var tokens = new List<SyntaxToken>();
        var i = 0;

        while (i < text.Length)
        {
            // Skip whitespace
            if (char.IsWhiteSpace(text[i]))
            {
                tokens.Add(new SyntaxToken(i, 1, TokenType.Plain, " "));
                i++;
                continue;
            }

            // String (key or value)
            if (text[i] == '"')
            {
                var start = i;
                i++;
                while (i < text.Length && text[i] != '"')
                {
                    if (text[i] == '\\') i++;
                    i++;
                }
                if (i < text.Length) i++;
                var str = text[start..i];

                // Check if it's a key (followed by colon)
                var j = i;
                while (j < text.Length && char.IsWhiteSpace(text[j])) j++;
                if (j < text.Length && text[j] == ':')
                    tokens.Add(new SyntaxToken(start, i - start, TokenType.JsonKey, str));
                else
                    tokens.Add(new SyntaxToken(start, i - start, TokenType.JsonValue, str));
                continue;
            }

            // Number
            if (char.IsDigit(text[i]) || text[i] == '-')
            {
                var start = i;
                if (text[i] == '-') i++;
                while (i < text.Length && (char.IsDigit(text[i]) || text[i] == '.' || text[i] == 'e' ||
                       text[i] == 'E' || text[i] == '+' || text[i] == '-')) i++;
                tokens.Add(new SyntaxToken(start, i - start, TokenType.Number, text[start..i]));
                continue;
            }

            // Keywords
            if (i + 4 <= text.Length && text.AsSpan(i, 4).SequenceEqual("true"))
            {
                tokens.Add(new SyntaxToken(i, 4, TokenType.Keyword, "true"));
                i += 4;
                continue;
            }
            if (i + 5 <= text.Length && text.AsSpan(i, 5).SequenceEqual("false"))
            {
                tokens.Add(new SyntaxToken(i, 5, TokenType.Keyword, "false"));
                i += 5;
                continue;
            }
            if (i + 4 <= text.Length && text.AsSpan(i, 4).SequenceEqual("null"))
            {
                tokens.Add(new SyntaxToken(i, 4, TokenType.Keyword, "null"));
                i += 4;
                continue;
            }

            // Operators
            if ("{}[],:".Contains(text[i]))
            {
                tokens.Add(new SyntaxToken(i, 1, TokenType.Operator, text[i].ToString()));
                i++;
                continue;
            }

            tokens.Add(new SyntaxToken(i, 1, TokenType.Plain, text[i].ToString()));
            i++;
        }

        return tokens;
    }

    private static List<SyntaxToken> TokenizeSql(string text)
    {
        var tokens = new List<SyntaxToken>();
        var keywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            "SELECT", "FROM", "WHERE", "INSERT", "UPDATE", "DELETE", "CREATE", "DROP", "ALTER", "TABLE",
            "INDEX", "VIEW", "INTO", "VALUES", "SET", "JOIN", "LEFT", "RIGHT", "INNER", "OUTER", "ON",
            "AND", "OR", "NOT", "IN", "BETWEEN", "LIKE", "IS", "NULL", "AS", "ORDER", "BY", "GROUP",
            "HAVING", "LIMIT", "OFFSET", "UNION", "ALL", "DISTINCT", "COUNT", "SUM", "AVG", "MIN", "MAX",
            "CASE", "WHEN", "THEN", "ELSE", "END", "IF", "EXISTS", "PRIMARY", "KEY", "FOREIGN", "REFERENCES",
            "CONSTRAINT", "DEFAULT", "CHECK", "UNIQUE", "ASC", "DESC", "GRANT", "REVOKE", "BEGIN", "COMMIT",
            "ROLLBACK", "TRANSACTION", "DECLARE", "CURSOR", "FETCH", "OPEN", "CLOSE", "EXECUTE", "PROCEDURE",
            "FUNCTION", "RETURN", "RETURNS", "TRIGGER", "DATABASE", "SCHEMA", "USE", "SHOW", "DESCRIBE",
            "EXPLAIN", "ANALYZE", "VACUUM", "INDEX", "CLUSTER", "COPY", "TRUNCATE"
        };

        var i = 0;
        while (i < text.Length)
        {
            // Line comment
            if (i + 1 < text.Length && text[i] == '-' && text[i + 1] == '-')
            {
                var start = i;
                while (i < text.Length && text[i] != '\n') i++;
                tokens.Add(new SyntaxToken(start, i - start, TokenType.Comment, text[start..i]));
                continue;
            }

            // Block comment
            if (i + 1 < text.Length && text[i] == '/' && text[i + 1] == '*')
            {
                var start = i;
                i += 2;
                while (i + 1 < text.Length && !(text[i] == '*' && text[i + 1] == '/')) i++;
                if (i + 1 < text.Length) i += 2;
                tokens.Add(new SyntaxToken(start, i - start, TokenType.Comment, text[start..i]));
                continue;
            }

            // String
            if (text[i] == '\'' || text[i] == '"')
            {
                var start = i;
                var quote = text[i];
                i++;
                while (i < text.Length && text[i] != quote) i++;
                if (i < text.Length) i++;
                tokens.Add(new SyntaxToken(start, i - start, TokenType.String, text[start..i]));
                continue;
            }

            // Number
            if (char.IsDigit(text[i]))
            {
                var start = i;
                while (i < text.Length && (char.IsDigit(text[i]) || text[i] == '.')) i++;
                tokens.Add(new SyntaxToken(start, i - start, TokenType.Number, text[start..i]));
                continue;
            }

            // Identifier or keyword
            if (char.IsLetter(text[i]) || text[i] == '_')
            {
                var start = i;
                while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '_')) i++;
                var word = text[start..i];

                if (keywords.Contains(word))
                    tokens.Add(new SyntaxToken(start, word.Length, TokenType.SqlKeyword, word));
                else if (i < text.Length && text[i] == '(')
                    tokens.Add(new SyntaxToken(start, word.Length, TokenType.SqlFunction, word));
                else
                    tokens.Add(new SyntaxToken(start, word.Length, TokenType.Plain, word));
                continue;
            }

            tokens.Add(new SyntaxToken(i, 1, TokenType.Plain, text[i].ToString()));
            i++;
        }

        return tokens;
    }

    /// <summary>Line-length cap for the O(n^2) inline bold/italic/code/link scan in
    /// <see cref="TokenizeMarkdown"/> - see the comment at its call site.</summary>
    private const int MaxInlineScanLineLength = 2000;

    private static List<SyntaxToken> TokenizeMarkdown(string text)
    {
        var tokens = new List<SyntaxToken>();
        var lines = text.Split('\n');
        var pos = 0;

        foreach (var line in lines)
        {
            // Header
            if (line.StartsWith('#'))
            {
                var level = 0;
                while (level < line.Length && line[level] == '#') level++;
                tokens.Add(new SyntaxToken(pos, level, TokenType.MarkdownHeader, line[..level]));
                tokens.Add(new SyntaxToken(pos + level, line.Length - level, TokenType.Plain, line[level..]));
                pos += line.Length + 1;
                continue;
            }

            // List item
            if (line.StartsWith("- ") || line.StartsWith("* ") || line.StartsWith("+ ") ||
                Regex.IsMatch(line, @"^\d+\."))
            {
                var match = Regex.Match(line, @"^(\s*[-*+]|\s*\d+\.)");
                tokens.Add(new SyntaxToken(pos, match.Length, TokenType.MarkdownList, match.Value));
                tokens.Add(new SyntaxToken(pos + match.Length, line.Length - match.Length, TokenType.Plain, line[match.Length..]));
                pos += line.Length + 1;
                continue;
            }

            // Code block
            if (line.StartsWith("```"))
            {
                tokens.Add(new SyntaxToken(pos, line.Length, TokenType.MarkdownCode, line));
                pos += line.Length + 1;
                continue;
            }

            // Bold and italic. The scan below re-runs 4 regexes over the full remaining suffix
            // on every match found, giving O(n^2) time for a line with many short matches - a
            // single line near SyncTokenizeCharThreshold built from a repeating short pattern
            // (e.g. "*a*a*a*..." ) would freeze the UI thread, since CodeEditorCanvas tokenizes
            // synchronously there. Cap the line length this scan runs on; longer lines (well
            // beyond any real Markdown prose line) fall through to a single Plain token instead.
            if (line.Length > MaxInlineScanLineLength)
            {
                tokens.Add(new SyntaxToken(pos, line.Length, TokenType.Plain, line));
                pos += line.Length + 1;
                continue;
            }

            var remaining = line;
            var linePos = pos;
            while (remaining.Length > 0)
            {
                // Bold
                var boldMatch = Regex.Match(remaining, @"\*\*(.+?)\*\*|__(.+?)__");
                // Italic
                var italicMatch = Regex.Match(remaining, @"\*(.+?)\*|_(.+?)_");
                // Code
                var codeMatch = Regex.Match(remaining, "`(.+?)`");
                // Link
                var linkMatch = Regex.Match(remaining, @"\[(.+?)\]\((.+?)\)");

                int earliest = int.MaxValue;
                TokenType earliestType = TokenType.Plain;
                int earliestLen = 0;

                if (boldMatch.Success && boldMatch.Index < earliest)
                {
                    earliest = boldMatch.Index;
                    earliestType = TokenType.MarkdownBold;
                    earliestLen = boldMatch.Length;
                }
                if (italicMatch.Success && italicMatch.Index < earliest)
                {
                    earliest = italicMatch.Index;
                    earliestType = TokenType.MarkdownItalic;
                    earliestLen = italicMatch.Length;
                }
                if (codeMatch.Success && codeMatch.Index < earliest)
                {
                    earliest = codeMatch.Index;
                    earliestType = TokenType.MarkdownCode;
                    earliestLen = codeMatch.Length;
                }
                if (linkMatch.Success && linkMatch.Index < earliest)
                {
                    earliest = linkMatch.Index;
                    earliestType = TokenType.MarkdownLink;
                    earliestLen = linkMatch.Length;
                }

                if (earliest == int.MaxValue)
                {
                    tokens.Add(new SyntaxToken(linePos, remaining.Length, TokenType.Plain, remaining));
                    break;
                }

                if (earliest > 0)
                    tokens.Add(new SyntaxToken(linePos, earliest, TokenType.Plain, remaining[..earliest]));

                tokens.Add(new SyntaxToken(linePos + earliest, earliestLen, earliestType, remaining.Substring(earliest, earliestLen)));

                linePos += earliest + earliestLen;
                remaining = remaining[(earliest + earliestLen)..];
            }

            pos += line.Length + 1;
        }

        return tokens;
    }

    private static List<SyntaxToken> TokenizePhp(string text)
    {
        // PHP is similar to C-like but with $ variables and <? ?> tags
        var tokens = TokenizeCLike(text, LanguageId.CSharp);
        // Add PHP-specific handling for $variables
        var result = new List<SyntaxToken>();
        foreach (var token in tokens)
        {
            if (token.Type == TokenType.Plain && token.Text.StartsWith("$"))
            {
                result.Add(new SyntaxToken(token.Start, token.Length, TokenType.Attribute, token.Text));
            }
            else
            {
                result.Add(token);
            }
        }
        return result;
    }

    private static List<SyntaxToken> TokenizeRuby(string text)
    {
        var tokens = new List<SyntaxToken>();
        var keywords = new HashSet<string> { "begin", "class", "def", "do", "else", "elsif", "end", "ensure",
            "for", "if", "in", "module", "next", "redo", "rescue", "retry", "return", "then", "unless", "until",
            "when", "while", "yield", "break", "case", "and", "or", "not", "nil", "true", "false", "self",
            "super", "require", "include", "extend", "attr_accessor", "attr_reader", "attr_writer" };

        var i = 0;
        while (i < text.Length)
        {
            // Comment
            if (text[i] == '#')
            {
                var start = i;
                while (i < text.Length && text[i] != '\n') i++;
                tokens.Add(new SyntaxToken(start, i - start, TokenType.Comment, text[start..i]));
                continue;
            }

            // Symbol
            if (text[i] == ':')
            {
                var start = i;
                i++;
                while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '_')) i++;
                tokens.Add(new SyntaxToken(start, i - start, TokenType.Attribute, text[start..i]));
                continue;
            }

            // String
            if (text[i] == '"' || text[i] == '\'')
            {
                var start = i;
                var quote = text[i];
                i++;
                while (i < text.Length && text[i] != quote) i++;
                if (i < text.Length) i++;
                tokens.Add(new SyntaxToken(start, i - start, TokenType.String, text[start..i]));
                continue;
            }

            // Variable
            if (text[i] == '@' || text[i] == '$')
            {
                var start = i;
                i++;
                while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '_')) i++;
                tokens.Add(new SyntaxToken(start, i - start, TokenType.Attribute, text[start..i]));
                continue;
            }

            // Number
            if (char.IsDigit(text[i]))
            {
                var start = i;
                while (i < text.Length && char.IsDigit(text[i])) i++;
                tokens.Add(new SyntaxToken(start, i - start, TokenType.Number, text[start..i]));
                continue;
            }

            // Identifier
            if (char.IsLetter(text[i]) || text[i] == '_')
            {
                var start = i;
                while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '_')) i++;
                var word = text[start..i];

                if (keywords.Contains(word))
                    tokens.Add(new SyntaxToken(start, word.Length, TokenType.Keyword, word));
                else if (char.IsUpper(word[0]))
                    tokens.Add(new SyntaxToken(start, word.Length, TokenType.Type, word));
                else
                    tokens.Add(new SyntaxToken(start, word.Length, TokenType.Plain, word));
                continue;
            }

            tokens.Add(new SyntaxToken(i, 1, TokenType.Plain, text[i].ToString()));
            i++;
        }

        return tokens;
    }

    private static List<SyntaxToken> TokenizeShell(string text)
    {
        var tokens = new List<SyntaxToken>();
        var keywords = new HashSet<string> { "if", "then", "else", "elif", "fi", "for", "while", "do", "done",
            "case", "esac", "function", "return", "in", "select", "until" };

        var i = 0;
        while (i < text.Length)
        {
            // Comment
            if (text[i] == '#')
            {
                var start = i;
                while (i < text.Length && text[i] != '\n') i++;
                tokens.Add(new SyntaxToken(start, i - start, TokenType.Comment, text[start..i]));
                continue;
            }

            // String
            if (text[i] == '"' || text[i] == '\'')
            {
                var start = i;
                var quote = text[i];
                i++;
                while (i < text.Length && text[i] != quote) i++;
                if (i < text.Length) i++;
                tokens.Add(new SyntaxToken(start, i - start, TokenType.String, text[start..i]));
                continue;
            }

            // Variable
            if (text[i] == '$')
            {
                var start = i;
                i++;
                if (i < text.Length && text[i] == '{')
                {
                    i++;
                    while (i < text.Length && text[i] != '}') i++;
                    if (i < text.Length) i++;
                }
                else
                {
                    while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '_')) i++;
                }
                tokens.Add(new SyntaxToken(start, i - start, TokenType.Attribute, text[start..i]));
                continue;
            }

            // Command
            if (char.IsLetter(text[i]) || text[i] == '_' || text[i] == '.' || text[i] == '/')
            {
                var start = i;
                while (i < text.Length && !char.IsWhiteSpace(text[i]) && text[i] != '|' && text[i] != ';' &&
                       text[i] != '&' && text[i] != '>' && text[i] != '<') i++;
                var word = text[start..i];

                if (keywords.Contains(word))
                    tokens.Add(new SyntaxToken(start, word.Length, TokenType.Keyword, word));
                else
                    tokens.Add(new SyntaxToken(start, word.Length, TokenType.Function, word));
                continue;
            }

            // Pipe and operators
            if ("|;&><".Contains(text[i]))
            {
                tokens.Add(new SyntaxToken(i, 1, TokenType.Operator, text[i].ToString()));
                i++;
                continue;
            }

            tokens.Add(new SyntaxToken(i, 1, TokenType.Plain, text[i].ToString()));
            i++;
        }

        return tokens;
    }

    private static List<SyntaxToken> TokenizeYaml(string text)
    {
        var tokens = new List<SyntaxToken>();
        var lines = text.Split('\n');
        var pos = 0;

        foreach (var line in lines)
        {
            // Comment
            if (line.TrimStart().StartsWith('#'))
            {
                var start = pos + line.IndexOf('#');
                tokens.Add(new SyntaxToken(start, line.Length - line.IndexOf('#'), TokenType.Comment, line[line.IndexOf('#')..]));
                pos += line.Length + 1;
                continue;
            }

            // Key
            var colonIdx = line.IndexOf(':');
            if (colonIdx > 0)
            {
                var key = line[..colonIdx].TrimStart();
                var indent = line.Length - key.TrimStart().Length;
                tokens.Add(new SyntaxToken(pos, indent, TokenType.Plain, line[..indent]));
                tokens.Add(new SyntaxToken(pos + indent, key.Length, TokenType.JsonKey, key));
                tokens.Add(new SyntaxToken(pos + indent + key.Length, colonIdx - indent - key.Length + 1, TokenType.Operator, line[(indent + key.Length)..(colonIdx + 1)]));

                var value = line[(colonIdx + 1)..].TrimStart();
                if (value.Length > 0)
                {
                    var valueStart = pos + colonIdx + 1 + (line[(colonIdx + 1)..].Length - value.Length);
                    tokens.Add(new SyntaxToken(valueStart, value.Length, TokenType.JsonValue, value));
                }
            }
            else
            {
                tokens.Add(new SyntaxToken(pos, line.Length, TokenType.Plain, line));
            }

            pos += line.Length + 1;
        }

        return tokens;
    }

    private static List<SyntaxToken> TokenizeIni(string text)
    {
        var tokens = new List<SyntaxToken>();
        var lines = text.Split('\n');
        var pos = 0;

        foreach (var line in lines)
        {
            var trimmed = line.TrimStart();

            // Comment
            if (trimmed.StartsWith(';') || trimmed.StartsWith('#'))
            {
                tokens.Add(new SyntaxToken(pos, line.Length, TokenType.Comment, line));
                pos += line.Length + 1;
                continue;
            }

            // Section header
            if (trimmed.StartsWith('[') && trimmed.Contains(']'))
            {
                tokens.Add(new SyntaxToken(pos, line.Length, TokenType.Preprocessor, line));
                pos += line.Length + 1;
                continue;
            }

            // Key=Value
            var eqIdx = line.IndexOf('=');
            if (eqIdx > 0)
            {
                var key = line[..eqIdx].Trim();
                var value = line[(eqIdx + 1)..].Trim();
                var keyStart = pos + line.IndexOf(key);

                tokens.Add(new SyntaxToken(keyStart, key.Length, TokenType.JsonKey, key));
                tokens.Add(new SyntaxToken(pos + eqIdx, 1, TokenType.Operator, "="));

                var valueStart = pos + eqIdx + 1 + (line[(eqIdx + 1)..].Length - value.Length);
                tokens.Add(new SyntaxToken(valueStart, value.Length, TokenType.JsonValue, value));
            }
            else
            {
                tokens.Add(new SyntaxToken(pos, line.Length, TokenType.Plain, line));
            }

            pos += line.Length + 1;
        }

        return tokens;
    }

    private static List<SyntaxToken> TokenizeDockerfile(string text)
    {
        var tokens = new List<SyntaxToken>();
        var instructions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            "FROM", "RUN", "CMD", "LABEL", "MAINTAINER", "EXPOSE", "ENV", "ADD", "COPY", "ENTRYPOINT",
            "VOLUME", "USER", "WORKDIR", "ARG", "ONBUILD", "STOPSIGNAL", "HEALTHCHECK", "SHELL"
        };

        var lines = text.Split('\n');
        var pos = 0;

        foreach (var line in lines)
        {
            var trimmed = line.TrimStart();

            // Comment
            if (trimmed.StartsWith('#'))
            {
                tokens.Add(new SyntaxToken(pos, line.Length, TokenType.Comment, line));
                pos += line.Length + 1;
                continue;
            }

            // Instruction
            var spaceIdx = trimmed.IndexOf(' ');
            var instruction = spaceIdx > 0 ? trimmed[..spaceIdx] : trimmed;

            if (instructions.Contains(instruction))
            {
                var indent = line.Length - trimmed.Length;
                if (indent > 0)
                    tokens.Add(new SyntaxToken(pos, indent, TokenType.Plain, line[..indent]));

                tokens.Add(new SyntaxToken(pos + indent, instruction.Length, TokenType.Keyword, instruction));

                if (spaceIdx > 0)
                {
                    var args = trimmed[(spaceIdx + 1)..];
                    var argsStart = pos + indent + instruction.Length + 1;
                    tokens.Add(new SyntaxToken(argsStart, args.Length, TokenType.Plain, args));
                }
            }
            else
            {
                tokens.Add(new SyntaxToken(pos, line.Length, TokenType.Plain, line));
            }

            pos += line.Length + 1;
        }

        return tokens;
    }

    private static List<SyntaxToken> TokenizeMakefile(string text)
    {
        var tokens = new List<SyntaxToken>();
        var lines = text.Split('\n');
        var pos = 0;

        foreach (var line in lines)
        {
            // Comment
            if (line.TrimStart().StartsWith('#'))
            {
                tokens.Add(new SyntaxToken(pos, line.Length, TokenType.Comment, line));
                pos += line.Length + 1;
                continue;
            }

            // Variable assignment
            var assignMatch = Regex.Match(line, @"^([A-Za-z_][A-Za-z0-9_]*)\s*[:+?]?=");
            if (assignMatch.Success)
            {
                tokens.Add(new SyntaxToken(pos, assignMatch.Groups[1].Length, TokenType.JsonKey, assignMatch.Groups[1].Value));
                var eqPos = line.IndexOf('=', assignMatch.Groups[1].Length);
                tokens.Add(new SyntaxToken(pos + assignMatch.Groups[1].Length, eqPos - assignMatch.Groups[1].Length, TokenType.Plain, line[assignMatch.Groups[1].Length..eqPos]));
                tokens.Add(new SyntaxToken(pos + eqPos, 1, TokenType.Operator, "="));
                var value = line[(eqPos + 1)..];
                tokens.Add(new SyntaxToken(pos + eqPos + 1, value.Length, TokenType.JsonValue, value));
                pos += line.Length + 1;
                continue;
            }

            // Target
            var targetMatch = Regex.Match(line, @"^([A-Za-z0-9_./%-]+)\s*:");
            if (targetMatch.Success && !line.StartsWith('\t'))
            {
                tokens.Add(new SyntaxToken(pos, targetMatch.Groups[1].Length, TokenType.Function, targetMatch.Groups[1].Value));
                var rest = line[targetMatch.Groups[1].Length..];
                tokens.Add(new SyntaxToken(pos + targetMatch.Groups[1].Length, rest.Length, TokenType.Plain, rest));
                pos += line.Length + 1;
                continue;
            }

            tokens.Add(new SyntaxToken(pos, line.Length, TokenType.Plain, line));
            pos += line.Length + 1;
        }

        return tokens;
    }
}
