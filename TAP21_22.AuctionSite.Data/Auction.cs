using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using TAP21_22.AlarmClock.Interface;
using TAP21_22_AuctionSite.Interface;

#nullable enable
namespace Mule
{
    public class Auction : IAuction
    {
        [Key]
        public int AuctionId { get; set; }
        
        [NotMapped]
        public int Id { get; }

        public User? Soldby { get; set; }
        public int SellingUserId { get; set; }

        public User? BoughtBy { get; set; }
        public int? BuyingUserId { get; set; }

        public Site? Site { get; set; }
        public int SiteId { get; }

        [NotMapped]
        private readonly string _connectionString;
        [NotMapped]
        public IUser Seller { get; set; }

        [NotMapped] 
        private readonly IAlarmClock _alarmClock;

        public string Description { get; set; }

        public DateTime EndsOn { get; set; }

        public double Price { get; set; }

        public double MaximumAmount { get; set; } = 0;

        private Auction(){}

        public Auction(int id, int sellingUserId, IUser seller, int siteId, string description, DateTime endsOn, double price, string connectionString, IAlarmClock alarmClock)
        {
            Id = id;
            SellingUserId = sellingUserId;
            Seller = seller;
            SiteId = siteId;
            Description = description;
            EndsOn = endsOn;
            _connectionString = connectionString;
            Price = price;
            _alarmClock = alarmClock;
        }

        public bool Bid(ISession session, double offer)
        {
            if (CheckIfDeleted() || EndsOn < _alarmClock.Now)
                throw new AuctionSiteInvalidOperationException("The auction is over or has been deleted");
            if (offer < 0)
                throw new AuctionSiteArgumentOutOfRangeException(nameof(offer), offer,
                    "offer must be a positive integer");
            if (session == null) throw new AuctionSiteArgumentNullException("The session passed is null");
            using (var c = new AuctionSiteDbContext(_connectionString))
            {
                var checkSession = c.Sessions.SingleOrDefault(s => s.Id == session.Id);
                if(checkSession == null) throw new AuctionSiteArgumentException("The session is not valid");
            }
            User? bidder;
            var sessionUser = (User)session.User;
            using (var c = new AuctionSiteDbContext(_connectionString))
            {
                bidder = c.Users.SingleOrDefault(u => u.Username == sessionUser.Username && u.SiteId == sessionUser.SiteId);
                if (bidder == null) throw new AuctionSiteInvalidOperationException("The bidder can't be null");
            }
            if (session.ValidUntil < _alarmClock.Now || bidder.Equals(Seller) || bidder.SiteId != SiteId)
                throw new AuctionSiteArgumentException("The session or the buyer are invalid");
            Auction? thisAuction;
            using (var c = new AuctionSiteDbContext(_connectionString))
            {
                thisAuction = c.Auctions.Include(a => a.BoughtBy).Include(a => a.Site).SingleOrDefault(a => a.SiteId == SiteId && a.AuctionId == Id);
            }
            if (thisAuction == null) throw new AuctionSiteInvalidOperationException("The auction is over or has been deleted");
            if (thisAuction.BuyingUserId == bidder.UserId &&
                offer < thisAuction.MaximumAmount + thisAuction.Site!.MinimumBidIncrement) return false;
            if (thisAuction.BuyingUserId != bidder.UserId && offer < thisAuction.Price) return false;
            if (thisAuction.BuyingUserId != bidder.UserId &&
                offer < thisAuction.Price + thisAuction.Site!.MinimumBidIncrement &&
                thisAuction.MaximumAmount != 0) return false;
            using (var c = new AuctionSiteDbContext(_connectionString))
            {
                var site = c.Sites.SingleOrDefault(s => s.SiteId == SiteId);
                var ss = c.Sessions.SingleOrDefault(s => s.Id == session.Id);
                if (ss==null) throw new AuctionSiteArgumentException("The session is not valid");
                ss.DbValidUntil = _alarmClock.Now.AddSeconds(site!.SessionExpirationInSeconds);
                c.SaveChanges();
            }
            if (thisAuction.MaximumAmount == 0)
            {
                thisAuction.MaximumAmount = offer;
                thisAuction.BuyingUserId = bidder.UserId;
                using (var c = new AuctionSiteDbContext(_connectionString))
                {
                    c.Auctions.Update(thisAuction);
                    thisAuction.BuyingUserId = bidder.UserId;
                    c.SaveChanges();
                }

                return true;
            }

            if (bidder.Equals(thisAuction.BoughtBy))
            {
                thisAuction.MaximumAmount = offer;
                using (var c = new AuctionSiteDbContext(_connectionString))
                {
                    c.Auctions.Update(thisAuction);
                    c.SaveChanges();
                }

                return true;
            }

            if (thisAuction.MaximumAmount != 0 && !bidder.Equals(thisAuction.BoughtBy) &&
                offer > thisAuction.MaximumAmount)
            {
                double min;
                if (offer < thisAuction.MaximumAmount + thisAuction.Site!.MinimumBidIncrement) min = offer;
                else
                {
                    min = thisAuction.MaximumAmount + thisAuction.Site!.MinimumBidIncrement;
                }

                thisAuction.Price = min;
                thisAuction.MaximumAmount = offer;
                thisAuction.BuyingUserId = bidder.UserId;
                using (var c = new AuctionSiteDbContext(_connectionString))
                {
                    c.Auctions.Update(thisAuction);
                    thisAuction.BuyingUserId = bidder.UserId;
                    c.SaveChanges();
                }

                return true;
            }

            double minn;
            if (thisAuction.MaximumAmount < offer + thisAuction.Site!.MinimumBidIncrement)
                minn = thisAuction.MaximumAmount;
            else
            {
                minn = offer + thisAuction.Site!.MinimumBidIncrement;
            }

            thisAuction.Price = minn;
            using (var c = new AuctionSiteDbContext(_connectionString))
            {
                c.Auctions.Update(thisAuction);
                c.SaveChanges();
            }

            return true;
        }

        public double CurrentPrice()
        {
            if (CheckIfDeleted()) throw new AuctionSiteInvalidOperationException("This auction appears to have been deleted");
            using (var c = new AuctionSiteDbContext(_connectionString))
            {
                var thisAuction = c.Auctions.SingleOrDefault(a => a.SiteId == SiteId && a.AuctionId == Id);
                if(thisAuction == null) throw new AuctionSiteInvalidOperationException("The auction has been deleted");
                return thisAuction.Price;
            }
        }

        public IUser? CurrentWinner()
        {
            if (CheckIfDeleted()) throw new AuctionSiteInvalidOperationException("This auction appears to have been deleted");
            using (var c = new AuctionSiteDbContext(_connectionString))
            {
                var thisAuction = c.Auctions.Include(a => a.BoughtBy).SingleOrDefault(a => a.SiteId == SiteId && a.AuctionId == Id);
                if (thisAuction!.BoughtBy == null) return null;
                return new User(SiteId, thisAuction.BoughtBy.Username, thisAuction.BoughtBy.Password, _connectionString,
                    _alarmClock);
            }
        }

        public void Delete()
        {
            if (CheckIfDeleted())
                throw new AuctionSiteInvalidOperationException("This auction appears to have been deleted");
            using (var c = new AuctionSiteDbContext(_connectionString))
            {
                var thisAuction = c.Auctions.SingleOrDefault(a => a.AuctionId == Id && a.SiteId == SiteId);
                if (thisAuction != null)
                {
                    c.Remove(thisAuction);
                    c.SaveChanges();
                }
            }
        }

        private bool CheckIfDeleted()
        {
            using (var c = new AuctionSiteDbContext(_connectionString))
            {
                var thisAuction = c.Auctions.SingleOrDefault(a => a.AuctionId == Id && a.SiteId == SiteId);
                if (thisAuction == null) return true;
                return false;
            }
        }

        public override bool Equals(object? obj)
        {
            var item = obj as Auction;

            if (item == null)
            {
                return false;
            }

            return SiteId == item.SiteId && Id == item.Id;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(SiteId, Id);
        }
    }
}