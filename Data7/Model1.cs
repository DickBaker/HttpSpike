namespace Data7
{
    using System;
    using System.Data.Entity;
    using System.ComponentModel.DataAnnotations.Schema;
    using System.Linq;

    public partial class Model1 : DbContext
    {
        public Model1()
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

            modelBuilder.Entity<WebPage>()
                .HasMany(e => e.ConsumeFrom)
                .WithMany(e => e.SupplyTo)
                .Map(m => m.ToTable("Depends").MapLeftKey("ChildId").MapRightKey("ParentId"));

            //modelBuilder.Entity<WebPage>()
            //     .HasMany(e => e.ConsumeFrom)
            //     .WithMany(e => e.SupplyTo)
            //     .Map(m => m.ToTable("Depends").MapLeftKey("ChildId").MapRightKey("ParentId"));
        }
    }
}
