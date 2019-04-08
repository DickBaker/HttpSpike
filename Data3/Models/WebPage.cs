namespace Infrastructure.Models
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel.DataAnnotations;
    using System.ComponentModel.DataAnnotations.Schema;

    public class WebPage : IEquatable<WebPage>
    {
        public WebPage() { }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2214:DoNotCallOverridableMethodsInConstructors")]
        public WebPage(string url, string draftFilespec = null, string filespec = null)
        {
            Url = url;                                  // caller must present as absolute, e.g. by convert(base,relative)
            DraftFilespec = draftFilespec;
            Filespec = filespec;
        }

        [Key]
        public int PageId { get; set; }

        [DatabaseGenerated(DatabaseGeneratedOption.Computed)]
        public int? HostId { get; set; }

        [NotMapped]
        Uri Uri { get; set; }                           // reference so can discover individual members like Host

        private string _url;                            // private backing field (saves having to invoke Uri.AbsoluteUri everytime)

        [Required]
        [StringLength(450)]
        public string Url
        {
            get => _url;

            set
            {
                Uri = new Uri(value.Contains(Uri.SchemeDelimiter) ? value : Uri.UriSchemeHttp + Uri.SchemeDelimiter + value, UriKind.Absolute);  // caller must present as absolute, e.g. by convert(base,relative)
                while (Uri.AbsoluteUri.Length > 450)
                {
                    throw new InvalidOperationException($"url length({Uri.AbsoluteUri.Length}) exceeds max(450) [[{Uri.AbsoluteUri}]");
                }
                _url = NoTrailSlash(Uri.AbsoluteUri);            // PERFORMANCE: do this once (immutable and is read often)
                HashCode = _url.GetHashCode();                  //  and cache the signature
            }
        }

        private static string NoTrailSlash(string value)
        {
            var _url = value.Trim();
            if (_url.EndsWith("/"))
            {
                _url = _url.Substring(0, _url.Length - 1).TrimEnd();        // standardise to strip any trailing "/"
            }
            return _url;
        }

        #region IEquatable<WebPage> for Fetch operation
        //[NotMapped]
        int HashCode { get; set; }                                          // PERFORMANCE: write once read many

        public bool Equals(WebPage other) => HashCode == other.HashCode;    //  Url.Equals(other.Url) is expensive

        public override int GetHashCode() => HashCode;
        #endregion

        //[NotMapped]
        private string _draftFilespec;

        [StringLength(511)]
        public string DraftFilespec { get; set; }

        [StringLength(511)]
        public string Filespec { get; set; }

        /// <summary>
        /// null means WebPages_trIU will determine extn then update from Host.IsXXX setting default
        /// </summary>
        public bool? NeedDownload { get; set; }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
        public virtual ICollection<WebPage> ConsumeFrom { get; set; } = new HashSet<WebPage>();

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
        public virtual ICollection<WebPage> SupplyTo { get; set; } = new HashSet<WebPage>();
    }
}
