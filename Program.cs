﻿using System;
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
            text = text.ToLower();
            symbols.RemoveAll(s => !s.Name.ToLower().Contains(text) && !s.Library.Contains(text) && !s.Object.Contains(text) && !s.Kind.Contains(text));
        }

        public void DumpInfo(bool doGroups = false)
        {
            Console.WriteLine("Size\tShared\tName\tBase\tObject\tLibrary\tKind");
            foreach (var s in symbols)
            {
                Console.WriteLine($"{s.Size}\t{s.Shared}\t{s.Name}\t{s.Base}\t{s.Object}\t{s.Library}\t{s.Kind}");
            }

            if (doGroups)
            { 
                PrepareGroups();
                Console.WriteLine("\nSize\tSymbols\tUnique\tGroup");
                foreach (var g in GetGroups())
                {
                    Console.WriteLine($"{g.Size}\t{g.Symbols.Count()}\t{g.Unique}\t{g.Text}");
                }
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

            g.Sort((g1, g2) => g1.Size.CompareTo(g2.Size));
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
                var groups = new List<string>();
                groups.Add(Kind);
                groups.Add(Library);
                groups.Add(Object);
                groups.Add(Name);
                
                var delims = "?$@".ToArray();
                foreach (var part in Name.Split(delims))
                {
                    if (part.IndexOfAny(lowerCase) >= 0 && (part.IndexOfAny(upperCase) >= 0 || part.Contains('_')) && part.Length > 4)
                    {
                        groups.Add(part);
                        groups.AddRange(GetVariations(part));
                    }
                }

                return new List<string>(groups.Distinct());
            }

            private List<string> GetVariations(string name)
            {
                var words = new List<string>();
                for (int i = 3; i < name.Length - 1; i++)
                {
                    if (char.IsUpper(name[i]) && char.IsLower(name[i - 1]))
                    {
                        words.Add(name.Substring(0, i));
                        name = name.Substring(i);
                        i = 0;
                    }
                    else if (name[i] == '_' && char.IsLower(name[i - 1]))
                    {
                        words.Add("_" + name.Substring(0, i).TrimStart('_'));
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
        static void Main(string[] args)
        {
            var map = new MapData();
            map.LoadMap(args.Count() >= 1 ? args[0] : "");
            for (int i = 1; i < args.Count(); i++)
            {
                if (args[i].ToLower() != "true")
                {
                    map.Filter(args[i]);
                }
            }
            map.DumpInfo(args.Count() >= 2);
        }
    }
}
