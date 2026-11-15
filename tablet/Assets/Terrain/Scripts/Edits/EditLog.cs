using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using MartianMap.Edits;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace MartianMap.Terrain
{
    /// <summary>
    /// The local edit log of one zone (Tablet, "Caché local"), plan C of E8: one JSON line per edit
    /// in <c>{zone folder}/edits.jsonl</c>, the wire edit plus local fields that never travel
    /// (<c>_from_habitat</c>, <c>_acked</c>). Append-only on create; rewritten whole when an ack or a
    /// rejection changes a line, through a temporary file. Main thread only.
    /// </summary>
    public sealed class EditLog
    {
        public const string FileName = "edits.jsonl";

        private readonly string _path;
        private readonly List<Entry> _entries = new List<Entry>();

        private EditLog(string zoneId, string path)
        {
            ZoneId = zoneId;
            _path = path;
        }

        public string ZoneId { get; }

        public int Count => _entries.Count;

        /// <summary>Every edit, in log order, with <c>fromHabitat</c> as it stands now.</summary>
        public Edit[] Edits => _entries.Select(e => e.Edit).ToArray();

        /// <summary>Local edits the habitat has not acked yet, oldest first.</summary>
        public IReadOnlyList<Edit> Pending => _entries.Where(e => !e.Edit.FromHabitat && !e.Acked).Select(e => e.Edit).ToList();

        /// <summary>The next Lamport value of this device: one more than any seen.</summary>
        public long NextLamport => (_entries.Count == 0 ? 0 : _entries.Max(e => e.Edit.Lamport)) + 1;

        public static EditLog Open(string zoneFolder, string zoneId)
        {
            Directory.CreateDirectory(zoneFolder);
            var log = new EditLog(zoneId, Path.Combine(zoneFolder, FileName));
            if (!File.Exists(log._path))
            {
                return log;
            }

            var lineNumber = 0;
            foreach (var line in File.ReadAllLines(log._path, Encoding.UTF8))
            {
                lineNumber++;
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                JObject json;
                try
                {
                    json = JObject.Parse(line);
                }
                catch (JsonReaderException bad)
                {
                    Debug.LogWarning($"[Terrain] edit log line skipped zone_id={zoneId} line={lineNumber} error={bad.Message}");
                    continue;
                }

                var fromHabitat = (bool?)json["_from_habitat"] ?? false;
                var acked = (bool?)json["_acked"] ?? false;
                if (EditJson.TryRead(json, fromHabitat, out var edit))
                {
                    log._entries.Add(new Entry(edit, acked));
                }
            }

            return log;
        }

        /// <summary>The creator of an entity: the author of its first edit in the log, or null.</summary>
        public string CreatorOf(string entityId)
        {
            return _entries.Where(e => e.Edit.EntityId == entityId)
                .OrderBy(e => e.Edit.Lamport).ThenBy(e => e.Edit.Author, StringComparer.Ordinal).ThenBy(e => e.Edit.Uuid, StringComparer.Ordinal)
                .Select(e => e.Edit.Creator).FirstOrDefault();
        }

        public bool Contains(string uuid) => _entries.Any(e => e.Edit.Uuid == uuid);

        /// <summary>Appends a local edit (not acked yet).</summary>
        public void Append(Edit edit)
        {
            if (Contains(edit.Uuid))
            {
                return;
            }

            _entries.Add(new Entry(edit, false));
            File.AppendAllText(_path, Line(_entries[_entries.Count - 1]) + "\n", Encoding.UTF8);
        }

        /// <summary>Adds winners from <c>changes</c>: new ones as from the habitat, known ones marked so.</summary>
        /// <returns>True if the log changed.</returns>
        public bool MergeWinners(IEnumerable<Edit> winners)
        {
            var changed = false;
            foreach (var winner in winners)
            {
                var index = _entries.FindIndex(e => e.Edit.Uuid == winner.Uuid);
                if (index < 0)
                {
                    _entries.Add(new Entry(winner, true));
                    changed = true;
                }
                else if (!_entries[index].Edit.FromHabitat)
                {
                    _entries[index] = new Entry(WithFromHabitat(_entries[index].Edit), true);
                    changed = true;
                }
            }

            if (changed)
            {
                Rewrite();
            }

            return changed;
        }

        /// <summary>
        /// Applies one ack of POST /edits: <c>accepted</c> / <c>duplicate</c> mark the edit as from the
        /// habitat (its <c>confirmed</c> is not evaluated again); <c>rejected</c> removes it.
        /// </summary>
        /// <returns>The edit that was rejected, or null.</returns>
        public Edit ApplyAck(string uuid, string status)
        {
            var index = _entries.FindIndex(e => e.Edit.Uuid == uuid);
            if (index < 0)
            {
                return null;
            }

            Edit rejected = null;
            switch (status)
            {
                case "accepted":
                case "duplicate":
                    _entries[index] = new Entry(WithFromHabitat(_entries[index].Edit), true);
                    break;
                case "rejected":
                    rejected = _entries[index].Edit;
                    _entries.RemoveAt(index);
                    break;
                default:
                    Debug.LogWarning($"[Terrain] unknown ack status={status} uuid={uuid}");
                    return null;
            }

            Rewrite();
            return rejected;
        }

        private void Rewrite()
        {
            var temporary = _path + ".tmp";
            File.WriteAllLines(temporary, _entries.Select(Line), Encoding.UTF8);
            if (File.Exists(_path))
            {
                File.Delete(_path);
            }

            File.Move(temporary, _path);
        }

        private static string Line(Entry entry)
        {
            var json = EditJson.Write(entry.Edit);
            json["_from_habitat"] = entry.Edit.FromHabitat;
            json["_acked"] = entry.Acked;
            return json.ToString(Formatting.None);
        }

        private static Edit WithFromHabitat(Edit e) =>
            e.FromHabitat ? e : new Edit(e.Uuid, e.Lamport, e.Author, e.Creator, e.ZoneId, e.EntityId, e.Op, e.Payload, e.Tombstone, true);

        private readonly struct Entry
        {
            public Entry(Edit edit, bool acked)
            {
                Edit = edit;
                Acked = acked;
            }

            public Edit Edit { get; }
            public bool Acked { get; }
        }
    }
}
