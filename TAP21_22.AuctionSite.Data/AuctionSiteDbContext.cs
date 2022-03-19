using System;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using TAP21_22_AuctionSite.Interface;
namespace Mule
{
    public class AuctionSiteDbContext:TapDbContext
    {
        public AuctionSiteDbContext(string connectionString) : base(new DbContextOptionsBuilder<AuctionSiteDbContext>().UseSqlServer(connectionString).Options) { }
        

        public override int SaveChanges()
        {
            try
            {
                return base.SaveChanges();
            }
            catch (SqlException e)
            {
                throw new AuctionSiteUnavailableDbException("Unavailable Db", e);
            }
            catch (DbUpdateException e)
            {
                var sqlException = e.InnerException as SqlException;
                if (sqlException == null) throw new AuctionSiteUnavailableDbException("Missing information from Db",e);
                switch (sqlException.Number)
                {
                    case 2601: throw new AuctionSiteNameAlreadyInUseException("Sql error:2601");
                    default:
                        throw new AuctionSiteUnavailableDbException("Missing information form Db exception", e);
                }
            }
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var u = modelBuilder.Entity<User>();
            u.HasOne(user => user.Session).WithOne(session => session.Owner).HasForeignKey<Session>(session => session.UserId).OnDelete(DeleteBehavior.Cascade);
            u.HasMany(user => user.Selling).WithOne(auction => auction.Soldby).HasForeignKey(auction => auction.SellingUserId)
                .OnDelete(DeleteBehavior.Cascade);
            u.HasMany(user => user.CurrentlyWinning).WithOne(auction => auction.BoughtBy)
                .HasForeignKey(auction => auction.BuyingUserId).OnDelete(DeleteBehavior.NoAction);

             var s = modelBuilder.Entity<Session>();
             s.HasOne(session => session.Site).WithMany(site => site.Sessions).HasForeignKey(session => session.SiteId)
                 .OnDelete(DeleteBehavior.NoAction);

             var a = modelBuilder.Entity<Auction>();
             a.HasOne(auction => auction.Site).WithMany(site => site.Auctions).HasForeignKey(auction => auction.SiteId)
                 .OnDelete(DeleteBehavior.NoAction);
        }

        public DbSet<Site> Sites { get; set; }
        public DbSet<Auction> Auctions { get; set; }
        public DbSet<Session> Sessions { get; set; }
        public DbSet<User> Users { get; set; }
    }
}
