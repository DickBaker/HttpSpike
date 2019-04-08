using System;
using System.Data.SqlTypes;
namespace NetUtils
{
    public static class UserDefinedFunctions
    {
        static readonly Uri HttpUri = new Uri("http://example.com");    // the example.com is just to satisfy IsAbsoluteUri dictates

        [Microsoft.SqlServer.Server.SqlFunction]
        public static SqlString UrlSplit(string url)
        {
            Uri myUri;              // outside the try-catch for debugging only
            try
            {
                myUri = new Uri(url, UriKind.RelativeOrAbsolute);
                if (!myUri.IsAbsoluteUri)       // has all of : scheme, authority, and path ?
                {
                    myUri = new Uri(HttpUri, "//" + url);               // prepend the "http://" to import scheme and/or authority
                }
                if (myUri.Scheme.StartsWith(Uri.UriSchemeHttp))         // "http" or "https"
                {
                    return new SqlString(myUri.Host);
                }
            }
#pragma warning disable RCS1075     // Avoid empty catch clause that catches System.Exception.
            catch (Exception)       // swallow any error
            { }
#pragma warning restore RCS1075     // Avoid empty catch clause that catches System.Exception.
            return null;            // refuse any non-http/https Url or on any error
        }

        /// <summary>
        ///     return extension (e.g. html) from specified input string
        /// </summary>
        /// <param name="fileExtn"
        ///     full/partial filespec (e.g. c:\dev\abc.def or c:\dev\abc. or c:\dev\abc)
        /// </param>
        /// <returns>
        ///     extension part (e.g. def or null)
        /// </returns>
        /// <remarks>
        ///     CLR function more efficient than TSQL that has no LastIndexOf function (so would need WHILE loop or REVERSE fn)
        /// </remarks>
        [Microsoft.SqlServer.Server.SqlFunction]
        public static SqlString udf_Extn(string fileExtn)
        {
            if (string.IsNullOrWhiteSpace(fileExtn))
            {
                return null;
            }
            var dot = fileExtn.LastIndexOf('.');            // determine where filename-extension delimiter comes (if any)
            return (dot < 0 || dot == fileExtn.Length - 1)
                ? null                                      // no extension (ditto null or blank extn)
                : new SqlString(fileExtn.Substring(dot + 1));
        }
    }
}