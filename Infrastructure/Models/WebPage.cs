namespace Infrastructure.Models
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel.DataAnnotations;
    using System.ComponentModel.DataAnnotations.Schema;
    using System.IO;
    using Infrastructure;

    public class WebPage : IEquatable<WebPage>, IComparable<WebPage>
    {
        public const int URLSIZE = 450,
            FILESIZE = 511;

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
        private Uri Uri { get; set; }                           // reference so can discover individual members like Host

        private string _url;                            // private backing field (saves having to invoke Uri.AbsoluteUri everytime)

        [Required]
        [StringLength(URLSIZE)]
        public string Url
        {
            get => _url;

            set
            {
                Uri = new Uri(value.Contains(Uri.SchemeDelimiter) ? value : Uri.UriSchemeHttp + Uri.SchemeDelimiter + value, UriKind.Absolute);  // caller must present as absolute, e.g. by convert(base,relative)
                while (Uri.AbsoluteUri.Length > URLSIZE)        //TODO: progressively remove fragment qs bits, UserInfo, Scheme
                {
                    throw new InvalidOperationException($"url length({Uri.AbsoluteUri.Length}) exceeds max({URLSIZE}) [[{Uri.AbsoluteUri}]");
                    /*
                    "https://pathwright.imgix.net/https%3A%2F%2Fpathwright.imgix.net%2Fhttps%253A%252F%252Fcdn.filestackcontent.com%252Fapi%252Ffile%252Fz7fu32BwSZWBZUkWs7WR%253Fsignature%253D888b9ea3eb997a4d59215bfbe2983c636df3c7da0ff8c6f85811ff74c8982e34%2526policy%253DeyJjYWxsIjogWyJyZWFkIiwgInN0YXQiLCAiY29udmVydCJdLCAiZXhwaXJ5IjogNDYyMDM3NzAzMX0%25253D%3Ffit%3Dcrop%26ixlib%3Dpython-1.1.0%26w%3D500%26s%3Dc6e9844e60c8f6003fb2670004259423?fit=crop&amp;h=114&amp;ixlib=python-1.1.0&amp;w=114&amp;s=ad2749ee67694cc1c00868afcaa1c61f"
                    */
                }
                _url = Utils.NoTrailSlash(Uri.AbsoluteUri.ToLowerInvariant());   // PERFORMANCE: do this once (immutable and is read often)
                HashCode = _url.GetHashCode();                          //  and cache the signature
            }
        }

        //[NotMapped]
        private string _draftFilespec;

        [StringLength(FILESIZE)]
        public string DraftFilespec             // filename.extn ONLY (no dev/folder path) - if blank/null will read as "unknown.txt"
        {
            get =>
                _draftFilespec ??
                (_draftFilespec = Utils.MakeValid(Utils.FilespecLastSegment(Uri.Segments[Uri.Segments.Length - 1])));   // includes .Trim()

            set
            {
                _draftFilespec = Utils.TrimOrNull(value);
                if (_draftFilespec != null)
                {
                    _draftFilespec = Path.GetFileName(_draftFilespec);      // strip off any device/folder part
                }
            }
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
        private int HashCode { get; set; }                                          // PERFORMANCE: write once read many

        #region IEquatable<WebPage> for Fetch operation
        public override bool Equals(object obj) => (obj is WebPage other) && Equals(other);
        public bool Equals(WebPage other) => HashCode == other.HashCode;    //  Url.Equals(other.Url) is expensive
        public override int GetHashCode() => HashCode;
        #endregion

        #region IComparable<WebPage>
        public int CompareTo(WebPage other) => throw new NotImplementedException();
        #endregion
    }
}
