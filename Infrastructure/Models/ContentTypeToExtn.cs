namespace Infrastructure.Models
{
    using System.ComponentModel.DataAnnotations;

    public class ContentTypeToExtn // : IEquatable<ContentTypeToExtn>
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

        //#region IEquatable<ContentTypeToExtn>
        //public override bool Equals(object obj) => (obj is ContentTypeToExtn other) && Equals(other);
        //public bool Equals(ContentTypeToExtn other) => Template.Equals(other.Template, StringComparison.InvariantCultureIgnoreCase);
        //public override int GetHashCode() => Template.GetHashCode();
        //#endregion
    }
}
