using System;
using Microsoft.Data.SqlClient;
using TAP21_22.AlarmClock.Interface;
using TAP21_22_AuctionSite.Interface;
namespace Mule
{
    public class HostFactory : IHostFactory
    {
        public void CreateHost(string connectionString)
        {
            if (connectionString == null)
                throw new AuctionSiteArgumentNullException("Connection string cannot be null");
            try
            {
                using (var c = new AuctionSiteDbContext(connectionString))
                {
                    c.Database.EnsureDeleted();
                    c.Database.EnsureCreated();
                }
            }
            catch (SqlException e)
            {
                throw new AuctionSiteUnavailableDbException("Unavailable db or malformed string", e);
            }
        }

        public IHost LoadHost(string connectionString, IAlarmClockFactory alarmClockFactory)
        {
            if (connectionString == null)
                throw new AuctionSiteArgumentNullException("Connection string cannot be null");
            if (alarmClockFactory == null)
                throw new AuctionSiteArgumentNullException("AlarmClockFactory cannot be null");
            try
            {
                using (var c = new AuctionSiteDbContext(connectionString))
                {
                    c.Database.EnsureCreated();
                }
            }
            catch (SqlException e)
            {
                throw new AuctionSiteUnavailableDbException("Unavailable db or malformed string", e);
            }
            return new Host(connectionString, alarmClockFactory);
        }
    }
}
