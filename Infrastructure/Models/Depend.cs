namespace Infrastructure.Models
{
    using System;
    using System.ComponentModel.DataAnnotations;
    using System.ComponentModel.DataAnnotations.Schema;

    public class Depend : IEquatable<Depend>
    {
        public Depend() { }

        public Depend(WebPage parent, WebPage child)
        {
            Parent = parent;
            Child = child;
        }

        [Key]
        [Column(Order = 0)]
        [DatabaseGenerated(DatabaseGeneratedOption.None)]
        public int ParentId { get; set; }

        [Key]
        [Column(Order = 1)]
        [DatabaseGenerated(DatabaseGeneratedOption.None)]
        public int ChildId { get; set; }

        [DatabaseGenerated(DatabaseGeneratedOption.Computed)]
        public byte Delta { get; set; }

        public virtual WebPage Parent { get; set; }

        public virtual WebPage Child { get; set; }

        // Delta is ignored for equality comparison purposes
        public bool Equals(Depend other) => ParentId == other.ParentId && ChildId == other.ChildId;

        public override int GetHashCode() => ParentId.GetHashCode() ^ ChildId.GetHashCode();
    }
}
