namespace Infrastructure.Models
{
    using System.ComponentModel.DataAnnotations;
    using System.ComponentModel.DataAnnotations.Schema;

    [Table("Boost")]
    public partial class Boost
    {
        [Key]
        [Column(Order = 0)]
        [StringLength(1)]
        public string Scheme { get; set; }

        [Key]
        [Column(Order = 1)]
        [DatabaseGenerated(DatabaseGeneratedOption.None)]
        public int Threshold { get; set; }

        public byte Priority { get; set; }
    }
}
