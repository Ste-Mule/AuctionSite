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
    [Index(nameof(Name),IsUnique = true,Name = "NameUnique")]
    public class Site : ISite
    {
        [Key]
        public int SiteId { get; set; }

        public List<User> Users { get; set; } = new();
        public List<Auction> Auctions { get; set; } = new();
        public List<Session> Sessions { get; set; } = new();

        [NotMapped] 
        private readonly IAlarmClock _alarmClock;
        [NotMapped]
        private readonly string _connectionString;

        [MinLength(DomainConstraints.MaxSiteName)]
        [MaxLength(DomainConstraints.MaxSiteName)]
        public string Name { get; }
        
        [Range(DomainConstraints.MinTimeZone,DomainConstraints.MaxTimeZone)]
        public int Timezone { get; set; }

        [Range(1,int.MaxValue)]
        public int SessionExpirationInSeconds { get; set; }

        [Range(double.Epsilon,double.PositiveInfinity)]
        public double MinimumBidIncrement { get; set; }

        private Site(){}

        public Site(string name, int timeZone, int sessionExpirationInSeconds, double minimumBidIncrement,IAlarmClock alarmClock, string connectionString)
        {
            Name = name;
            Timezone = timeZone;
            SessionExpirationInSeconds = sessionExpirationInSeconds;
            MinimumBidIncrement = minimumBidIncrement;
            _alarmClock = alarmClock;
            _connectionString = connectionString;
        }

        private void CheckUsernameAndPassword(string username, string password)
        {
            if (username == null) throw new AuctionSiteArgumentNullException("Username cannot be null");
            if (password == null) throw new AuctionSiteArgumentNullException("Password cannot be null");
            if (username.Length < DomainConstraints.MinUserName || username.Length > DomainConstraints.MaxUserName)
                throw new AuctionSiteArgumentException($"The username length must be beteween {DomainConstraints.MinUserName} and {DomainConstraints.MaxUserName}", nameof(username));
            if (password.Length < DomainConstraints.MinUserPassword) throw new AuctionSiteArgumentException($"The password length must be at least {DomainConstraints.MinUserPassword}", nameof(password));
        }

        public void CreateUser(string username, string password)
        {
            if (CheckIfDeleted()) throw new AuctionSiteInvalidOperationException("This site appears to have been deleted");
            CheckUsernameAndPassword(username,password);
            User? sameNameUser;
            using (var c = new AuctionSiteDbContext(_connectionString))
            {
                sameNameUser = c.Users.SingleOrDefault(u => u.Username == username);
            }
            if (sameNameUser != null) throw new AuctionSiteNameAlreadyInUseException(username);

            var newUser = new User(SiteId, username, AuctionSiteUtilities.CreateHash(password), _connectionString,_alarmClock);
            using (var c = new AuctionSiteDbContext(_connectionString))
            {
                c.Users.Add(newUser);
                try
                {
                    c.SaveChanges();
                }
                catch (AuctionSiteNameAlreadyInUseException e)
                {
                    throw new AuctionSiteNameAlreadyInUseException(username, e.Message, e);
                }

            }

        }

        public void Delete()
        {
            if(CheckIfDeleted()) throw new AuctionSiteInvalidOperationException("This site appears to have been deleted");
            using (var c = new AuctionSiteDbContext(_connectionString))
            {
                var item = c.Sites.SingleOrDefault(site => site.Name == Name);
                if (item != null)
                {
                    c.Remove(item);
                    c.SaveChanges();
                }
            }
        }

        public ISession? Login(string username, string password)
        {
            if(CheckIfDeleted()) throw new AuctionSiteInvalidOperationException("This site appears to have been deleted");
            CheckUsernameAndPassword(username,password);
            
            using (var c = new AuctionSiteDbContext(_connectionString))
            {
                var user = c.Users.Include(u => u.Session).SingleOrDefault(u =>
                    u.Username == username && u.SiteId == SiteId &&
                    u.Password == AuctionSiteUtilities.CreateHash(password));

                if (user == null) return null;
                if (user.SessionId == null)
                {
                    var newSession = new Session(SiteId, user.UserId,
                        new User(SiteId, username, AuctionSiteUtilities.CreateHash(password), _connectionString,_alarmClock),
                        _alarmClock.Now.AddSeconds(SessionExpirationInSeconds), _connectionString, _alarmClock){Id = user.UserId.ToString()};
                    user.SessionId = newSession.Id;
                    c.Sessions.Add(newSession);
                    c.SaveChanges();
                    return newSession;
                }
                else
                {
                    var session = user.Session??throw new AuctionSiteUnavailableDbException("Unexpected error");
                    session.ValidUntil = _alarmClock.Now.AddSeconds(SessionExpirationInSeconds);
                    c.Update(session);
                    c.SaveChanges();
                    var newSession = new Session(SiteId, user.UserId,
                        new User(SiteId, username, AuctionSiteUtilities.CreateHash(password), _connectionString, _alarmClock),
                        session.DbValidUntil, _connectionString, _alarmClock){Id = session.Id};
                    return newSession;
                }
            }
        }

        public DateTime Now()
        {
            if (CheckIfDeleted()) throw new AuctionSiteInvalidOperationException("This site appears to have been deleted");
            return _alarmClock.Now;
        }

        public IEnumerable<IAuction> ToyGetAuctions(bool onlyNotEnded)
        {
            if (CheckIfDeleted()) throw new AuctionSiteInvalidOperationException("This site appears to have been deleted");
            var auctionList = new List<IAuction>();
            using (var c = new AuctionSiteDbContext(_connectionString))
            {
                
                
                var auctions = c.Auctions.Where(a => a.SiteId == SiteId).Include(a => a.Soldby);
                if (onlyNotEnded)
                {
                    foreach (var auction in auctions)
                    {
                        if (auction.EndsOn >= _alarmClock.Now) 
                            auctionList.Add(new Auction(auction.AuctionId, auction.SellingUserId,auction.Soldby!,SiteId,auction.Description,auction.EndsOn, auction.Price, _connectionString,_alarmClock){MaximumAmount = auction.MaximumAmount});
                    }
                }
                else
                {
                    foreach (var auction in auctions)
                    {
                        auctionList.Add(new Auction(auction.AuctionId, auction.SellingUserId, auction.Soldby!, SiteId, auction.Description, auction.EndsOn, auction.Price, _connectionString,_alarmClock){ MaximumAmount = auction.MaximumAmount });
                    }
                }

                return auctionList;
            }
        }

        public IEnumerable<ISession> ToyGetSessions()
        {
            if (CheckIfDeleted()) throw new AuctionSiteInvalidOperationException("This site appears to have been deleted");
            var sessionList = new List<ISession>();
            using (var c = new AuctionSiteDbContext(_connectionString))
            {
                var sessions = c.Sessions.Include(s => s.Owner).Where(s => s.SiteId == SiteId);
                foreach (var session in sessions)
                {
                    sessionList.Add(new Session(SiteId,session.UserId,session.Owner!,session.DbValidUntil,_connectionString, _alarmClock){Id = session.Id});
                }
            }

            return sessionList;
        }

        public IEnumerable<IUser> ToyGetUsers()
        {
            if (CheckIfDeleted()) throw new AuctionSiteInvalidOperationException("This site appears to have been deleted");
            var userList = new List<IUser>();
            using (var c = new AuctionSiteDbContext(_connectionString))
            {
                var users = c.Users.Where(u => u.SiteId == SiteId);
                foreach (var user in users)
                {
                    userList.Add(new User(SiteId,user.Username,user.Password,_connectionString,_alarmClock));
                }
            }

            return userList;
        }
        public void OnRingingEvent()
        {
            using (var c = new AuctionSiteDbContext(_connectionString))
            {
                var sessionsToClean =
                    c.Sessions.Where(s => s.SiteId == SiteId && s.DbValidUntil <= _alarmClock.Now);

                c.Sessions.RemoveRange(sessionsToClean);
                c.SaveChanges();


            }
        }

        private bool CheckIfDeleted()
        {
            using (var c = new AuctionSiteDbContext(_connectionString))
            {
                var thisSite = c.Sites.SingleOrDefault(s => s.Name == Name);
                if (thisSite == null) return true;
                return false;
            }
        }

        public override bool Equals(object? obj)
        {
            var item = obj as Site;

            if (item == null)
            {
                return false;
            }

            return Name.Equals(item.Name);
        }

        public override int GetHashCode()
        {
            return Name.GetHashCode();
        }
    }
}
