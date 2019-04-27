namespace WebStore.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class InitialDDL : DbMigration
    {
        public override void Up()
        {
            CreateTable(
                "dbo.ContentTypeToExtns",
                c => new
                    {
                        Template = c.String(nullable: false, maxLength: 100, unicode: false),
                        Extn = c.String(nullable: false, maxLength: 10, unicode: false),
                        IsText = c.Boolean(nullable: false),
                    })
                .PrimaryKey(t => t.Template);
            
            CreateTable(
                "dbo.WebPages",
                c => new
                    {
                        PageId = c.Int(nullable: false, identity: true),
                        Url = c.String(nullable: false, maxLength: 450),
                        DraftFilespec = c.String(maxLength: 260),
                        Filespec = c.String(maxLength: 260),
                        Download = c.Byte(),
                        Localise = c.Byte(nullable: false),
                    })
                .PrimaryKey(t => t.PageId);
            
            CreateTable(
                "dbo.Depends",
                c => new
                    {
                        ChildId = c.Int(nullable: false),
                        ParentId = c.Int(nullable: false),
                    })
                .PrimaryKey(t => new { t.ChildId, t.ParentId })
                .ForeignKey("dbo.WebPages", t => t.ChildId)
                .ForeignKey("dbo.WebPages", t => t.ParentId)
                .Index(t => t.ChildId)
                .Index(t => t.ParentId);
            
        }
        
        public override void Down()
        {
            DropForeignKey("dbo.Depends", "ParentId", "dbo.WebPages");
            DropForeignKey("dbo.Depends", "ChildId", "dbo.WebPages");
            DropIndex("dbo.Depends", new[] { "ParentId" });
            DropIndex("dbo.Depends", new[] { "ChildId" });
            DropTable("dbo.Depends");
            DropTable("dbo.WebPages");
            DropTable("dbo.ContentTypeToExtns");
        }
    }
}
