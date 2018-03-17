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
using SolrNet;
using System.Net;
using System.Net.Http.Headers;

namespace GPWAca
{
    class Program
    {
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
                batches.Add(result.Skip(offset).Take(5).ToList());

                offset += 5;
                maxNum -= 5;
                if (maxNum<=0)
                {
                    break;
                }
            }


            //create post.sh
            string postFilePath = Path.Combine(System.Environment.CurrentDirectory, $"POST.sh");
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
                    //update post.sh
                    sw.WriteLine($"curl '{uri}' --data-binary @{jsonPath} -H 'Content-type:application/json'");
                    sw.WriteLine("");

                    i++;
                }
            }
            Console.WriteLine($"Saved {batches.Count} documents.");

            Console.WriteLine("Crawler done.");

        
            //var content = new StringContent(jsonString, Encoding.UTF8, "application/json");
            //var response = client.PostAsync("http://150.254.78.133:8983/solr/isi/update/json/docs?commit=true", content).Result;


            Console.WriteLine("Press any key to proceed...");
            Console.ReadKey();
        }

        private static readonly HttpClient client = new HttpClient();
        private static HtmlWeb htmlWeb = new HtmlWeb();

        public static async Task<List<Announcement>> GetAsync()
        {

            var announcements = new List<Announcement>();

            int limit = 5000;
            int offset = 0;
            List<string> links = new List<string>();
            List<string> newLinks = new List<string>();

            //Console.WriteLine($"Limit: {limit}");
            while (true)
            {
                //Console.WriteLine($"Offset: {offset}");
                newLinks = await GetLinksAsync(offset, limit);
                if (/*links.Count>1 ||*/ newLinks.Count == 0)
                {
                    break;
                }
                Console.WriteLine($"Fetched {newLinks.Count} new records");
                links.AddRange(newLinks);
                offset += limit;
            }

            links = links.Take(20).ToList();
            Parallel.ForEach(links, link => announcements.Add(GetAnnouncement(link)));

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
            Console.WriteLine($"Fetching data for url: {url}");
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
            string date = "0000-00-00T00:00:00Z";
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
                    .Replace("\r", "")
                    .Replace(" ","T")+":00Z";
                date = date.Substring(6, 4) + "-" + date.Substring(3, 2) + "-" + date.Substring(0, 2) + date.Substring(10);
            }
            catch
            {
            }
            try
            {
                content = doc.DocumentNode.SelectNodes("/html/body/section[2]/div[2]/div/div")[0]
                    .InnerText
                    .Replace("\t", "")
                    .Replace("\n", "")
                    .Replace("\r", "").Replace("\"", "''")
                    .Replace("&oacute;","ó");
                var innerContent = doc.DocumentNode.SelectNodes("/html/body/section[2]/div[2]/div/div");
                foreach (var p in innerContent[0].ChildNodes.Where(cn => cn.Name == "p"))
                {
                    content += p.InnerText
                        .Replace("\t", "")
                        .Replace("\n", "")
                        .Replace("\r", "").Replace("\"", "''")
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
