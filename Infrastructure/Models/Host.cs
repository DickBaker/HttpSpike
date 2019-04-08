namespace Infrastructure.Models
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel.DataAnnotations;

    public class Host : IEquatable<Host>
    {
        public Host() { }

        public Host(string hostName, bool isHtml = false, bool isCss = true, bool isJs = true, bool isJson = true, bool isXml = true, bool isOther = true)
        {
            HostName = hostName;
            IsHtml = isHtml;
            IsCss = isCss;
            IsJs = isJs;
            IsJson = isJson;
            IsXml = isXml;
            IsOther = isOther;
        }

        public int HostId { get; set; }

        [Required]
        [StringLength(255)]
        public string HostName { get; set; }

        public bool IsHtml { get; set; }

        public bool IsCss { get; set; }

        public bool IsJs { get; set; }

        public bool IsJson { get; set; }

        public bool IsXml { get; set; }

        public bool IsOther { get; set; }

        public bool Equals(Host other) => HostName == other.HostName;
        public override int GetHashCode() => HostName.GetHashCode();

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
        public virtual ICollection<WebPage> WebPages { get; set; } = new HashSet<WebPage>();
    }
}
