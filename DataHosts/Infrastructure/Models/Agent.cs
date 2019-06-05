namespace Infrastructure.Models
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel.DataAnnotations;
    using System.ComponentModel.DataAnnotations.Schema;

    public partial class Agent
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.None)]
        public short Spid { get; set; }

        public int? PrefHostId { get; set; }

        [StringLength(450)]
        public string Url { get; set; }

        [Column(TypeName = "smalldatetime")]
        public DateTime LastCall { get; set; }

        public virtual Host Host { get; set; }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
        public virtual ICollection<Downloading> Downloadings { get; set; } = new HashSet<Downloading>();
    }
}
