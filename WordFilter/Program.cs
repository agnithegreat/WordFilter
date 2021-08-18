using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RestSharp;
using SQLite;

namespace WordFilter
{
    internal class Program
    {
        public static void Main(string[] args)
        {
            var path = Environment.CurrentDirectory + "/Application.sqlite";
            var db = new SQLiteConnection(path);
            db.CreateTable<WordEntry>();
            db.CreateTable<WordDefinitionEntry>();

            InsertFromFile(db);
            CollectDefinitions(db);
            
            var table = db.Table<WordEntry>();

            Console.WriteLine("Введите количество букв или само слово");
            while (ReadInput(out string input))
            {
                var word = ReadInt(input, out int result) ? Roll(table, result) : input;
                Search(table, word);
            }
        }

        private static bool ReadInput(out string input)
        {
            input = Console.ReadLine();
            return input != null;
        }

        private static bool ReadInt(string input, out int result)
        {
            return int.TryParse(input, out result);
        }

        private static bool GetBit(int number, int index)
        {
            return (number & (1 << index)) != 0;
        }

        private static void InsertFromFile(SQLiteConnection db)
        {
            var input = Environment.CurrentDirectory + "/web2";
            
            if (!File.Exists(input)) return;

            var requestsRemaining = 10;

            using (StreamReader streamReader = new StreamReader(input))
            {
                string line = null;

                while ((line = streamReader.ReadLine()) != null)
                {
                    if (requestsRemaining < 10)
                    {
                        Console.WriteLine("Requests are about to finish");
                        Console.WriteLine("Next word is " + line);
                        return;
                    }
                    
                    var word = line.ToLower();
                    if (line != word) continue;
                    
                    if (word.Length < 3) continue;
                    if (word.Length > 7) continue;
                    
                    if (db.Find<WordEntry>(w => w.Word == word) != null) continue;

                    var entry = new WordEntry
                    {
                        Word = word,
                        Normalized = String.Concat(word.OrderBy(c => c)),
                        Length = word.Length,
                    };

                    var client = new RestClient($"https://wordsapiv1.p.rapidapi.com/words/{word}");
                    var request = new RestRequest(Method.GET);
                    request.AddHeader("x-rapidapi-key", "ad8e1cadd3msh5b1378982a81bcap15080fjsn8d942992b7ad");
                    request.AddHeader("x-rapidapi-host", "wordsapiv1.p.rapidapi.com");
                    IRestResponse response = client.Execute(request);
                    
                    Console.WriteLine(response.Content);

                    requestsRemaining = response.Headers
                        .Where(h => h.Name == "X-RateLimit-requests-Remaining")
                        .Select(h => h.Value != null ? int.Parse(h.Value.ToString()) : 0)
                        .FirstOrDefault();
                    Console.WriteLine("Requests remaining: " + requestsRemaining);

                    if (response.IsSuccessful)
                    {
                        var data = SimpleJson.DeserializeObject<WordInfo>(response.Content);
                        if (data.results != null)
                        {
                            entry.Frequency = data.frequency;
                            
                            db.Insert(entry);
                            Console.WriteLine("Word id: " + entry.Id);

                            for (var i = 0; i < data.results.Length; i++)
                            {
                                db.Insert(new WordDefinitionEntry
                                {
                                    WordId = entry.Id,
                                    Definition = data.results[i].definition
                                });
                            }
                        }
                    }
                } 
            }
        }
        
        private static void CollectDefinitions(SQLiteConnection db)
        {
            var requestsRemaining = 10;

            var words = db.Table<WordEntry>();
            
            foreach (var entry in words)
            {
                var word = entry.Word;
                if (db.Table<WordDefinitionEntry>().FirstOrDefault(w => w.WordId == entry.Id) != null) continue;
                
                if (requestsRemaining < 10)
                {
                    Console.WriteLine("Requests are about to finish");
                    Console.WriteLine("Next word is " + word);
                    return;
                }
                
                var client = new RestClient($"https://wordsapiv1.p.rapidapi.com/words/{word}");
                var request = new RestRequest(Method.GET);
                request.AddHeader("x-rapidapi-key", "ad8e1cadd3msh5b1378982a81bcap15080fjsn8d942992b7ad");
                request.AddHeader("x-rapidapi-host", "wordsapiv1.p.rapidapi.com");
                IRestResponse response = client.Execute(request);
                
                Console.WriteLine(response.Content);

                requestsRemaining = response.Headers
                    .Where(h => h.Name == "X-RateLimit-requests-Remaining")
                    .Select(h => h.Value != null ? int.Parse(h.Value.ToString()) : 0)
                    .FirstOrDefault();
                Console.WriteLine("Requests remaining: " + requestsRemaining);

                if (response.IsSuccessful)
                {
                    var data = SimpleJson.DeserializeObject<WordInfo>(response.Content);
                    if (data.results != null)
                    {
                        for (var i = 0; i < data.results.Length; i++)
                        {
                            var result = data.results[i];
                            db.Insert(new WordDefinitionEntry
                            {
                                WordId = entry.Id,
                                Definition = result.definition,
                                PartOfSpeech = result.partOfSpeech,
                                Synonyms = result.synonyms != null ? String.Join("\n", result.synonyms) : "",
                                InCategory = result.inCategory != null ? String.Join("\n", result.inCategory) : "",
                                TypeOf = result.typeOf != null ? String.Join("\n", result.typeOf) : "",
                                HasTypes = result.hasTypes != null ? String.Join("\n", result.hasTypes) : "",
                                HasMembers = result.hasMembers != null ? String.Join("\n", result.hasMembers) : "",
                                Derivation = result.derivation != null ? String.Join("\n", result.derivation) : "",
                                Examples = result.examples != null ? String.Join("\n", result.examples) : "",
                            });
                        }
                    }
                }
            }

        }
        
        private static string Roll(TableQuery<WordEntry> table, int value)
        {
            var random = new Random();

            var lengthFiltered = table.Where(w => w.Length == value);
            return lengthFiltered.ElementAt(random.Next(lengthFiltered.Count())).Word;
        }

        private static void Search(TableQuery<WordEntry> table, string word)
        {
            Console.WriteLine(word);
            var result = new HashSet<string>();
            
            var pattern = String.Concat(word.OrderBy(c => c));
            Console.WriteLine(pattern);
            var variants = 1 << pattern.Length;
            for (int i = 0; i < variants; i++)
            {
                var search = "";
                for (int j = 0; j < pattern.Length; j++)
                {
                    if (GetBit(i, j))
                    {
                        search += pattern[j];
                    }
                }

                if (search.Length >= 3)
                {
                    var query = table.Where(w => w.Normalized == search);
                    foreach (var entry in query)
                    {
                        result.Add(entry.Word);
                    }
                }
            }
            
            var sorted = result.OrderByDescending(w => w.Length);
            foreach (var entry in sorted)
            {
                Console.WriteLine(entry);
            }
        }
        
        public class WordEntry
        {
            [PrimaryKey, AutoIncrement]
            public int Id { get; set; }
            
            [Indexed, MaxLength(7)]
            public string Word { get; set; }
            
            [Indexed, MaxLength(7)]
            public string Normalized { get; set; }
            
            [Indexed]
            public int Length { get; set; }
            
            [Indexed]
            public float Frequency { get; set; }

            public override string ToString()
            {
                return $"{Word}";
            }
        }
        
        public class WordDefinitionEntry
        {
            [PrimaryKey, AutoIncrement]
            public int Id { get; set; }
            
            [Indexed]
            public int WordId { get; set; }
            
            public string Definition { get; set; }
            public string PartOfSpeech { get; set; }
            public string Synonyms { get; set; }
            public string InCategory { get; set; }
            public string TypeOf { get; set; }
            public string HasTypes { get; set; }
            public string HasMembers { get; set; }
            public string Derivation { get; set; }
            public string Examples { get; set; }
        }

        public class WordInfo
        {
            public string word;
            public WordDefinition[] results;
            public float frequency;

            public override string ToString()
            {
                return $"{word}:{frequency}";
            }
        }

        public class WordDefinition
        {
            public string definition;
            public string partOfSpeech;
            public string[] synonyms;
            public string[] inCategory;
            public string[] typeOf;
            public string[] hasTypes;
            public string[] hasMembers;
            public string[] derivation;
            public string[] examples;
        }
    }
}