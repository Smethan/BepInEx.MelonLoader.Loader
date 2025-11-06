using System;
using System.Collections.Generic;

namespace BepInEx.MelonLoader.Loader.Shared;

internal static class ArgParser
{
    private static readonly List<Argument> Arguments;

    static ArgParser()
    {
        Arguments = new List<Argument>();

        var args = Environment.GetCommandLineArgs();

        for (var i = 1; i < args.Length; i++)
        {
            var arg = args[i];

            if (arg.StartsWith("--", StringComparison.Ordinal))
            {
                arg = arg.Substring(2);
            }
            else if (arg.StartsWith("-", StringComparison.Ordinal))
            {
                arg = arg.Substring(1);
            }
            else
            {
                continue;
            }

            string value = null;

            var eqIdx = arg.IndexOf('=');
            if (eqIdx >= 0)
            {
                value = arg.Substring(eqIdx + 1);
                arg = arg.Substring(0, eqIdx);
            }
            else if (i + 1 < args.Length)
            {
                var next = args[i + 1];

                if (!next.StartsWith("-", StringComparison.Ordinal))
                {
                    value = next;
                    i++;
                }
            }

            Arguments.Add(new Argument
            {
                Name = arg,
                Value = value
            });
        }
    }

    internal static bool IsDefined(string longName)
    {
        return Arguments.Exists(x => x.Name.Equals(longName, StringComparison.OrdinalIgnoreCase));
    }

    internal static string GetValue(string longName)
    {
        var arg = Arguments.Find(x => x.Name.Equals(longName, StringComparison.OrdinalIgnoreCase));
        return arg?.Value;
    }

    private sealed class Argument
    {
        internal string Name { get; set; }
        internal string Value { get; set; }
    }
}
