using System.Collections.Generic;

namespace ComfyShellExt.Core
{
    /// <summary>A single piece of text metadata harvested from a media container.</summary>
    public struct MetaEntry
    {
        /// <summary>Where the value came from, e.g. "png:tEXt" or "mp4:ilst".</summary>
        public string Source;
        /// <summary>Container specific key, e.g. "workflow", "Comment", "0x010f".</summary>
        public string Key;
        public string Value;

        public MetaEntry(string source, string key, string value)
        {
            Source = source;
            Key = key;
            Value = value;
        }

        public string Origin
        {
            get { return string.IsNullOrEmpty(Key) ? Source : Source + ":" + Key; }
        }
    }

    /// <summary>
    /// Collects metadata from format readers and feeds it to a consumer. The consumer returns
    /// false when it has seen enough, which lets readers abandon the rest of the file.
    /// </summary>
    public sealed class MetaSink
    {
        /// <summary>Values larger than this are ignored; real workflows are a few MB at most.</summary>
        public const int MaxValueChars = 48 * 1024 * 1024;

        private readonly System.Func<MetaEntry, bool> _consume;
        private readonly List<MetaEntry> _kept;

        public MetaSink(System.Func<MetaEntry, bool> consume, bool keepAll)
        {
            _consume = consume;
            _kept = keepAll ? new List<MetaEntry>() : null;
        }

        public bool Stopped { get; private set; }

        public List<MetaEntry> Kept { get { return _kept; } }

        /// <summary>Returns false when the caller should stop reading the file.</summary>
        public bool Add(string source, string key, string value)
        {
            if (Stopped) return false;
            if (string.IsNullOrEmpty(value) || value.Length > MaxValueChars) return true;
            var entry = new MetaEntry(source, key, value);
            if (_kept != null) _kept.Add(entry);
            if (!_consume(entry)) Stopped = true;
            return !Stopped;
        }
    }
}
