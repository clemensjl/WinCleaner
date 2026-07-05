using WinCleaner.Gui.Services;

namespace WinCleaner.Tests;

/// <summary>
/// Sichert das CommandLineToArgvW-konforme Quoting der GUI ab: ein gequoteter
/// Wert muss beim Parsen wieder EXAKT das Original ergeben – besonders bei
/// Pfaden mit Leerzeichen und abschließendem Backslash (sonst würde ein
/// irreversibles shred das falsche Ziel treffen).
/// </summary>
public class ElevatedCliQuoteTests
{
    [Theory]
    [InlineData("einfach", "einfach")]                       // keine Sonderzeichen -> unverändert
    [InlineData("", "\"\"")]                                 // leer -> ""
    [InlineData("C:\\Alte Daten", "\"C:\\Alte Daten\"")]     // Leerzeichen -> gequotet
    [InlineData("C:\\Alte Daten\\", "\"C:\\Alte Daten\\\\\"")] // trailing backslash verdoppelt
    [InlineData("a\"b", "\"a\\\"b\"")]                        // eingebettetes Quote escaped
    public void Quote_ProducesArgvSafeString(string input, string expected)
        => Assert.Equal(expected, ElevatedCli.Quote(input));

    [Theory]
    [InlineData("C:\\Alte Daten\\")]
    [InlineData("Ashampoo \"Snap\" 12")]
    [InlineData("simple")]
    [InlineData("with space")]
    [InlineData("trailing\\\\\\")]
    [InlineData("")]
    public void Quote_RoundTripsThroughArgvParser(string input)
    {
        // Der gequotete String, durch dieselbe Zerlegung geschickt, die Windows
        // (CommandLineToArgvW) nutzt, muss genau ein Argument = das Original liefern.
        var parsed = SplitArgs(ElevatedCli.Quote(input));
        Assert.Single(parsed);
        Assert.Equal(input, parsed[0]);
    }

    /// <summary>Referenz-Implementierung der CommandLineToArgvW-Zerlegung für einen Token-String.</summary>
    private static List<string> SplitArgs(string commandLine)
    {
        var args = new List<string>();
        var cur = new System.Text.StringBuilder();
        bool inQuotes = false;
        bool any = false;
        int i = 0;

        while (i < commandLine.Length)
        {
            char c = commandLine[i];

            if (c == '\\')
            {
                int bs = 0;
                while (i < commandLine.Length && commandLine[i] == '\\') { bs++; i++; }
                if (i < commandLine.Length && commandLine[i] == '"')
                {
                    cur.Append('\\', bs / 2);
                    if (bs % 2 == 1) { cur.Append('"'); i++; any = true; }
                    // gerade Anzahl: Quote bleibt als Delimiter, unten behandelt
                }
                else
                {
                    cur.Append('\\', bs);
                }
                continue;
            }

            if (c == '"')
            {
                inQuotes = !inQuotes;
                any = true;
                i++;
                continue;
            }

            if (char.IsWhiteSpace(c) && !inQuotes)
            {
                if (any || cur.Length > 0) { args.Add(cur.ToString()); cur.Clear(); any = false; }
                i++;
                continue;
            }

            cur.Append(c);
            any = true;
            i++;
        }

        if (any || cur.Length > 0) args.Add(cur.ToString());
        return args;
    }
}
