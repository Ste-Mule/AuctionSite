using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using TAP21_22.AlarmClock.Interface;
using TAP21_22_AuctionSite.Interface;

#nullable enable
namespace Mule
{
    [Index(nameof(SiteId),nameof(Username),IsUnique = true,Name = "UsernameUnique")]
    public class User : IUser{
        
        [Key]
        public int UserId { get; set; }

        public Site? Site { get; set; }
        public int SiteId { get; }

        public Session? Session { get; set; }
        public string? SessionId { get; set; } = null;

        [MinLength(DomainConstraints.MinUserName)]
        [MaxLength(DomainConstraints.MaxUserName)]
        public string Username { get; }
        public byte[] Password { get; set; }
        
        public List<Auction> CurrentlyWinning { get; set; } = new();
        public List<Auction> Selling { get; set; } = new();

        [NotMapped]
        private readonly string _connectionString;

        [NotMapped] 
        private readonly IAlarmClock _alarmClock;

        private User(){}
        public User(int siteId, string username, byte[] password, string connectionString, IAlarmClock alarmClock)
        {
            Username = username;
            Password = password;
            SiteId = siteId;
            _connectionString = connectionString;
            _alarmClock = alarmClock;
        }

        public void Delete()
        {
            if(CheckIfDeleted()) throw new AuctionSiteInvalidOperationException("This user appears to have been deleted");

            using (var c = new AuctionSiteDbContext(_connectionString))
            {
                var buyingAuctions = c.Auctions.Where(a => a.BuyingUserId == UserId).ToList();
                foreach (var auction in buyingAuctions)
                {
                    if (auction.EndsOn >= _alarmClock.Now) 
                        throw new AuctionSiteInvalidOperationException("This user can't be deleted until he is winning an auction");
                }

                var sellingAuctions = c.Auctions.Where(a => a.SellingUserId == UserId).ToList();
                foreach (var auction in sellingAuctions)
                {
                    if (auction.EndsOn >= _alarmClock.Now) 
                        throw new AuctionSiteInvalidOperationException("This user is currently selling an item, so he can't be deleted");
                }
            }
            
            using (var c = new AuctionSiteDbContext(_connectionString))
            {
                var auctions = c.Auctions.Where(auction => auction.BuyingUserId == UserId);
                var item = c.Users.SingleOrDefault(user => user.Username == Username && user.SiteId == SiteId);
                foreach (var auction in auctions)
                {
                    auction.BoughtBy = null;
                    auction.BuyingUserId = null;
                }

                if (item != null)
                {
                    c.Remove(item);
                    c.SaveChanges();
                }
            }
        }

        public IEnumerable<IAuction> WonAuctions()
        {
            if (CheckIfDeleted()) throw new AuctionSiteInvalidOperationException("This user appears to have been deleted");
            var auctionList = new List<IAuction>();
            using (var c = new AuctionSiteDbContext(_connectionString))
            {
                var wonAuctions = c.Auctions.Where(auction =>
                    auction.BoughtBy!.Username == Username && auction.BoughtBy.SiteId == SiteId).Include(a => a.Soldby).ToList();
                foreach (var auction in wonAuctions)
                {
                    if(auction.EndsOn <= _alarmClock.Now) 
                        auctionList.Add(new Auction(auction.AuctionId, auction.SellingUserId, auction.Soldby!, SiteId, auction.Description, auction.EndsOn, auction.Price,_connectionString,_alarmClock){ MaximumAmount = auction.MaximumAmount });
                }
            }
            return auctionList;
        }

        private bool CheckIfDeleted()
        {
            using (var c = new AuctionSiteDbContext(_connectionString))
            {
                var thisUser = c.Users.SingleOrDefault(u => u.SiteId == SiteId && u.Username == Username);
                if (thisUser == null) return true;
                return false;
            }
        }

        public override bool Equals(object? obj)
        {
            var item = obj as User;

            if (item == null)
            {
                return false;
            }

            return SiteId == item.SiteId && Username == item.Username;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(SiteId,Username);
        }
    }
}