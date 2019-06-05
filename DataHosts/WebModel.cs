namespace DataHosts
{
    using System.Data.Entity;
    using Infrastructure.Models;

    public partial class WebModel : DbContext
    {
        public WebModel()
            : base("name=DefaultConnection")
        {
        }

        public virtual DbSet<Agent> Agents { get; set; }
        public virtual DbSet<Boost> Boosts { get; set; }
        public virtual DbSet<ContentTypeToExtn> ContentTypeToExtns { get; set; }
        public virtual DbSet<Downloading> Downloadings { get; set; }
        public virtual DbSet<Host> Hosts { get; set; }
        public virtual DbSet<WebPage> WebPages { get; set; }

        protected override void OnModelCreating(DbModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Agent>()
                .HasMany(e => e.Downloadings)
                .WithOptional(e => e.Agent)
                .WillCascadeOnDelete();

            modelBuilder.Entity<Boost>()
                .Property(e => e.Scheme)
                .IsFixedLength()
                .IsUnicode(false);

            modelBuilder.Entity<ContentTypeToExtn>()
                .Property(e => e.Template)
                .IsUnicode(false);

            modelBuilder.Entity<ContentTypeToExtn>()
                .Property(e => e.Extn)
                .IsUnicode(false);

            modelBuilder.Entity<Host>()
                .HasMany(e => e.Agents)
                .WithOptional(e => e.Host)
                .HasForeignKey(e => e.PrefHostId);

            modelBuilder.Entity<Host>()
                .HasMany(e => e.SubDomains)
                .WithOptional(e => e.ParentHost)
                .HasForeignKey(e => e.ParentId);

            modelBuilder.Entity<Host>()
                .HasMany(e => e.WebPages)
                .WithOptional(e => e.Host)
                .WillCascadeOnDelete();

            modelBuilder.Entity<WebPage>()
                .Property(e => e.DraftExtn)
                .IsUnicode(false);

            modelBuilder.Entity<WebPage>()
                .Property(e => e.FinalExtn)
                .IsUnicode(false);

            modelBuilder.Entity<WebPage>()
                .HasOptional(e => e.Downloading)
                .WithRequired(e => e.WebPage)
                .WillCascadeOnDelete();

            modelBuilder.Entity<WebPage>()
                .HasMany(e => e.ConsumeFrom)
                .WithMany(e => e.SupplyTo)
                .Map(m => m.ToTable("Depends").MapLeftKey("ChildId").MapRightKey("ParentId"));
        }
    }
}
