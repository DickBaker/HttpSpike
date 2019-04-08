namespace Webstore
{
    using Infrastructure.Models;
    using System.Data.Entity;

    public class WebModel : DbContext
    {
        public WebModel(string config = "name=DefaultConnection") : base(config)
        { }

        public virtual DbSet<ContentTypeToExtn> ContentTypeToExtns { get; set; }
        //public virtual DbSet<Host> Hosts { get; set; }
        public virtual DbSet<WebPage> WebPages { get; set; }

        protected override void OnModelCreating(DbModelBuilder modelBuilder)
        {
            Database.SetInitializer(new CreateDatabaseIfNotExists<WebModel>());      // default

            modelBuilder.Entity<ContentTypeToExtn>()
                .Property(e => e.Template)
                .IsUnicode(unicode: false);

            modelBuilder.Entity<ContentTypeToExtn>()
                .Property(e => e.Extn)
                .IsUnicode(unicode: false);

            modelBuilder.Entity<WebPage>()
                .HasMany(e => e.ConsumeFrom)
                .WithMany(e => e.SupplyTo)
                .Map(m => m.ToTable("Depends").MapLeftKey("ChildId").MapRightKey("ParentId"));
        }
    }
}
