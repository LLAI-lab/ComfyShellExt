using System;
using System.Collections.Generic;

namespace ComfyShellExt.Cli
{
    /// <summary>Tiny command line parser: positional values plus --flag / --key value options.</summary>
    internal sealed class Args
    {
        private readonly Dictionary<string, string> _options =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public readonly List<string> Values = new List<string>();

        public Args(IEnumerable<string> args, params string[] valuelessFlags)
        {
            var flags = new HashSet<string>(valuelessFlags, StringComparer.OrdinalIgnoreCase);
            string pending = null;
            foreach (var arg in args)
            {
                if (pending != null)
                {
                    _options[pending] = arg;
                    pending = null;
                    continue;
                }
                if (arg.StartsWith("-", StringComparison.Ordinal) && arg.Length > 1)
                {
                    var name = arg.TrimStart('-');
                    int eq = name.IndexOf('=');
                    if (eq > 0)
                    {
                        _options[name.Substring(0, eq)] = name.Substring(eq + 1);
                    }
                    else if (flags.Contains(name))
                    {
                        _options[name] = "1";
                    }
                    else
                    {
                        pending = name;
                    }
                    continue;
                }
                Values.Add(arg);
            }
            if (pending != null) _options[pending] = "1";
        }

        public bool Has(string name) { return _options.ContainsKey(name); }

        public string Str(string name, string dflt = null)
        {
            string v;
            return _options.TryGetValue(name, out v) ? v : dflt;
        }

        public int Int(string name, int dflt)
        {
            string v; int r;
            return _options.TryGetValue(name, out v) && int.TryParse(v, out r) ? r : dflt;
        }
    }
}
