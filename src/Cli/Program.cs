using System;
using System.Text;

namespace ComfyShellExt.Cli
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] rawArgs)
        {
            try { Console.OutputEncoding = Encoding.UTF8; } catch { }
            if (rawArgs.Length == 0 || rawArgs[0] == "-h" || rawArgs[0] == "--help")
            {
                Usage();
                return rawArgs.Length == 0 ? 2 : 0;
            }
            var rest = new string[rawArgs.Length - 1];
            Array.Copy(rawArgs, 1, rest, 0, rest.Length);
            try
            {
                switch (rawArgs[0].ToLowerInvariant())
                {
                    case "check":
                        return CheckCommand.Run(new Args(rest, "json", "dump", "deep"));
                    case "dump":
                        return DumpCommand.Run(new Args(rest, "tree", "deep"));
                    case "selftest":
                        return CheckCommand.SelfTest(new Args(rest));
                    case "scan":
                        return ScanCommand.Run(new Args(rest, "no-recurse", "all", "quiet", "prune"));
                    case "find":
                        return QueryCommands.Find(new Args(rest, "paths-only"));
                    case "get":
                        return QueryCommands.Get(new Args(rest, "prompt", "graph"));
                    case "list":
                        return QueryCommands.List(new Args(rest, "no-workflow", "paths-only"));
                    case "stats":
                        return QueryCommands.Stats(new Args(rest));
                    case "preview":
                        return PreviewCommand.Run(new Args(rest, "stream", "shell"));
                    case "diag":
                        return DiagCommand.Run(new Args(rest, "plan"));
                    case "comcheck":
                        return ComCheckCommand.Run(new Args(rest));
                    default:
                        Console.Error.WriteLine("unknown command: " + rawArgs[0]);
                        Usage();
                        return 2;
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.GetType().Name + ": " + ex.Message);
                return 3;
            }
        }

        private static void Usage()
        {
            Console.WriteLine(@"ComfyWorkflowDb - extract and index ComfyUI workflows from images and videos

  check <file|dir> [...]          show what metadata a file carries
      --json                      one JSON line per file
      --dump                      also print the workflow JSON
      --deep                      brute force scan the whole file, not just both ends
  dump <file|dir> [...]           list every metadata entry, for diagnosing a file that
                                  should have a workflow but shows none
      --tree                      also print the container structure (png chunks, mp4 boxes)
      --deep                      brute force scan the whole file
      --chars <n>                 preview length per entry (default 200)
  selftest [fixtureDir]           verify detection against expected.json

  scan <dir|file> [...]           build or update the workflow database
      --db <dir>                  database location (default %LOCALAPPDATA%\ComfyShellExt\db)
      --ext .png,.mp4             restrict to these extensions
      --no-recurse                do not walk subdirectories
      --all                       re-inspect files even when size and date match
      --prune                     drop index entries whose file is gone
      --jobs <n>                  worker threads (default: processor count, max 8)
      --quiet                     only print the summary

  list [--no-workflow] [--limit n] [--paths-only] [--db <dir>]
  find <text> [--node <class>] [--model <name>] [--limit n] [--paths-only] [--db <dir>]
  get <file> [--graph|--prompt] [-o out.json] [--db <dir>]
  stats [--db <dir>]

  preview <file|dir> [...]        render thumbnails the way Explorer would, without installing
      --size <px>                 requested thumbnail size (default 256)
      -o <file|dir>               save the PNG result
      --sheet <file.png>          save every result as one contact sheet
      --stream                    initialise through a stream instead of a path
      --shell                     ask the shell instead: shows what Explorer actually draws,
                                  so comparing with the normal output proves whether the
                                  handler is registered and being called
  diag                            show the handler currently registered for each file type
      --plan                      also list the registry keys install.bat would claim
  comcheck [file]                 verify the COM interfaces Explorer will query for

Supported: png jpg webp gif mp4 mov mkv webm avi flac (plus a raw scan fallback).");
        }
    }
}
