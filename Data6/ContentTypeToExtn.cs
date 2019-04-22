namespace Data6
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel.DataAnnotations;
    using System.ComponentModel.DataAnnotations.Schema;
    using System.Data.Entity.Spatial;

    public partial class ContentTypeToExtn
    {
        [Key]
        [StringLength(100)]
        public string Template { get; set; }

        [Required]
        [StringLength(10)]
        public string Extn { get; set; }

        public bool IsText { get; set; }
    }
}
