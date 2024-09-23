using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WLL_Tracker.Logs
{
    public class LogEvent
    {
        public string Id { get; set; }
        public string Author { get; set; }
        public DateTime Updated { get; set; }
        public List<string> Changes { get; set; }

        public LogEvent(string id, string author, DateTime updated, List<string> changes)
        {
            Id = id;
            Author = author;
            Updated = updated;
            Changes = changes;
        }

        public async Task SaveLog()
        {
            string path = "./log.txt";

            if (!File.Exists(path))
            {
                File.Create(path);
                TextWriter tw = new StreamWriter(path);
                tw.Close();
            }
            else if (File.Exists(path))
            {
                using (var sw = new StreamWriter(path, true))
                {
                    sw.WriteLine(JsonConvert.SerializeObject(this));
                }
            }
        }

    }
}
