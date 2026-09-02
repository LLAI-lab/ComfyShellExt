using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using ComfyShellExt.Core.Util;

namespace ComfyShellExt.Core.Store
{
    /// <summary>
    /// The workflow database: a JSON Lines index plus a content addressed store of the extracted
    /// JSON documents. Identical workflows are stored once, which matters because a batch of images
    /// from one run shares the same graph.
    /// </summary>
    public sealed class WorkflowIndex
    {
        private readonly Dictionary<string, DbRecord> _byPath =
            new Dictionary<string, DbRecord>(StringComparer.OrdinalIgnoreCase);
        private readonly object _gate = new object();
        private bool _dirty;

        public WorkflowIndex(string dir)
        {
            Dir = string.IsNullOrEmpty(dir) ? Paths.DefaultDbDir : Path.GetFullPath(dir);
        }

        public string Dir { get; private set; }

        public string IndexPath { get { return Path.Combine(Dir, "index.jsonl"); } }

        public string ObjectsDir { get { return Path.Combine(Dir, "objects"); } }

        public int Count { get { lock (_gate) return _byPath.Count; } }

        public bool Dirty { get { return _dirty; } }

        public List<DbRecord> All()
        {
            lock (_gate) return new List<DbRecord>(_byPath.Values);
        }

        public void Load()
        {
            lock (_gate)
            {
                _byPath.Clear();
                if (!File.Exists(IndexPath)) return;
                using (var reader = new StreamReader(IndexPath, Encoding.UTF8))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        if (line.Length < 3) continue;
                        var record = DbRecord.FromJson(line);
                        if (record != null) _byPath[record.Path] = record;
                    }
                }
            }
        }

        public DbRecord Get(string path)
        {
            lock (_gate)
            {
                DbRecord record;
                return _byPath.TryGetValue(path, out record) ? record : null;
            }
        }

        /// <summary>True when the file is new or has changed since the last scan.</summary>
        public bool NeedsScan(string path, long size, long mtime)
        {
            var record = Get(path);
            return record == null || record.Size != size || record.MTime != mtime;
        }

        public void Put(DbRecord record)
        {
            if (record == null || string.IsNullOrEmpty(record.Path)) return;
            lock (_gate)
            {
                _byPath[record.Path] = record;
                _dirty = true;
            }
        }

        public int Remove(Func<DbRecord, bool> predicate)
        {
            lock (_gate)
            {
                var drop = new List<string>();
                foreach (var kv in _byPath) if (predicate(kv.Value)) drop.Add(kv.Key);
                foreach (var key in drop) _byPath.Remove(key);
                if (drop.Count > 0) _dirty = true;
                return drop.Count;
            }
        }

        /// <summary>Stores a JSON document and returns its hash, or null when json is null.</summary>
        public string PutObject(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            var bytes = Encoding.UTF8.GetBytes(json);
            string hash;
            using (var sha = SHA1.Create()) hash = Hex(sha.ComputeHash(bytes));
            var file = ObjectPath(hash);
            try
            {
                if (!File.Exists(file))
                {
                    Paths.EnsureDir(Path.GetDirectoryName(file));
                    var temp = file + ".tmp" + System.Threading.Thread.CurrentThread.ManagedThreadId;
                    File.WriteAllBytes(temp, bytes);
                    if (File.Exists(file)) File.Delete(temp);
                    else File.Move(temp, file);
                }
            }
            catch (Exception ex)
            {
                Log.Error("PutObject " + hash, ex);
                return null;
            }
            return hash;
        }

        public string GetObject(string hash)
        {
            if (string.IsNullOrEmpty(hash)) return null;
            var file = ObjectPath(hash);
            try { return File.Exists(file) ? File.ReadAllText(file, Encoding.UTF8) : null; }
            catch { return null; }
        }

        public string ObjectPath(string hash)
        {
            return Path.Combine(ObjectsDir, hash.Substring(0, 2), hash + ".json");
        }

        /// <summary>Rewrites index.jsonl through a temporary file so a crash cannot truncate it.</summary>
        public void Save()
        {
            lock (_gate)
            {
                Paths.EnsureDir(Dir);
                var temp = IndexPath + ".tmp";
                var records = new List<DbRecord>(_byPath.Values);
                records.Sort((a, b) => string.Compare(a.Path, b.Path, StringComparison.OrdinalIgnoreCase));
                using (var writer = new StreamWriter(temp, false, new UTF8Encoding(false)))
                    foreach (var record in records) writer.WriteLine(record.ToJson());
                if (File.Exists(IndexPath)) File.Delete(IndexPath);
                File.Move(temp, IndexPath);
                _dirty = false;
            }
        }

        private static string Hex(byte[] data)
        {
            var sb = new StringBuilder(data.Length * 2);
            foreach (var b in data) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }
    }
}
