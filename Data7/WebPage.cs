namespace Data7
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel.DataAnnotations;
    using System.ComponentModel.DataAnnotations.Schema;
    using System.Data.Entity.Spatial;

    public partial class WebPage
    {
        [Key]
        public int PageId { get; set; }

        //public int? HostId { get; set; }

        [Required]
        [StringLength(450)]
        public string Url { get; set; }

        [StringLength(260)]
        public string DraftFilespec { get; set; }

        [StringLength(260)]
        public string Filespec { get; set; }

        public bool? NeedDownload { get; set; }

        public bool NeedLocalise { get; set; }

        //[DatabaseGenerated(DatabaseGeneratedOption.Computed)]
        //[StringLength(99)]
        //public string DraftExtn { get; set; }

        //[DatabaseGenerated(DatabaseGeneratedOption.Computed)]
        //[StringLength(99)]
        //public string FinalExtn { get; set; }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
        public virtual ICollection<WebPage> ConsumeFrom { get; set; } = new HashSet<WebPage>();

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
        public virtual ICollection<WebPage> SupplyTo { get; set; } = new HashSet<WebPage>();
    }
}
