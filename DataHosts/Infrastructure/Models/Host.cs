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

        public int? ParentId { get; set; }

        [Required]
        [StringLength(255)]
        public string HostName { get; set; }

        public bool IsHtml { get; set; }

        public bool IsCss { get; set; }

        public bool IsJs { get; set; }

        public bool IsJson { get; set; }

        public bool IsXml { get; set; }

        public bool IsImage { get; set; }

        public bool IsOther { get; set; }

        public byte Priority { get; set; }

        public int? WaitCount { get; set; }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
        public virtual ICollection<Agent> Agents { get; set; } = new HashSet<Agent>();

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
        public virtual ICollection<Host> SubDomains { get; set; } = new HashSet<Host>();

        public virtual Host ParentHost { get; set; }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
        public virtual ICollection<WebPage> WebPages { get; set; } = new HashSet<WebPage>();

        #region IEquatable<Host>
        public bool Equals(Host other) => HostName == other.HostName;
        public override int GetHashCode() => HostName.GetHashCode();
        #endregion

        public override string ToString() => $"Id[{HostId}]:\t{HostName}";
    }
}
