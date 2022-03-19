using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using Microsoft.EntityFrameworkCore;
using TAP21_22.AlarmClock.Interface;
using TAP21_22_AuctionSite.Interface;

#nullable enable
namespace Mule
{
    public class Session : ISession
    {
        [Key]
        public string Id { get; set; }

        public User? Owner { get; set; }
        public int UserId { get; set; }

        public Site? Site { get; set; }
        public int SiteId { get; set; }

        public DateTime DbValidUntil { get; set; }
        
        [NotMapped]
        public DateTime ValidUntil {
            get
            {
                using (var c = new AuctionSiteDbContext(_connectionString))
                {
                    var thisSession = c.Sessions.SingleOrDefault(s => s.Id == Id);
                    if (thisSession == null) return DbValidUntil;
                    return thisSession.DbValidUntil;
                }
            }
            set
            {
                DbValidUntil = value;
            }
        }
        [NotMapped]
        public IUser User { get; set; }

        [NotMapped] 
        private readonly string _connectionString;

        [NotMapped] private IAlarmClock _alarmClock;

        private Session(){}

        public Session(int siteId, int userId, User user,DateTime validUntil, string connectionString, IAlarmClock alarmClock)
        {
            Id = userId.ToString();
            _connectionString = connectionString;
            SiteId = siteId;
            UserId = userId;
            ValidUntil = validUntil;
            Id = userId.ToString();
            User = user;
            _alarmClock = alarmClock;
        }

        public IAuction CreateAuction(string description, DateTime endsOn, double startingPrice)
        {
            if (CheckIfDeleted())
                throw new AuctionSiteInvalidOperationException("This session has expired");
            using (var c = new AuctionSiteDbContext(_connectionString))
            {
                if(_alarmClock.Now > ValidUntil) throw new AuctionSiteInvalidOperationException("This session has expired");
            }
            if (description == null) throw new AuctionSiteArgumentNullException("Description cannot be null");
            if (description == "")
                throw new AuctionSiteArgumentException("The description cannot be empty", nameof(description));
            if (startingPrice < 0)
                throw new AuctionSiteArgumentOutOfRangeException(nameof(startingPrice), startingPrice,
                    "The starting price must be a positive integer");
            if (endsOn < _alarmClock.Now)
                throw new AuctionSiteUnavailableTimeMachineException("endsOn cannot precede the current time");
            using (var c = new AuctionSiteDbContext(_connectionString))
            {
                var user = c.Users.SingleOrDefault(u => u.SiteId == SiteId && u.Username == User.Username);
                if (user == null) throw new AuctionSiteUnavailableDbException("Unexpected Error");
                var newAuction = new Auction(0, user.UserId,
                    new User(SiteId, user.Username, user.Password, _connectionString, _alarmClock), 
                    SiteId, description, endsOn, startingPrice, _connectionString,_alarmClock);
                c.Auctions.Add(newAuction);
                var site = c.Sites.SingleOrDefault(s => s.SiteId == SiteId);
                ValidUntil = _alarmClock.Now.AddSeconds(site!.SessionExpirationInSeconds);
                var thisSession = c.Sessions.SingleOrDefault(s => s.Id == Id);
                thisSession!.DbValidUntil = _alarmClock.Now.AddSeconds(site!.SessionExpirationInSeconds);
                c.SaveChanges();
                return new Auction(newAuction.AuctionId, newAuction.SellingUserId,
                    new User(user.SiteId,user.Username,user.Password,_connectionString,_alarmClock)
                    ,newAuction.SiteId,description,endsOn,startingPrice,_connectionString,_alarmClock);
            }
        }

        public void Logout()
        {
            if (CheckIfDeleted()) throw new AuctionSiteInvalidOperationException("You have already logged out");
            using (var c = new AuctionSiteDbContext(_connectionString))
            {
                var thisSession = c.Sessions.SingleOrDefault(s => s.Id == Id);
                if (thisSession != null)
                {
                    c.Remove(thisSession);
                    c.SaveChanges();
                }
            }
        }

        private bool CheckIfDeleted()
        {
            using (var c = new AuctionSiteDbContext(_connectionString))
            {
                var thisSession = c.Sessions.SingleOrDefault(s => s.Id == Id);
                if (thisSession == null) return true;
                return false;
            }
        }

        public override bool Equals(object? obj)
        {
            var item = obj as Session;

            if (item == null)
            {
                return false;
            }

            return Id.Equals(item.Id);
        }

        public override int GetHashCode()
        {
            return Id.GetHashCode();
        }
    }
}