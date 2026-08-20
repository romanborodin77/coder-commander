using System.Text;

namespace CoderCommander.Utils;

/// <summary>
/// Standard Win32 command-line argument quoting - the exact algorithm
/// <c>CommandLineToArgvW</c> parses, and the one every <c>ProcessStartInfo.Arguments</c> /
/// <c>CreateProcess</c> caller in this app needs, not the CSV/cmd convention of merely doubling
/// quote characters.
///
/// That simpler convention breaks on two common inputs: a path with a trailing backslash (the
/// final \" escapes the closing quote instead of ending the argument, so the argument
/// silently swallows the rest of the command line) and a backslash immediately followed by a
/// quote. Both are reachable with an ordinary Windows path - this is not just a style
/// preference, it is the difference between an argument being interpreted correctly and one
/// program's command line being reinterpreted as another's.
/// </summary>
internal static class Win32ArgumentQuoting
{
    /// <summary>Builds a full command line: <paramref name="executablePath"/> followed by each of
    /// <paramref name="arguments"/>, each individually quoted only when it needs to be.</summary>
    public static string BuildCommandLine(string executablePath, IReadOnlyList<string> arguments)
    {
        var sb = new StringBuilder();
        AppendArgument(sb, executablePath);
        foreach (var arg in arguments)
        {
            sb.Append(' ');
            AppendArgument(sb, arg);
        }
        return sb.ToString();
    }

    /// <summary>Quotes a single argument (wrapping in quotes, doubling backslashes that
    /// immediately precede a quote, and escaping the quote itself) and appends it to
    /// <paramref name="sb"/>. Left unquoted when it contains none of space/tab/quote.</summary>
    public static void AppendArgument(StringBuilder sb, string arg)
    {
        if (arg.Length > 0 && arg.IndexOfAny([' ', '\t', '"']) < 0)
        {
            sb.Append(arg);
            return;
        }

        sb.Append('"');
        var backslashCount = 0;
        foreach (var c in arg)
        {
            if (c == '\\')
            {
                backslashCount++;
                continue;
            }
            if (c == '"')
            {
                sb.Append('\\', backslashCount * 2 + 1).Append('"');
                backslashCount = 0;
                continue;
            }
            sb.Append('\\', backslashCount);
            backslashCount = 0;
            sb.Append(c);
        }
        sb.Append('\\', backslashCount * 2).Append('"');
    }

    /// <summary>Quotes a single standalone argument as a string (convenience wrapper over
    /// <see cref="AppendArgument"/> for a single value, e.g. a file path passed as one command-line
    /// argument to an external tool).</summary>
    public static string Quote(string arg)
    {
        var sb = new StringBuilder();
        AppendArgument(sb, arg);
        return sb.ToString();
    }
}
