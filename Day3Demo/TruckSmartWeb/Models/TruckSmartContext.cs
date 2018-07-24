using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Web;
using System.Configuration;
using StackExchange.Redis;
using Newtonsoft.Json;

namespace TruckSmartWeb.Models
{
    public class TruckSmartContext:DbContext
    {
        //TODO: 1. Create the infrastructure as per the instructor guide
        //TODO: 2. Update the Database and Redis connection strings in web.config
        private string driverID;
        #region Redis cache setup
        private static Lazy<ConnectionMultiplexer> lazyConnection = new Lazy<ConnectionMultiplexer>(() =>
        {
            string cacheConnection = ConfigurationManager.AppSettings["redis"];
            return ConnectionMultiplexer.Connect(cacheConnection);
        });

        public static ConnectionMultiplexer Connection
        {
            get
            {
                return lazyConnection.Value;
            }
        }

        #endregion

        static TruckSmartContext()
        {
            //var init = new TruckSmartDBInitializer();
            //init.InitializeDatabase(new TruckSmartContext());
            Database.SetInitializer<TruckSmartContext>(null);
        }

        #region Database context setup
        public TruckSmartContext():this("name=TruckSmartDB")
        {

        }
        public TruckSmartContext(string connection) : base(connection)
        {
            driverID = System.Web.HttpContext.Current.Session["DriverID"].ToString();
        }
        public DbSet<Customer> Customers { get; set; }
        public DbSet<Driver> Drivers { get; set; }
        public DbSet<Shipment> Shipments { get; set; }
        public DbSet<Trip> Trips { get; set; }
        public DbSet<ServiceProvider> ServiceProviders { get; set; }
        #endregion

        #region Shipment Management
        public List<Shipment> GetOpenShipments()
        {
            return Shipments.Include(s => s.Driver).Include(s => s.From).Include(s => s.To).Where(s => s.Driver == null).ToList();
        }
        public List<Shipment> GetMyShipments()
        {
            return Shipments.Include(s => s.Driver).Include(s => s.From).Include(s => s.To).Where(s => (s.Driver != null) && (s.Driver.DriverID == this.driverID)).ToList();

        }
        public Shipment GetShipment(Guid id)
        {
            return Shipments.Include(s => s.Driver).Include(s => s.From).Include(s => s.To).Where(s => s.ShipmentID == id).First();
        }
        public Shipment ReserveShipment(Guid id)
        {
            var shipment = Shipments.Include(s => s.Driver).Where(s => s.ShipmentID == id).First();
            //Check to make sure it is not already reserved
            if(shipment.Driver!=null)
            {
                throw new InvalidOperationException("This shipment is already reserved");
            }
            var driver = Drivers.First(d => d.DriverID == this.driverID);
            shipment.Driver = driver;
            SaveChanges();
            return shipment;

        }
        public Shipment ReleaseShipment(Guid id)
        {
            var shipment = Shipments.Include(s => s.Driver).Include(s => s.From).Include(s => s.To).Where(s => s.ShipmentID == id).First();
            if((shipment.Driver == null) || (shipment.Driver.DriverID != this.driverID))
            {
                throw new InvalidOperationException("This shipment is not reserved for the current driver.");
            }
            shipment.Driver = null;
            SaveChanges();
            return shipment;

        }
        #endregion

        #region Emergency service providers
        public List<ServiceProvider> GetProviders()
        {
            List<ServiceProvider> results = null;
            string cacheKey = "TruckSmart_Providers";
            IDatabase cache = Connection.GetDatabase();
            string cacheData = cache.StringGet(cacheKey);
            if (!string.IsNullOrEmpty(cacheData))
            {
                try
                {
                    results = JsonConvert.DeserializeObject<List<ServiceProvider>>(cacheData);
                }
                catch
                {
                    //Do something if there is an error
                }
            }
            if ((results==null) || (results.Count ==0))
            {
                results = this.ServiceProviders.ToList();
                cache.StringSet(cacheKey, JsonConvert.SerializeObject(results));
            }
            return results;
        }
        public ServiceProvider GetNearestProvider(double latitude, double longitude)
        {
            /*
            Theorectically this would do geo-based calculations to return the closest provider.
            At the moment it returns a random result.  This would be bad for an actual driver,
            but it serves our purposes here.
            */
            var providers = GetProviders();
            var id = (int) Math.Truncate(((new Random()).NextDouble() * (double)providers.Count));
            return providers[id];
        }

        #endregion
    }
}