namespace Infrastructure.Models
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel.DataAnnotations;
    using System.ComponentModel.DataAnnotations.Schema;
    using System.Net;
    using System.Text;
    using Infrastructure;

    public class WebPage : IEquatable<WebPage>      // , IComparable<WebPage>
    {
        //public enum OLDDownloadEnum : byte
        //{
        //    Ignore = 0,
        //    ToDownload,
        //    ReDownload,
        //    Redirected,             // ConsumeFrom should have ONE entry, Filespec should be NULL
        //    Downloaded
        //}
        public enum DownloadEnum : byte
        {
            Ignore = 0,
            Redirected,                                                     // ConsumeFrom should have ONE entry, Filespec should be NULL
            Downloaded,
            LoPriorityDownload,                                             // 3
            HiPriorityDownload = 63,                                        // valid range is 3 .. 63 for all WebPage rows
            Default = (LoPriorityDownload + HiPriorityDownload) / 2,        // 33 midpoint is default on INSERT
            BoostMin = Default + 1,                                         // 34 midpoint is default on INSERT
            BoostMax = (BoostMin + HiPriorityDownload) / 2,                 // 48 so boost is range 34 .. 48 (15 automatic notches)
                                                                            // so 49-63 only set manually after UI action (15 manual notches)
            LoReserved = 64,                                                // 64-255 reserved for future definition
            HiReserved = byte.MaxValue                                      // byte is unsigned 8-bit integer (ditto TSQL tinyint) range 0-255
        }

        public enum LocaliseEnum : byte
        {
            Ignore = 0,
            ToLocalise,
            Localised
        }
        public const int URLSIZE = 900 / 2,                         // UTF-8 unicode=2bytes/char * 450 = 900 [max index size for CI_WebPages]
            FILESIZE = 260,                                         // cf. MAX_PATH=260 for most NTFS volumes (W10 optionally more)
            DRAFTSIZE = FILESIZE / 2 - ContentTypeToExtn.EXTNSIZE;  // allow space for device:folders

        public WebPage() { }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2214:DoNotCallOverridableMethodsInConstructors")]
        public WebPage(string url, string draftFilespec = null, string filespec = null, DownloadEnum download = DownloadEnum.Ignore, LocaliseEnum localise = LocaliseEnum.Ignore)
        {
            Url = url;                                  // caller must present as absolute, e.g. by convert(base,relative)
            DraftFilespec = draftFilespec;
            Filespec = filespec;
            Download = download;
            Localise = localise;
        }

        [Key]
        public int PageId { get; set; }

        //[DatabaseGenerated(DatabaseGeneratedOption.Computed)]
        //public int? HostId { get; set; }

        [NotMapped]
        Uri Uri { get; set; }                           // reference so can discover individual members like Host

        string _url;                                    // backing field (saves having to invoke Uri.AbsoluteUri everytime)

        [Required]
        [StringLength(URLSIZE)]
        public string Url
        {
            get => _url;

            set
            {
                _url = value.Contains(Uri.SchemeDelimiter)
                    ? value
                    : Uri.UriSchemeHttp + Uri.SchemeDelimiter + value;
                if (_url.Length > URLSIZE)
                {
                    _url = SlimQP(_url);
                    //throw new InvalidOperationException($"url length({Uri.AbsoluteUri.Length}) exceeds max({URLSIZE}) [[{Uri.AbsoluteUri}]");
                    /*
                    "https://pathwright.imgix.net/https%3A%2F%2Fpathwright.imgix.net%2Fhttps%253A%252F%252Fcdn.filestackcontent.com%252Fapi%252Ffile%252Fz7fu32BwSZWBZUkWs7WR%253Fsignature%253D888b9ea3eb997a4d59215bfbe2983c636df3c7da0ff8c6f85811ff74c8982e34%2526policy%253DeyJjYWxsIjogWyJyZWFkIiwgInN0YXQiLCAiY29udmVydCJdLCAiZXhwaXJ5IjogNDYyMDM3NzAzMX0%25253D%3Ffit%3Dcrop%26ixlib%3Dpython-1.1.0%26w%3D500%26s%3Dc6e9844e60c8f6003fb2670004259423?fit=crop&amp;h=114&amp;ixlib=python-1.1.0&amp;w=114&amp;s=ad2749ee67694cc1c00868afcaa1c61f"
                    */
                }
                Uri = new Uri(_url, UriKind.Absolute);      // caller must present as absolute, e.g. by convert(base,relative)
                HashCode = _url.GetHashCode();                                  //  and cache the signature
                var numsegs = Uri.Segments.Length - 1;
                if (numsegs >= 0)
                {
                    if (Uri.Segments[numsegs] == "/" && --numsegs < 0)
                    {
                        return;
                    }
                    DraftFilespec = WebUtility.UrlDecode(Uri.Segments[numsegs]);
                }
            }
        }

        //[NotMapped]
        string _draftFilespec;

        [StringLength(FILESIZE)]
        public string DraftFilespec             // filename.extn ONLY (no dev/folder path)
        {
            get => _draftFilespec;
            set => _draftFilespec = Utils.TrimOrNull(Utils.FileExtnFix(value)); // strip off any device/folder part and remove any dodgy chars
        }

        [StringLength(FILESIZE)]
        public string Filespec { get; set; }

        /// <summary>
        ///     null means WebPages_trIU will determine extn then update from Host.IsXXX setting default
        /// </summary>
        /// <value>
        ///     see DownloadEnum
        /// </value>
        /// <remarks>
        ///     no default value in SQL metadata, and NULLable
        ///     on INSERT/UPDATE when NULL, WebPages_trIU trigger will set a NN value (e.g. DownloadEnum.Ignor) based on lookup to Hosts table
        /// </remarks>
        public DownloadEnum? Download { get; set; }

        /// <summary>
        ///     true means downloaded file should be localised [when most/all independent pages also downloaded]
        /// </summary>
        /// <value>
        ///     see LocaliseEnum
        /// </value>
        /// <remarks>
        ///     NOT NULL and the default value [DF_WebPages_Localise] in SQL metadata is 0
        /// </remarks>
        public LocaliseEnum Localise { get; set; } = LocaliseEnum.Ignore;

        //public virtual Host Host { get; set; }            // not implemented here (i.e. server-side only usage)

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
        public virtual ICollection<WebPage> ConsumeFrom { get; set; } = new HashSet<WebPage>();

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
        public virtual ICollection<WebPage> SupplyTo { get; set; } = new HashSet<WebPage>();

        //[NotMapped]
        int HashCode { get; set; }                                          // PERFORMANCE: write once read many

        #region IEquatable<WebPage> for Fetch operation
        public override bool Equals(object obj) => (obj is WebPage other) && Equals(other);
        public bool Equals(WebPage other) => HashCode == other.HashCode;    //  Url.Equals(other.Url) is expensive
        public override int GetHashCode() => HashCode;
        #endregion

        #region IComparable<WebPage> *unused*
        //public int CompareTo(WebPage other) => string.Compare(this.Url, other.Url, StringComparison.InvariantCultureIgnoreCase);
        #endregion

        /// <summary>
        ///     improve debugging UX to show ID and Url keys
        /// </summary>
        /// <returns>
        ///     string that will show up in VS debugger (not used otherwise by app)
        /// </returns>
        public override string ToString() => $"[{PageId}]: {Url}";

        /// <summary>
        ///     reduce QueryString so it fits the Url size max
        /// </summary>
        /// <param name="url">
        ///     oversize string of AbsoluteUri (Scheme, Authority, Path, Query)
        /// </param>
        /// <returns>
        ///     reduced string thats fits within max size
        /// </returns>
        /// <exception cref="InvalidOperationException">
        ///     if can't reduce QS, method will throw fatal exception (caller will have to ignore candidate URL)
        /// </exception>
        public static string SlimQP(string url)
        {
            const string AMP = "&", QUEST = "?";
            string[] DELIM = { AMP };

            if (url.Length <= URLSIZE)
            {
                return url;
            }
#pragma warning disable CA1307 // Specify StringComparison
            var qpstart = url.IndexOf(QUEST);                       // first "?" indicates start of queryparams (any subsequent is simple ASCII)
#pragma warning restore CA1307 // Specify StringComparison
            if (qpstart < 0 || qpstart > URLSIZE)            // either empty querystring or path itself already too long ?
            {
                //return url.Substring(0, WebPage.URLSIZE);           // yes. crude truncate at max width (may not be at word-break)
                throw new InvalidOperationException("Url too long (even after removing any queryparams)");  // abandon this particular link
            }
            var sb = new StringBuilder(url.Substring(0, qpstart));
            var qryprns = url.Substring(qpstart + 1).Split(DELIM, StringSplitOptions.RemoveEmptyEntries);
            var special = QUEST;                                    // 1st delimiter introducing QueryParams is a "?"
            for (var i = 0; i < qryprns.Length; i++)
            {
#pragma warning disable CA1307 // Specify StringComparison
                if (!qryprns[i].EndsWith("="))
#pragma warning restore CA1307 // Specify StringComparison
                {
                    var qpn = special + qryprns[i];
                    if (sb.Length + qpn.Length <= URLSIZE)
                    {
                        sb.Append(qpn);                             // may not be left-significant (e.g. ignore QP[i] but copy QP[i+1])
                        special = AMP;                              // subsequent delimiter for QS params is the "&"
                    }
                }
            }
            return sb.ToString();
        }
    }
}