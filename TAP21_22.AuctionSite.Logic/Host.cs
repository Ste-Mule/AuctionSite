using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using TAP21_22.AlarmClock.Interface;
using TAP21_22_AuctionSite.Interface;

namespace Mule
{
    internal class Host : IHost
    {
        private readonly string _connectionString;
        private readonly IAlarmClockFactory _alarmClockFactory;

        public Host(string connectionString, IAlarmClockFactory alarmClockFactory)
        {
            _connectionString = connectionString;
            _alarmClockFactory = alarmClockFactory;
        }

        private void CheckSiteName(string name)
        {
            if (name == null) throw new AuctionSiteArgumentNullException("Site name cannot be null");
            if (name.Length < DomainConstraints.MinSiteName || name.Length > DomainConstraints.MaxSiteName)
                throw new AuctionSiteArgumentException($"Site name length must be between {DomainConstraints.MinSiteName} and {DomainConstraints.MaxSiteName}", nameof(name));
        }

        public void CreateSite(string name, int timezone, int sessionExpirationTimeInSeconds,
            double minimumBidIncrement)
        {
            CheckSiteName(name);
            if (timezone < DomainConstraints.MinTimeZone || timezone > DomainConstraints.MaxTimeZone)
                throw new AuctionSiteArgumentOutOfRangeException(nameof(timezone), timezone,
                    "timezone must be an integer between -12 and 12");
            if (sessionExpirationTimeInSeconds <= 0)
                throw new AuctionSiteArgumentOutOfRangeException(nameof(sessionExpirationTimeInSeconds),
                    sessionExpirationTimeInSeconds, "expiration time must be positive");
            if (minimumBidIncrement <= 0)
                throw new AuctionSiteArgumentOutOfRangeException(nameof(minimumBidIncrement),
                    minimumBidIncrement, "minimum bid increment must be positive");
            var newSite = new Site(name, timezone, sessionExpirationTimeInSeconds, minimumBidIncrement,
                _alarmClockFactory.InstantiateAlarmClock(timezone),_connectionString);
            using (var c = new AuctionSiteDbContext(_connectionString))
            {
                var existingSite = c.Sites.SingleOrDefault(s => s.Name == name);
                if (existingSite != null)
                    throw new AuctionSiteNameAlreadyInUseException(name, "This name is already used for another site");
                c.Sites.Add(newSite);
                try
                {
                    c.SaveChanges();
                }
                catch (AuctionSiteNameAlreadyInUseException e)
                {
                    throw new AuctionSiteNameAlreadyInUseException(name, "This name is already used for another site", e);
                }
            }
        }

        public IEnumerable<(string Name, int TimeZone)> GetSiteInfos()
        {
            using (var c = new AuctionSiteDbContext(_connectionString))
            {
                IQueryable<Site> sites;
                try
                {
                    sites = c.Sites.AsQueryable();
                }
                catch (SqlException e)
                {
                    throw new AuctionSiteUnavailableDbException("Unexpected error",e);
                }
                foreach (var site in sites)
                {
                    yield return (site.Name, site.Timezone);
                }
            }
        }

        public ISite LoadSite(string name)
        {
            CheckSiteName(name);
            using (var c = new AuctionSiteDbContext(_connectionString))
            {
                try
                {
                    var site = c.Sites.SingleOrDefault(s => s.Name == name);
                    if (site == null) throw new AuctionSiteInexistentNameException(name, "this site name is nonexistent");
                    var alarmClock = _alarmClockFactory.InstantiateAlarmClock(site.Timezone);
                    var alarm = alarmClock.InstantiateAlarm(300000);
                    var newSite = new Site(site.Name, site.Timezone, site.SessionExpirationInSeconds, site.MinimumBidIncrement, _alarmClockFactory.InstantiateAlarmClock(site.Timezone), _connectionString) { SiteId = site.SiteId };
                    alarm.RingingEvent += newSite.OnRingingEvent;
                    return newSite;

                }
                catch (SqlException e)
                {
                    throw new AuctionSiteUnavailableDbException("Unexpected DB error",e);
                }
            }
        }
    }
}