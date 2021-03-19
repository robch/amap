using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;

namespace amap
{
    public class MapData
    {
        public MapData()
        {

        }

        public void LoadMap(string fileName)
        {
            ReadLinesFromFile(fileName);
            ProcessCodeSymbols();
            ProcessDataSymbols();
            CalculateSizes();
            FinalPrep();
        }

        private void FinalPrep()
        {
            symbols.Sort((s1, s2) => {
                var c1 = s1.BaseOffset.CompareTo(s2.BaseOffset);
                var c2 = s1.Name.CompareTo(s2.Name);
                return c1 == 0 ? c2 : c1;
            });

            List<Symbol> distinct = new List<Symbol>();

            Int64 offset = 0;
            var prevName = "";
            foreach (var s in symbols)
            {
                if (s.Name != prevName || s.BaseOffset != offset)
                {
                    distinct.Add(s);
                }
                offset = s.BaseOffset;
                prevName = s.Name;
            }

            symbols = distinct;
        }

        private string FindFirstContains(string text)
        {
            while (lines.Count > 0)
            {
                var line = lines.Dequeue();
                if (line.Contains(text))
                {
                    return line;
                }
            }

            return null;
        }

        private string FindFirstStartsWith(string text)
        {
            while (lines.Count > 0)
            {
                var line = lines.Dequeue();
                if (line.StartsWith(text))
                {
                    return line;
                }
            }

            return null;
        }

        private string FindFirstNonWhiteSpace()
        {
            while (lines.Count > 0)
            {
                var line = lines.Dequeue();
                if (!string.IsNullOrWhiteSpace(line))
                {
                    return line;
                }
            }

            return null;
        }

        private void ReadLinesFromFile(string fileName)
        {
            lines = new Queue<string>(File.ReadAllLines(fileName));
        }

        private void ProcessCodeSymbols()
        {
            ProcessSymbolsInternal("code", FindFirstContains("__ImageBase"));
        }

        private void ProcessDataSymbols()
        {
            FindFirstContains("Static symbols");
            ProcessSymbolsInternal("data", FindFirstNonWhiteSpace());
        }

        private void ProcessSymbolsInternal(string kind, string line)
        {
            while (!string.IsNullOrWhiteSpace(line))
            {
                symbols.Add(new Symbol(line, kind));
                line = lines.Count > 0 ? lines.Dequeue() : "";
            }
        }

        private void CalculateSizes()
        {
            var first = symbols.FirstOrDefault();
            var imageBase = first.BaseOffset;

            symbols.RemoveAll(s => s.BaseOffset < imageBase);
            symbols.Sort((s1, s2) => s1.BaseOffset.CompareTo(s2.BaseOffset));

            for (int i = symbols.Count - 2; i >= 0; i--)
            {
                symbols[i].Size = symbols[i + 1].BaseOffset - symbols[i].BaseOffset;
                if (symbols[i].Size == 0 && symbols[i].BaseOffset == symbols[i + 1].BaseOffset)
                {
                    symbols[i].Size = symbols[i + 1].Size;

                    symbols[i].Shared = "shared";
                    if (symbols[i + 1].Shared == "unique") symbols[i + 1].Shared = "shared(first)";
                }
                else if (symbols[i].Size >= 100000)
                {
                    symbols[i].Size = 0;
                    symbols[i].Shared = "unknown";
                }
            }
        }

        public void Filter(string text)
        {
            Func<Symbol, string, bool> removeCheckN = (s, t) => !s.Name.ToLower().Contains(t);
            Func<Symbol, string, bool> removeCheckL = (s, t) => !s.Library.Contains(t);
            Func<Symbol, string, bool> removeCheckO = (s, t) => !s.Object.Contains(t);
            Func<Symbol, string, bool> removeCheckK = (s, t) => !s.Kind.Contains(t);
            Func<Symbol, string, bool> removeCheckAll = (s, t) => !s.Name.ToLower().Contains(t) && !s.Library.Contains(t) && !s.Object.Contains(t) && !s.Kind.Contains(t);

            var keep = text.StartsWith("-");
            if (keep) text = text.Substring(1);
            text = text.ToLower();

            var at = text.IndexOf(':');
            Func<Symbol, string, bool> remove =
                  text.StartsWith("symbol:") ? removeCheckN
                : text.StartsWith("library:") ? removeCheckL
                : text.StartsWith("object:") ? removeCheckO
                : text.StartsWith("kind:") ? removeCheckK
                : removeCheckAll;
            if (at > 0) text = text.Substring(at + 1);

            Func<Symbol, string, bool> notRemove = (s, t) => !remove(s, t);

            var match = keep ? notRemove : remove;
            symbols.RemoveAll(s => match(s, text));
        }

        public void DumpInfo(bool outputSymbolsObjectsLibrariesKind, bool outputUnigramBigrams, bool outputMapDump, List<string> finalFilters, int tail)
        {
            List<string> output = new List<string>();

            if (outputMapDump)
            {
                output.Add("Size\tShared\tName\tBase\tObject\tLibrary\tKind");
                foreach (var s in symbols)
                {
                    output.Add($"{s.Size}\t{s.Shared}\t{s.Name}\t{s.Base}\t{s.Object}\t{s.Library}\t{s.Kind}");
                }
            }

            if (outputSymbolsObjectsLibrariesKind || outputUnigramBigrams)
            { 
                PrepareGroups();
                output.Add("\nSize\tSymbols\tUnique\tGroup");
                foreach (var g in GetGroups())
                {
                    var unigramOrBigram = !g.Text.Contains(":");
                    var doit = (outputUnigramBigrams && unigramOrBigram) || (outputSymbolsObjectsLibrariesKind && !unigramOrBigram);
                    if (doit) output.Add($"{g.Size}\t{g.Symbols.Count()}\t{g.Unique}\t{g.Text}");
                }
            }

            if (finalFilters.Count > 0)
            {
                output.RemoveAll(line => finalFilters.Count(filter => line.Contains(filter)) == 0);
            }

            if (output.Count > tail) output.RemoveRange(0, output.Count - tail);
            foreach (var line in output)
            {
                Console.WriteLine(line);
            }
        }

        class Group
        {
            public string Text;
            public Int64 Size;
            public Int32 Unique;

            public List<Symbol> Symbols = new List<Symbol>();
        }

        private void PrepareGroups()
        {
            foreach (var s in symbols)
            {
                foreach (var gn in s.GetGroupNames())
                {
                    if (!groups.ContainsKey(gn)) groups[gn] = new Group();

                    var g = groups[gn];
                    g.Text = gn;
                    g.Size += s.Shared == "shared" ? 0 : s.Size;
                    g.Unique += s.Shared == "shared" ? 0 : 1;

                    g.Symbols.Add(s);
                }
            }
        }

        private List<Group> GetGroups()
        {
            List<Group> g = new List<Group>();
            foreach (var gn in groups.Keys)
            {
                var group = groups[gn];
                g.Add(group);
            }

            g.Sort((g1, g2) => {
                var scomp = g1.Size.CompareTo(g2.Size);
                if (scomp != 0) return scomp;
                var lcomp = g1.Text.Length.CompareTo(g2.Text.Length);
                if (lcomp != 0) return lcomp;
                var tcomp = g1.Text.CompareTo(g2.Text);
                if (tcomp != 0) return tcomp;
                return 0;
            });
            return g;
        }

        private Dictionary<string, Group> groups = new Dictionary<string, Group>();

        class Symbol
        {
            private static char[] lowerCase = "abcdefghijklmnopqrstuvwxyz".ToArray();
            private static char[] upperCase = "ABCDEFGHIJKLMNOPQRSTUVWXYZ".ToArray();

            public Symbol(string line, string kind)
            {
                Kind = kind;

                line = line.TrimStart(' ').TrimEnd(' ');

                Address = line.Substring(0, line.IndexOf(' '));
                line = line.Substring(Address.Length).TrimStart(' ');

                Name = line.Substring(0, line.IndexOf(' '));
                line = line.Substring(Name.Length).TrimStart(' ');

                Base = line.Substring(0, line.IndexOf(' '));
                line = line.Substring(Base.Length).TrimStart(' ');

                line = line.TrimStart(' ').Split(' ').LastOrDefault();
                var parts = line.Split(':');
                Library = parts.Length == 2 ? parts.First() : line;
                Object = parts.Length == 2 ? parts.Last() : line;

                parts = Address.TrimEnd(' ').Split(':');
                AddressSection = parts.Length == 2 ? Int32.Parse(parts.First(), System.Globalization.NumberStyles.HexNumber) : 0;
                AddressOffset = parts.Length == 2 ? Int32.Parse(parts.Last(), System.Globalization.NumberStyles.HexNumber) : 0;

                BaseOffset = Int64.Parse(Base, System.Globalization.NumberStyles.HexNumber);
            }

            public List<string> GetGroupNames()
            {
                var thingsToSplit = new List<string>();
                thingsToSplit.Add($"@{Library}");
                thingsToSplit.Add($"@{Object}");
                thingsToSplit.Add(Name);

                var groups = new List<string>();
                groups.Add($"kind:{Kind}");
                groups.Add($"library:{Library}");
                groups.Add($"object:{Object}");
                groups.Add($"symbol:{Name}");

                var delims = "?$@<>.".ToArray();
                foreach (var thing in thingsToSplit)
                {
                    foreach (var part in thing.Split(delims))
                    {
                        if (part.IndexOfAny(lowerCase) >= 0 && (part.IndexOfAny(upperCase) >= 0 || part.Contains('_')) && part.Length > 4)
                        {
                            groups.Add(part);
                            groups.AddRange(GetVariations(part));
                        }
                        else if (part.Length > 2 && !thing.Contains($":{part}"))
                        {
                            groups.Add(part);
                        }
                    }
                }

                return new List<string>(groups.Distinct());
            }

            private List<string> GetVariations(string name)
            {
                var words = new List<string>();
                for (int i = 2; i < name.Length - 1; i++)
                {
                    if (char.IsUpper(name[i]) && char.IsLower(name[i - 1]))
                    {
                        words.Add(name.Substring(0, i));
                        name = name.Substring(i);
                        i = 0;
                    }
                    else if (name[i] == '_' && char.IsLower(name[i - 1]))
                    {
                        words.Add(name.Substring(0, i).TrimStart('_') + "_");
                        name = name.Substring(i + 1);
                        i = 0;
                    }
                }

                if (words.Count > 0 && name.Length > 0)
                {
                    words.Add(name);
                }

                var varations = new List<string>();
                if (words.Count > 2)
                {
                    for (int i = 1; i < words.Count; i++)
                    {
                        varations.Add(words[i - 1] + words[i]);
                    }
                }

                foreach (var word in words)
                {
                    if (word.Length >= 3 && char.IsUpper(word[0]) && char.IsLower(word[1]))
                    {
                        varations.Add(word);
                    }
                }

                return varations;
            }

            public string Kind;

            public string Name;

            public string Address;
            public Int32 AddressSection;
            public Int32 AddressOffset;

            public string Base;
            public Int64 BaseOffset;

            public string Library;
            public string Object;

            public Int64 Size = 0;
            public string Shared = "unique";

        }

        private Queue<string> lines;
        private List<Symbol> symbols = new List<Symbol>();
    }

    class Program
    {
        static int Main(string[] args)
        {
            var map = new MapData();
            if (args.Count() == 0)
            {
                Console.WriteLine(
                    "amap filename.map [[-]filter1 [[-]filter2 ...]] [+/++/-] [,TAIL]\n" +
                    "\n" +
                    "   (default)  output symbols, objects, libraries, and kind\n" +
                    "\n" +
                    "       +      output symbols, objects, libraries, and kind + unigram and bigram analysis + \n" +
                    "       ++     output symbols, objects, libraries, and kind + unigram and bigram analysis + map dump\n" +
                    "       -      output only unigram and bigram analysis\n" +
                    "       --     output only map dump\n"
                );
                return 1;
            }

            var finalFilters = new List<string>();
            var outputSymbolsObjectsLibrariesKind = true;
            var outputUnigramBigrams = false;
            var outputMapDump = false;
            var tail = int.MaxValue;

            map.LoadMap(args[0]);
            for (int i = 1; i < args.Count(); i++)
            {
                var arg = args[i];
                if (arg == "+") { outputUnigramBigrams = true; continue; }
                if (arg == "++") { outputUnigramBigrams = true; outputMapDump = true; continue; }
                if (arg == "-") { outputSymbolsObjectsLibrariesKind = false; outputUnigramBigrams = true; continue; }
                if (arg == "--")  { outputSymbolsObjectsLibrariesKind = false; outputMapDump = true; continue; }
                if (arg.StartsWith(",")) { tail = int.Parse(arg.Substring(1)); continue; }
                if (arg.EndsWith(":")) { finalFilters.Add(arg); continue; }

                map.Filter(args[i]);
            }

            map.DumpInfo(outputSymbolsObjectsLibrariesKind, outputUnigramBigrams, outputMapDump, finalFilters, tail);

            return 0;
        }
    }
}
