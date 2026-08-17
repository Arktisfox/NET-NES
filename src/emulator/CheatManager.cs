using NES;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Emulator
{
    public static class CheatManager
    {
        private const string CHEATS_DIR = "cheats";
        private const string CHEATS_FORMAT = "{0}.json";

        private static Cartridge? activeCartridge => EmulatorState.NES?.Cartridge;
        private static readonly Dictionary<string, List<CheatEntry>> cheatsByGame = new();
        private static string HashKey(Cartridge cartridge) => BitConverter.ToString(cartridge.GetRomHash()).Replace("-", "");

        public class CheatEntry
        {
            public string Code = "";
            public bool Enabled = true;
            public string? Description;

            public CheatEntry() { }

            public CheatEntry(string code, bool enabled = true, string? description = null)
            {
                Code = code;
                Enabled = enabled;
                Description = description;
            }
        }

        public static void ActivateCheats(Cartridge cartridge, out int numEnabled)
        {
            numEnabled = 0;

            string key = HashKey(cartridge);

            if (!cheatsByGame.TryGetValue(key, out var entries))
                return;

            // Make sure we don't retain cheats from a previous cartridge.
            cartridge.ClearGenieCodes();

            foreach (var entry in entries)
            {
                if (!entry.Enabled)
                    continue;

                if (GameGenie.GameGenie.TryParse(entry.Code, out _))
                {
                    cartridge.AddGenieCode(entry.Code);
                    numEnabled++;
                }
            }
        }

        public static bool AddCode(Cartridge cartridge, string rawCode, string? description = null)
        {
            if (!GameGenie.GameGenie.TryParse(rawCode, out _)) return false;

            string normalized = rawCode.Trim().ToUpperInvariant();
            var entries = GetOrCreateEntries(cartridge);

            // Adding a code that's already saved replaces it rather than
            // duplicating it (matches Cartridge.AddGenieCode's one-override-
            // per-address behavior).
            entries.RemoveAll(e => e.Code == normalized);
            entries.Add(new CheatEntry(normalized, enabled: true, description));

            cartridge.AddGenieCode(normalized);
            SaveForRom(HashKey(cartridge));
            return true;
        }

        public static bool AddCode(string rawCode, string? description = null)
        {
            if (activeCartridge == null) return false;
            return AddCode(activeCartridge, rawCode, description);
        }

        public static bool RemoveCode(Cartridge cartridge, string code)
        {
            string normalized = code.Trim().ToUpperInvariant();
            string key = HashKey(cartridge);

            if (!cheatsByGame.TryGetValue(key, out var entries))
                return false;

            int removed = entries.RemoveAll(e => e.Code == normalized);
            if (removed == 0)
                return false;

            cartridge.RemoveGenieCode(normalized);
            SaveForRom(key);
            return true;
        }

        public static bool RemoveCode(string code)
        {
            if (activeCartridge == null) return false;
            return RemoveCode(activeCartridge, code);
        }

        public static bool SetEnabled(string code, bool enabled)
        {
            if (activeCartridge == null) return false;

            string normalized = code.Trim().ToUpperInvariant();
            string key = HashKey(activeCartridge);
            if (!cheatsByGame.TryGetValue(key, out var entries)) return false;

            var entry = entries.FirstOrDefault(e => e.Code == normalized);
            if (entry == null) return false;

            entry.Enabled = enabled;
            if (enabled) activeCartridge.AddGenieCode(normalized);
            else activeCartridge.RemoveGenieCode(normalized);

            SaveForRom(key);
            return true;
        }

        public static void ClearCodes(Cartridge cartridge)
        {
            string key = HashKey(cartridge);

            cheatsByGame.Remove(key);
            cartridge.ClearGenieCodes();
            SaveForRom(key);
        }

        public static void ClearCodes()
        {
            if (activeCartridge == null) return;
            ClearCodes(activeCartridge);
        }

        public static IReadOnlyList<CheatEntry> GetCodesForActiveGame()
        {
            if (activeCartridge == null) return Array.Empty<CheatEntry>();
            return cheatsByGame.TryGetValue(HashKey(activeCartridge), out var entries)
                ? entries
                : Array.Empty<CheatEntry>();
        }

        public static IReadOnlyList<CheatEntry> GetCodesForGame(Cartridge cartridge)
        {
            return GetOrCreateEntries(cartridge);

        }
        private static List<CheatEntry> GetOrCreateEntries(Cartridge cartridge)
        {
            string key = HashKey(cartridge);
            if (!cheatsByGame.TryGetValue(key, out var entries))
            {
                entries = new List<CheatEntry>();
                cheatsByGame[key] = entries;
            }
            return entries;
        }

        // Save/load
        public static void Load()
        {
            if(!Directory.Exists(CHEATS_DIR))
            {
                return;
            }

            foreach (var cheatFile in Directory.GetFiles(CHEATS_DIR, "*.json"))
            {
                string key = Path.GetFileNameWithoutExtension(cheatFile);

                try
                {
                    string json = File.ReadAllText(cheatFile);

                    var entries = JsonConvert.DeserializeObject<List<CheatEntry>>(json);

                    if (entries != null)
                        cheatsByGame[key] = entries;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to load cheats '{cheatFile}': {ex.Message}");
                }
            }
        }

        private static void SaveForRom(string romHashKey)
        {
            Directory.CreateDirectory(CHEATS_DIR);

            string filename = string.Format(CHEATS_FORMAT, romHashKey);
            string filepath = Path.Combine(CHEATS_DIR, filename);

            if (!cheatsByGame.TryGetValue(romHashKey, out var entries) ||
                entries.Count == 0)
            {
                if (File.Exists(filepath))
                    File.Delete(filepath);

                return;
            }

            string json = JsonConvert.SerializeObject(entries, Formatting.Indented);
            File.WriteAllText(filepath, json);
        }
    }
}
