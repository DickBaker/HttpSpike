using System;

namespace DownloadExtractLib.Messages
{
    class AddMessage
    {
        public AddMessage(string url)
        {
            MyUri = new Uri(url.ToLower());         // standardise on lowercase
        }

        public Uri MyUri { get; }
        public string Url => MyUri.ToString();
        public string FileName => MyUri.PathAndQuery;
    }
}