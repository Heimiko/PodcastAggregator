using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using System.Xml;
using System.IO;
using System.Diagnostics;
using System.Net;
using System.Net.Mail;

namespace PodcastAggregator
{
    public class Aggregator
    {
        const string DownloadDir = "E:\\Downloads\\! Podcasts";

        public void ProcessOPML(string FileName, bool DownloadEnclosures)
        {
            // load OPML file
            if (!File.Exists(FileName))
                return; // got no file to load

            DeleteOldFiles();

            try
            {
                XmlDocument OPML = new XmlDocument();
                OPML.Load(FileName);

                XmlNodeList outlines = OPML.GetElementsByTagName("outline");
                foreach (XmlNode outline in outlines)
                    ProcessFeed(outline, DownloadEnclosures);
            }
            catch { /* something went wrong ...  don't care */ }
        }

        void DeleteOldFiles()
        {
            try
            {
                //first, delete all old files (if any)
                foreach (string existingFile in Directory.GetFiles(DownloadDir))
                {
                    try
                    {
                        // if file is older than 2 weeks, delete it!
                        DateTime LastMod = File.GetLastWriteTime(existingFile);
                        if (LastMod.AddDays(14) < DateTime.Now)
                            File.Delete(existingFile);
                    }
                    catch { /* couldn't delete file */ }
                }
            }
            catch { /* GetFiles failed */ }
        }

        void ProcessFeed(XmlNode outline, bool DownloadEnclosures)
        {
            XmlAttribute text = outline.Attributes["text"];
            XmlAttribute title = outline.Attributes["title"];
            XmlAttribute xmlUrl = outline.Attributes["xmlUrl"];

            if (title == null || xmlUrl == null || text == null)
                return; // no valid attributes found

            // load previously processed items from file
            string ProcessedFileName = "History for " + RemoveInvalidFileChars(text.Value) + ".txt";
            ProcessedFileName = Path.Combine(Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName), ProcessedFileName);
            
            List<string> PreviouslyProcessedHashes = new List<string>();
            List<string> NewlyProcessedHashes = new List<string>();
            string strLine;
            if (File.Exists(ProcessedFileName))
                using (FileStream fs = File.OpenRead(ProcessedFileName))
                    using (StreamReader reader = new StreamReader(fs))
                        while((strLine = reader.ReadLine()) != null)
                            PreviouslyProcessedHashes.Add(strLine);
                     
            try
            {
                switch (XmlHelper.GetAttributeValue(outline, "type", "rss").ToLower())
                {
                    case "rss": // RSS feed
                    default:

                        XmlDocument RSS = new XmlDocument();
                        RSS.Load(xmlUrl.Value);
                        XmlNodeList Items = RSS.GetElementsByTagName("item");
                        foreach (XmlNode item in Items)
                            ProcessRssItem(outline, item, DownloadEnclosures, PreviouslyProcessedHashes, NewlyProcessedHashes); // process item, then always update lastDownload value

                        break;
                }
            }
            catch
            {
                /* something went wrong ...  don't care */
            }

            try
            {
                // save all found items in the stream to its history file
                File.Delete(ProcessedFileName); // always create a new empty file
                using (FileStream fs = File.OpenWrite(ProcessedFileName))
                    using (StreamWriter writer = new StreamWriter(fs))
                        foreach (string NewItem in NewlyProcessedHashes)
                            writer.WriteLine(NewItem);
            }
            catch
            {

            }
        }

        void ProcessRssItem(XmlNode outline, XmlNode item, bool DownloadEnclosure, List<string> PreviouslyProcessedHashes, List<string> NewlyProcessedHashes)
        {
            string Enclosure = null, Title = null, FileType = null, Description = null, Author = string.Empty, Link = null, Guid = null, Summary = null, Subtitle = null;
            long FileLength = 0;

            foreach (XmlNode ChildNode in item.ChildNodes)
            {
                switch (ChildNode.Name)
                {
                    case "guid":
                        if (Guid == null) // sometimes, there's multiple GUID's, just use the 1st one
                            Guid = ChildNode.InnerText;
                        break;

                    case "title":
                        Title = ChildNode.InnerText;
                        break;

                    case "description":                     // most usually, this has HTML code in it
                        Description = ChildNode.InnerText;
                        break;

                    case "itunes:summary":                  // this is the best description without HTML stuff in it
                        Summary = ChildNode.InnerText;
                        break;

                    case "itunes:subtitle":
                        Subtitle = ChildNode.InnerText;
                        break;

                    case "author":
                        Author = ChildNode.InnerText;
                        break;

                    case "link":
                        Link = ChildNode.InnerText;
                        break;

                    case "enclosure":
                        bool TakeThis = false;
                        long ThisLength = 0;
                        string ThisType = XmlHelper.GetAttributeValue(ChildNode, "type");
                        long.TryParse(XmlHelper.GetAttributeValue(ChildNode, "length"), out FileLength);

                        if (Enclosure == null || FileType == null)
                            TakeThis = true;
                        else if (!FileType.Contains("video") && ThisType.Contains("video")) // we want video!
                            TakeThis = true;
                        else if (!FileType.Contains("mp4") && ThisType.Contains("mp4")) // we prefer mp4 (since you can ID3 tag this)
                            TakeThis = true;
                        else if (ThisLength > FileLength && FileType.Contains("video") == ThisType.Contains("video") && FileType.Contains("mp4") == ThisType.Contains("mp4"))
                            TakeThis = true;

                        if (TakeThis)
                        {
                            Enclosure = XmlHelper.GetAttributeValue(ChildNode, "url");
                            FileLength = ThisLength;
                            FileType = ThisType;
                        }
                        break;
                }
            }

            string strItem = Guid ?? Enclosure ?? Link ?? Title;
            NewlyProcessedHashes.Add(strItem);
            if (PreviouslyProcessedHashes.Contains(strItem))
                return; // if we already processed this item, we don't wanna do that again
            if (PreviouslyProcessedHashes.Count == 0 && NewlyProcessedHashes.Count > 1)
                return; // when we haven't ever processed any items before, we only want a single download to occur

            if (Title == null)
                return; //there's no title?? weird ....

            string DownloadURL = Enclosure ?? Link;
            string Body = "<h2 style='color:blue;'><a href='" + (Link ?? string.Empty) + "'>" + Title + "</a></h2><br/>";
            Body += Description ?? Summary ?? Subtitle;
            Body.Replace("<img ", "<img style='width:100%; height:auto; border:none;' "); // fix images getting out of screen

            string Subject = XmlHelper.GetAttributeValue(outline, "title", "");
            if (Subject.IndexOf('%') < 0)
                Subject += " - " + Title;  // default file name format
            else
                Subject = Subject.Replace("%T", Title).Replace("%A", Author);

            if (DownloadEnclosure && DownloadURL != null && DownloadURL.Length > 0)
            {
                Uri DownloadURI = new Uri(DownloadURL);

                string DownloadFileName = Subject; // start off with subject name

                // replace colon with dash (occurs often in titles, we want to display this nicely)
                DownloadFileName = DownloadFileName.Replace(": ", " - ").Replace(':', '-');

                //remove illigal characters (replace by spaces)
                DownloadFileName = RemoveInvalidFileChars(DownloadFileName);

                // insert full path into DownloadFileName
                DownloadFileName = DownloadDir + DownloadFileName;

#if !DEBUG
                if (DownloadURI.Host.Contains("youtube.com")) // youtube URL? then we need to download using our "special" method
                {
                    //Using Youtube downloader: http://rg3.github.com/youtube-dl/download.html
                    // command line parameter info: https://github.com/rg3/youtube-dl
                    ProcessStartInfo YoutubeDL = new ProcessStartInfo("youtube-dl.exe");
                    //YoutubeDL.WorkingDirectory = Path.GetDirectoryName(DownloadFileName);
                    YoutubeDL.Arguments = "-o \"" + DownloadFileName + ".%(ext)s\" " + DownloadURL;
                    YoutubeDL.WindowStyle = ProcessWindowStyle.Hidden;
                    YoutubeDL.UseShellExecute = false;
                    YoutubeDL.CreateNoWindow = true;
                    Process.Start(YoutubeDL).WaitForExit();
                }
                else // normal file download
                {
                    string ext = Path.GetExtension(DownloadURI.AbsolutePath);
                    if (ext == null || ext.Length <= 1)
                        ext = ".mp4";
                    
                    DownloadFileName += ext;

                    File.Delete(DownloadFileName); // just in case this exists from a previous (failed) download
                    File.Delete(DownloadFileName + ".part"); // just in case this exists from a previous (failed) download

                    WebClient webClient = new WebClient();
                    webClient.DownloadFile(DownloadURI, DownloadFileName + ".part"); // blocks thread until download is complete (which is what we want!)

                    File.Move(DownloadFileName + ".part", DownloadFileName); // download is done -> rename
                }
#endif

                SendNotificationEmail(Subject, Body);
            }

            SendNewsFeed(Subject, Body);
        }

        string RemoveInvalidFileChars(string Name, char ReplaceIllegalWithChar = ' ')
        {
            //remove illigal characters (replace by spaces)
            foreach (char InvalidChar in Path.GetInvalidFileNameChars())
                Name = Name.Replace(InvalidChar, ReplaceIllegalWithChar); // replace invalid chars with spaces

            return Name;
        }

        void SendNotificationEmail(string Title, string Body = null)
        {
            MailMessage msg = new MailMessage();
            msg.IsBodyHtml = true;
            msg.To.Add("anton@heimiko.com");
            msg.From = new MailAddress("server@heimiko.com");
            msg.Subject = "Podcast Downloaded: " + Title;

            msg.Body = "<html>" + Body ?? Title + "</html>";

            SmtpClient client = new SmtpClient("hkserver.heimiko.com", 25);
            client.Send(msg);
        }

        void SendNewsFeed(string Title, string Body = null)
        {
            MailMessage msg = new MailMessage();
            msg.IsBodyHtml = true;
            msg.To.Add("anton.heimiko@gmail.com");
            msg.From = new MailAddress("server@heimiko.com", "test name");
            msg.Subject = Title;

            msg.Body = "<html>" + Body ?? Title + "</html>";
            
            SmtpClient client = new SmtpClient("hkserver.heimiko.com", 25);
            client.Send(msg);
        }

        void SendErrorEmail()
        {
            //TODO
        }
    }

    public static class XmlHelper
    {
        public static string GetAttributeValue(XmlNode node, string AttributeName, string Default = "")
        {
            XmlAttribute Attribute = node.Attributes[AttributeName];
            return Attribute != null ? Attribute.Value ?? Default : Default;
        }
    }
}
