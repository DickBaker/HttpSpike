namespace Infrastructure.Models
{
    using System;
    using System.ComponentModel.DataAnnotations;
    using System.ComponentModel.DataAnnotations.Schema;

    [Table("Downloading")]
    public partial class Downloading
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.None)]
        public int PageId { get; set; }

        public short? Spid { get; set; }

        [Column(TypeName = "smalldatetime")]
        public DateTime FirstCall { get; set; }

        [Column(TypeName = "smalldatetime")]
        public DateTime LastCall { get; set; }

        public int Retry { get; set; }

        public virtual Agent Agent { get; set; }

        public virtual WebPage WebPage { get; set; }
    }
}
