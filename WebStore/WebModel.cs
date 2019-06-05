namespace Webstore
{
    using System.Data.Entity;
    using Infrastructure.Models;

    public class WebModel : DbContext
    {
        /*
        The target context 'Webstore.WebModel' is not constructible. Add a default constructor or provide an implementation of IDbContextFactory.
        */
        public WebModel() : base("name=DefaultConnection")          // would generate Webstore.WebModel db unless given overload
        { }
        //public WebModel(string config) : base(config)               // => System.Console.WriteLine(Database.Connection.ConnectionTimeout);
        //{ }

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
