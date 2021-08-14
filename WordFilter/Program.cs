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

            using (StreamReader streamReader = new StreamReader(input))
            {
                string line = null;

                while ((line = streamReader.ReadLine()) != null)
                {
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

                    if (response.IsSuccessful)
                    {
                        var data = SimpleJson.DeserializeObject<WordInfo>(response.Content);
                        if (data.results != null)
                        {
                            entry.Frequency = data.frequency;
                            
                            db.Insert(entry);
                            Console.WriteLine(entry.Id);

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