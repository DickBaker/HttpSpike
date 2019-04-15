namespace Infrastructure.Models
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel.DataAnnotations;
    using System.ComponentModel.DataAnnotations.Schema;
    using System.Text;
    using Infrastructure;

    public class WebPage : IEquatable<WebPage>, IComparable<WebPage>
    {
        public const int URLSIZE = 450,
            FILESIZE = 511;     // but cf. MAX_PATH=260 for most NTFS volumes (W10 optionally more)

        public WebPage() { }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2214:DoNotCallOverridableMethodsInConstructors")]
        public WebPage(string url, string draftFilespec = null, string filespec = null, bool? needDownload = null)
        {
            Url = url;                                  // caller must present as absolute, e.g. by convert(base,relative)
            DraftFilespec = draftFilespec;
            Filespec = filespec;
            NeedDownload = needDownload;
        }

        [Key]
        public int PageId { get; set; }

        [DatabaseGenerated(DatabaseGeneratedOption.Computed)]
        public int? HostId { get; set; }

        [NotMapped]
        Uri Uri { get; set; }                           // reference so can discover individual members like Host

        string _url;                                    // backing field (saves having to invoke Uri.AbsoluteUri and NoTrailSlash everytime)

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
                //_url = Utils.NoTrailSlash(Uri.AbsoluteUri.ToLowerInvariant());  // PERFORMANCE: do this once (immutable and is read often)
                HashCode = _url.GetHashCode();                                  //  and cache the signature
                var numsegs = Uri.Segments.Length - 1;
                if (numsegs >= 0)
                {
                    if (Uri.Segments[numsegs] == "/" && --numsegs < 0)
                    {
                        return;
                    }
                    DraftFilespec = Uri.Segments[numsegs];
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
        /// null means WebPages_trIU will determine extn then update from Host.IsXXX setting default
        /// </summary>
        public bool? NeedDownload { get; set; }

        //public virtual Host Host { get; set; }

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

        #region IComparable<WebPage>
        public int CompareTo(WebPage other) => string.Compare(this.Url, other.Url, StringComparison.InvariantCultureIgnoreCase);
        #endregion

        public static string SlimQP(string url)
        {
            const string AMP = "&amp;", QUEST = "?";
            string[] DELIM = { AMP };

            if (url.Length <= URLSIZE)
            {
                return url;
            }
#pragma warning disable CA1307 // Specify StringComparison
            var qpstart = url.IndexOf(QUEST);                       // first "?" indicates start of queryparams (any subsequent is simple ASCII)
#pragma warning restore CA1307 // Specify StringComparison
            if (qpstart < 0)
            {
                return url.Substring(0, WebPage.URLSIZE);           // crude truncate at max width (may not be at word-break)
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
                    if (sb.Length + qpn.Length <= WebPage.URLSIZE)
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
