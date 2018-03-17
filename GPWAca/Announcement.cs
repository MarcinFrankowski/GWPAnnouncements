using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace GPWAca
{
    public class Announcement
    {

        public string id { get; set; }

        public string iid { get; set; }

        public string duration_start { get; set; }

        public string duration_end { get; set; }

        public string lname = "Komunikaty GPW";

        /// <summary>
        /// Announcement Title
        /// </summary>
        public string title { get; set; }

        /// <summary>
        /// Announcement Content
        /// </summary>
        public string content { get; set; }

        /// <summary>
        /// Announcement Url
        /// </summary>
        public string url { get; set; }
    }
}
