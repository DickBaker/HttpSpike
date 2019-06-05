namespace Infrastructure.Models
{
    using System.ComponentModel.DataAnnotations;

    public class ContentTypeToExtn
    {
        public const int EXTNSIZE = 10;
        public ContentTypeToExtn() { }

        public ContentTypeToExtn(string template, string extn, bool isText = false)
        {
            Template = template;
            Extn = extn;
            IsText = isText;
        }

        [Key]
        [StringLength(100)]
        public string Template { get; set; }

        [Required]
        [StringLength(EXTNSIZE)]
        public string Extn { get; set; }

        public bool IsText { get; set; }
    }
}
