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
using System.Threading;

namespace GPWAca
{
    class Program
    {

        private static readonly HttpClient client = new HttpClient();
        private static HtmlWeb htmlWeb = new HtmlWeb();
        public static int requestDelay = 200;
        public static int linkBatchSize = 5000;
        public static int docsPerBatch = 100;
        public static string uri = "http://150.254.78.133:8983/solr/isi/update/json/docs?commit=true";
        /// <summary>
        /// if not > than 0 gets all available documents 
        /// </summary>
        public static int maxDocuments = 0;


        static void Main(string[] args)
        {

            if (linkBatchSize>5000)
            {
                Console.WriteLine("Link batch size can not be grater than 5000, using max value.");
                linkBatchSize = 5000;
            }

            Console.WriteLine("Getting all available urls");
            List<string> links = GetLinks();

            Console.WriteLine($"[{DateTime.Now.TimeOfDay}] Creating {links.Count} documents.");
            Task<List<Announcement>> task = Task.Run(() => GetAsync(links));
            List<Announcement> result = task.Result;

            Console.WriteLine("Creating batch files");
            List<List<Announcement>> batches = new List<List<Announcement>>();
            batches = CreateBatches(result, docsPerBatch);

            Console.WriteLine("Creating output files");
            CreateOutputFiles(batches);

            Console.WriteLine($"Created {batches.Count} json files.");

            Console.WriteLine("Crawler finished. Run \"POST.bat\" to post json files.");

            Console.WriteLine("Press any key to close...");
            Console.ReadKey();
        }

        /// <summary>
        /// Gets available links
        /// </summary>
        /// <returns></returns>
        public static List<string> GetLinks()
        {
            int limit = linkBatchSize;
            int offset = 0;
            List<string> links = new List<string>();
            List<string> newLinks = new List<string>();

            while (true)
            {
                newLinks = GetLinksAsync(offset, limit).Result;
                if (newLinks.Count == 0)
                {
                    break;
                }
                Console.WriteLine($"Fetched {newLinks.Count} new links");
                links.AddRange(newLinks);
                offset += limit;
            }
            if (maxDocuments > 0)
            {
                links = links.Take(maxDocuments).ToList();
            }
            return links;
        }

        /// <summary>
        /// Gets collection of announcements
        /// </summary>
        /// <returns></returns>
        public static async Task<List<Announcement>> GetAsync(List<string> links)
        {

            var announcements = new List<Announcement>();

            foreach (var link in links)
            {
                var ann = GetAnnouncement(link);
                if (!string.IsNullOrEmpty(ann.content) && !ann.content.Equals("-"))
                {
                    announcements.Add(ann);
                }
                else
                {
                    Console.WriteLine($"[FAILURE] url: {ann.url}");
                }
                Thread.Sleep(requestDelay);
            }

            // Multiple request in short time periods are blocked by host
            //Parallel.ForEach(links, link =>
            //{
            //    var ann = GetAnnouncement(link);
            //    if (!string.IsNullOrEmpty(ann.content) && !ann.content.Equals("-"))
            //    {
            //        announcements.Add(ann);
            //    }
            //    else
            //    {
            //        Console.WriteLine($"!!FAIL {ann.url}");
            //    }
            //    Thread.Sleep(requestDelay);
            //});


            return announcements;
        }


        #region private methods

        private static void CreateOutputFiles(List<List<Announcement>> batches)
        {
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
        }

        private static List<List<Announcement>> CreateBatches(List<Announcement> announcements, int batchSize)
        {
            var batches = new List<List<Announcement>>();
            int maxNum = announcements.Count;
            int offset = 0;
            while (true)
            {
                batches.Add(announcements.Skip(offset).Take(batchSize).ToList());

                offset += batchSize;
                maxNum -= batchSize;
                if (maxNum <= 0)
                {
                    break;
                }
            }
            return batches;
        }

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

            var response = client.PostAsync(url, content).Result;

            var responseString = response.Content.ReadAsStringAsync().Result;

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
            Console.WriteLine($"[{DateTime.Now.TimeOfDay}] Processing url: {url}");
            htmlWeb.OverrideEncoding = Encoding.UTF8;
            var doc = new HtmlDocument();
            try
            {
                var data = new MyWebClient().DownloadString(url);
                doc.LoadHtml(data);
            }
            catch
            {
                Console.WriteLine($"[WARN] Failed to load url: {url}");
                return new Announcement { url=url};
            }

            string title = "Undefined document";
            string date = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ssZ");
            string content = "-";
            string id = "434589"+url.Substring(url.IndexOf("id=")+3);
            try
            {
                title = doc.DocumentNode.SelectNodes("/html/body/section[2]/div[2]/div/div/h1")[0]
                    .InnerText
                    .Replace("\t", " ")
                    .Replace("\n", Environment.NewLine)
                    .Replace("\r", "")
                    .Replace("&oacute;", "ó").Replace("\"","''")
                    .Replace("&sect;", "§")
                    .Replace("&oacute;", "ó");
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
                    .Replace("\r", "").Replace(" ","")
                    .Replace("&sect;", "§")
                    .Replace("&oacute;", "ó");
                if (date.Length<15)
                {
                    date=date.Insert(10, "0");
                }
                date = DateTime.ParseExact(date,"dd-MM-yyyyHH:mm",CultureInfo.InvariantCulture).ToString("yyyy-MM-ddTHH:mm:ssZ");
            }
            catch
            {
                Console.WriteLine($"[WARN] Failed to get date from url: {url}");
                try
                {
                    Console.WriteLine($"[WARN] Preprocessed date: {doc.DocumentNode.SelectNodes("/html/body/section[2]/div[2]/div/div/span")[0].InnerText}");
                }
                catch { }
                date = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ssZ");
            }
            try
            {
                // text not in <p> tag
                content = doc.DocumentNode.SelectNodes("/html/body/section[2]/div[2]/div/div")[0]
                    .InnerText
                    .Trim()
                    .Replace("\t", "")
                    .Replace("\n", Environment.NewLine)
                    .Replace("\r", "")
                    .Replace(".page-title {display:none;}","")
                    .Replace("&sect;", "§")
                    .Replace("&oacute;","ó");

                content += Environment.NewLine;
                // concat text in <p> tags
                var innerContent = doc.DocumentNode.SelectNodes("/html/body/section[2]/div[2]/div/div/p");
                foreach (var p in innerContent[0].ChildNodes.Where(cn => cn.Name == "p"))
                {

                    content += p.InnerText
                        .Trim()
                        .Replace("\t", "")
                        .Replace("\n", Environment.NewLine)
                        .Replace("\r", "")
                        .Replace(".page-title {display:none;}", "")
                        .Replace("&sect;", "§")
                        .Replace("&oacute;", "ó");

                    content += Environment.NewLine;
                }
                content = content.Replace("\n\n", Environment.NewLine);
                content = content.Replace("\n\r\n", "");
            }
            catch
            {
            }


            return new Announcement { content = content, duration_end = date,duration_start=date, title = title, url = url,id = id+"-1", iid=id  };
        }

        class MyWebClient : WebClient
        {
            protected override WebRequest GetWebRequest(Uri address)
            {
                HttpWebRequest request = base.GetWebRequest(address) as HttpWebRequest;

                request.Timeout = 20 * 60 * 1000;
                request.AutomaticDecompression = DecompressionMethods.Deflate | DecompressionMethods.GZip;
                return request;
            }
        }
            #endregion


        }

    }
