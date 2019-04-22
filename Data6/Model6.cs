namespace Data6
{
    using System.Data.Entity;

    public partial class Model6 : DbContext
    {
        public Model6()
            : base("name=DefaultConnection")
        {
        }

        public virtual DbSet<ContentTypeToExtn> ContentTypeToExtns { get; set; }
        public virtual DbSet<WebPage> WebPages { get; set; }

        protected override void OnModelCreating(DbModelBuilder modelBuilder)
        {
            modelBuilder.Entity<ContentTypeToExtn>()
                .Property(e => e.Template)
                .IsUnicode(false);

            modelBuilder.Entity<ContentTypeToExtn>()
                .Property(e => e.Extn)
                .IsUnicode(false);
        }
    }
}
