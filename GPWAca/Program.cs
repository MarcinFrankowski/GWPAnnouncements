using HtmlAgilityPack;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft;
using Newtonsoft.Json;
using System.IO;
using System.Net;
using System.Net.Http.Headers;
using System.Globalization;

namespace GPWAca
{
    class Program
    {

        private static readonly HttpClient client = new HttpClient();
        private static HtmlWeb htmlWeb = new HtmlWeb();

        static void Main(string[] args)
        {
            string uri = "http://150.254.78.133:8983/solr/isi/update/json/docs?commit=true";
            Console.WriteLine("Getting all available links");
            Task<List<Announcement>> task = Task.Run(() => GetAsync());
            List<Announcement> result = task.Result;

            List<List<Announcement>> batches = new List<List<Announcement>>();    
           
            int maxNum = result.Count;
            int offset = 0;
            while (true)
            {
                batches.Add(result.Skip(offset).Take(100).ToList());

                offset += 100;
                maxNum -= 100;
                if (maxNum<=0)
                {
                    break;
                }
            }

            string postFilePath = Path.Combine(System.Environment.CurrentDirectory, $"POST.bat");
            System.IO.File.WriteAllText(postFilePath, "");
            using (StreamWriter sw = File.AppendText(postFilePath))
            {

                string json;
                int i = 0;
                string jsonPath;
                foreach (var batch in batches)
                {
                    json = JsonConvert.SerializeObject(batch);
                    jsonPath = Path.Combine(System.Environment.CurrentDirectory, $"json_{i}.json");
                    System.IO.File.WriteAllText(jsonPath, json);

                    
                    sw.WriteLine($"curl {uri} -X POST --data-binary @{jsonPath} -H \"Content-type:application/json\"");
                    sw.WriteLine("");

                    i++;
                }
                sw.WriteLine($"pause");

            }
            Console.WriteLine($"Created {batches.Count} batch files.");

            Console.WriteLine("Crawler finished. Run \"POST.sh\" to post json files.");

            Console.WriteLine("Press any key to close...");
            Console.ReadKey();
        }


        public static async Task<List<Announcement>> GetAsync()
        {

            var announcements = new List<Announcement>();

            int limit = 1000;
            int offset = 0;
            List<string> links = new List<string>();
            List<string> newLinks = new List<string>();

            while (true)
            {
                newLinks = await GetLinksAsync(offset, limit);
                if (links.Count>1000 || newLinks.Count == 0)
                {
                    break;
                }
                Console.WriteLine($"Fetched {newLinks.Count} new records");
                links.AddRange(newLinks);
                offset += limit;
            }

            links = links.Take(1000).ToList();
            Parallel.ForEach(links, link =>
            {
                var ann = GetAnnouncement(link);
                if (!string.IsNullOrEmpty(ann.content))
                {
                    announcements.Add(ann);
                }
            });

            return announcements;
        }


        #region private methods
        /// <summary>
        /// Gets links to announcement's page
        /// </summary>
        /// <param name="offset"></param>
        /// <param name="limit"></param>
        /// <returns></returns>
        private static async Task<List<string>> GetLinksAsync(int offset, int limit)
        {
            List<string> links = new List<string>();
            string baseUrl = "https://www.gpw.pl";

            string url = "https://www.gpw.pl/ajaxindex.php";
            var request = new Dictionary<string, string>
            {
                {"action", "CMNews" },
                {"start", "ajaxList" },
                {"page_iterator_active", "false" },
                {"page", "komunikaty-i-uchwaly-gpw" },
                {"target", "main_01" },
                {"cmng_id", "2,8,9,11" },
                {"limit", $"{limit}" },
                {"offset", $"{offset}" }
            };

            var content = new FormUrlEncodedContent(request);

            var response = await client.PostAsync(url, content);

            var responseString = await response.Content.ReadAsStringAsync();

            List<string> LiElements = responseString.Split(new string[] { "<a href=\"" }, StringSplitOptions.None).Skip(1).ToList();

            if (LiElements.Count == 0)
            {
                return new List<string>();
            }

            foreach (var href in LiElements)
            {
                try
                {
                    links.Add(baseUrl + href.Substring(0, href.IndexOf("&amp;title")).Replace("amp;", ""));
                }
                catch
                {
                }
            }

            return links;
        }
        /// <summary>
        /// Gets announcement's data
        /// </summary>
        /// <param name="url"></param>
        /// <returns></returns>
        private static Announcement GetAnnouncement(string url)
        {
            Console.WriteLine($"Processing url: {url}");
            htmlWeb.OverrideEncoding = Encoding.UTF8;
            var doc = new HtmlDocument();
            try
            {
                doc = htmlWeb.Load(url);
            }
            catch
            {
                return new Announcement();
            }

            string title = "-";
            string date = DateTime.Now.ToString("yyyy-MM-ddThh:mm:ssZ");
            string content = "-";
            string id = "434589"+url.Substring(url.IndexOf("id=")+3);
            try
            {
                title = doc.DocumentNode.SelectNodes("/html/body/section[2]/div[2]/div/div/h1")[0]
                    .InnerText
                    .Replace("\t", "")
                    .Replace("\n", "")
                    .Replace("\r", "")
                    .Replace("&oacute;", "ó").Replace("\"","''");
            }
            catch
            {
            }
            try
            {
                date = doc.DocumentNode.SelectNodes("/html/body/section[2]/div[2]/div/div/span")[0]
                    .InnerText
                    .Replace("\t", "")
                    .Replace("\n", "")
                    .Replace("\r", "").Trim();
                date = DateTime.ParseExact(date,"dd-MM-yyyyhh:mm",CultureInfo.InvariantCulture).ToString("yyyy-MM-ddThh:mm:ssZ");
                date = date.Substring(6, 4) + "-" + date.Substring(3, 2) + "-" + date.Substring(0, 2) + date.Substring(10);
            }
            catch
            {
            }
            try
            {
                content = doc.DocumentNode.SelectNodes("/html/body/section[2]/div[2]/div/div")[0]
                    .InnerText
                    //.Replace("\t", "")
                    //.Replace("\n", "")
                    //.Replace("\r", "").Replace("\"", "''")
                    .Replace("&oacute;","ó");
                var innerContent = doc.DocumentNode.SelectNodes("/html/body/section[2]/div[2]/div/div");
                foreach (var p in innerContent[0].ChildNodes.Where(cn => cn.Name == "p"))
                {
                    content += p.InnerText
                        //.Replace("\t", "")
                        //.Replace("\n", "")
                        //.Replace("\r", "").Replace("\"", "''")
                        .Replace("&oacute;", "ó");
                }
            }
            catch
            {
            }


            return new Announcement { content = content, duration_end = date,duration_start=date, title = title, url = url,id = id+"-1", iid=id  };
        }
        #endregion


    }

}
