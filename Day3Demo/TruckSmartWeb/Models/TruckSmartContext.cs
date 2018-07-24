using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Web;
using System.Configuration;
using StackExchange.Redis;
using Newtonsoft.Json;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Table;
using System.Text.RegularExpressions;
using Microsoft.WindowsAzure.Storage.Blob;
using System.IO;

namespace TruckSmartWeb.Models
{
    public class TruckSmartContext : DbContext
    {
        private string driverID;
        #region redis setup
        //private static Lazy<ConnectionMultiplexer> lazyConnection = new Lazy<ConnectionMultiplexer>(() =>
        //{
        //    string cacheConnection = ConfigurationManager.ConnectionStrings["redis"].ConnectionString;
        //    return ConnectionMultiplexer.Connect(cacheConnection);
        //});

        //public static ConnectionMultiplexer Connection
        //{
        //    get
        //    {
        //        return lazyConnection.Value;
        //    }
        //}

        #endregion

        #region Database initialization
        static TruckSmartContext()
        {
            var init = new TruckSmartDBInitializer();
            init.InitializeDatabase(new TruckSmartContext());
        }
        #endregion

        #region Context object initialization
        public TruckSmartContext() : this("name=TruckSmartDB")
        {

        }
        public TruckSmartContext(string connection) : base(connection)
        {
            driverID = System.Web.HttpContext.Current.Session["DriverID"].ToString();
        }
        #endregion

        #region Data collection properties.  Core to Entity Framework
        public DbSet<Customer> Customers { get; set; }
        public DbSet<Driver> Drivers { get; set; }
        public DbSet<Shipment> Shipments { get; set; }
        public DbSet<Trip> Trips { get; set; }
        public DbSet<ServiceProvider> ServiceProviders { get; set; }
        #endregion

        #region Shipment/trip management
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
            if (shipment.Driver != null)
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
            if ((shipment.Driver == null) || (shipment.Driver.DriverID != this.driverID))
            {
                throw new InvalidOperationException("This shipment is not reserved for the current driver.");
            }
            shipment.Driver = null;
            SaveChanges();
            return shipment;

        }
        #endregion

        #region Provider management
        public List<ServiceProvider> GetProviders()
        {
            //Simplified code for using redis cache
            //List<ServiceProvider> results = null;
            //string cacheKey = "TruckSmart_Providers";
            //IDatabase cache = Connection.GetDatabase();
            //string cacheData = cache.StringGet(cacheKey);
            //if(!string.IsNullOrEmpty(cacheData))
            //{
            //    try
            //    {
            //        results = JsonConvert.DeserializeObject<List<ServiceProvider>>(cacheData);
            //    } catch
            //    {
            //        //Do something if there is an error
            //    }
            //}
            //if(results==null)
            //{
            //    results = this.ServiceProviders.ToList();
            //    cache.StringSet(cacheKey, JsonConvert.SerializeObject(results));
            //}
            List<ServiceProvider> results = this.ServiceProviders.ToList();
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
            var id = (int)Math.Truncate(((new Random()).NextDouble() * (double)providers.Count));
            return providers[id];
        }
        #endregion

        #region Expense management
        //Note: Expenses are not saved to the relational database
        private CloudTable _expenseTable = null;
        private CloudStorageAccount _acct = null;

        private CloudStorageAccount storageAccount
        {
            get
            {
                if (_acct == null)
                {
                    //TODO: 1. Storage Account connections
                    //This is pretty standard for working with storage accounts.
                    //Generally, the key should be kept in Key Vault.
                    _acct = CloudStorageAccount.Parse(ConfigurationManager.AppSettings["StorageAccount"]);
                }
                return _acct;
            }
        }
        private CloudTable expenseTable
        {
            get
            {
                if (_expenseTable == null)
                {
                    //TODO: 2. Table reference
                    //This is a pretty standard approach for working with any storage objects:
                    //1. Create a client
                    //2. Get a reference to the object
                    //3. Create the object if it doesn't exist
                    var client = storageAccount.CreateCloudTableClient();
                    _expenseTable = client.GetTableReference("expenses");
                    if (expenseTable.CreateIfNotExists())
                    {
                        generateExpenses();
                    }
                }
                return _expenseTable;
            }
        }


        public List<Expense> GetExpenses(Guid? ShipmentID = null, DateTime? From = null, DateTime? To = null)
        {
            var expenses = new List<Expense>();

            //TODO: 3. Basic querying.  
            var partitionFilter = TableQuery.GenerateFilterCondition("PartitionKey", QueryComparisons.Equal, this.driverID);
            TableQuery<Expense> query = null;
            if (!ShipmentID.HasValue && (!From.HasValue || !To.HasValue))
            {
                query = new TableQuery<Expense>().Where(partitionFilter);
            }
            if (ShipmentID.HasValue)
            {
                var shipmentFilter = string.Format("ShipmentID eq guid'{0}'", ShipmentID.Value);
                query = new TableQuery<Expense>().Where(TableQuery.CombineFilters(
                    partitionFilter,
                    TableOperators.And,
                    shipmentFilter
                    ));

            }
            if (From.HasValue && To.HasValue)
            {
                //TODO: 4. More filtering
                //There are two important aspects to this filter:
                //1. It uses the CombineFilters method to combine filters
                //2. It uses the datetime qualifier for the date leterals.  This
                //   is standard OQuery syntax.  There is also a helper method specifically for dates and other data types.
                var fromFilter = string.Format("(Date ge datetime'{0:s}')", From.Value);

                var toFilter = string.Format("(Date le datetime'{0:s}')", To.Value);

                query = new TableQuery<Expense>().Where(TableQuery.CombineFilters(
                    partitionFilter,
                    TableOperators.And,
                    TableQuery.CombineFilters(fromFilter, TableOperators.And, toFilter)
                    ));
            }

            var result = expenseTable.ExecuteQuery(query);
            return result.ToList();
        }
        public Expense GetExpense(Guid ExpenseID)
        {
            var expense = new Expense();
            var operation = TableOperation.Retrieve<Expense>(this.driverID, ExpenseID.ToString());
            var result = expenseTable.Execute(operation);
            return (Expense)result.Result;


        }
        public Expense SaveExpense(Expense NewExpense, byte[] receipt)
        {
            //Add code to save an expense record and receipt image
            if (receipt != null)
            {
                NewExpense.HasReceipt = true;
                NewExpense.ReceiptURL = saveReceipt(NewExpense.ExpenseID, "jpg", receipt);
            }
            var operation = TableOperation.Insert(NewExpense);
            expenseTable.Execute(operation);
            return NewExpense;
        }

        private string saveReceipt(Guid id, string fileType, byte[] receipt)
        {
            Regex reg = new Regex("[^a-zA-Z0-9 -]");
            string containerName = reg.Replace(this.driverID, "-").ToLower();
            string fileName = string.Format("{0}.{1}", reg.Replace(id.ToString(), ""), fileType);

            //Get or create container
            //TODO: 5. Create a container per driver
            //There is a limit of 5 policies per container but there are not limits to the 
            //number of containers that can be created in a storage account.  This is necessary
            //because each driver needs access to only their own receipt images, and that access needs
            //to be revocable on a per driver basis.
            var client = storageAccount.CreateCloudBlobClient();
            var container = client.GetContainerReference(containerName);
            if (container.CreateIfNotExists())
            {
                //Create policy
                var permissions = new BlobContainerPermissions();
                permissions.SharedAccessPolicies.Add("standard", new SharedAccessBlobPolicy
                {
                    SharedAccessExpiryTime = DateTime.UtcNow.AddYears(1),
                    Permissions = SharedAccessBlobPermissions.Read
                });
                permissions.PublicAccess = BlobContainerPublicAccessType.Off;
                container.SetPermissions(permissions);

            }


            //Store image
            var blob = container.GetBlockBlobReference(fileName);
            blob.UploadFromByteArray(receipt, 0, receipt.Length);
            //Generate SAS and return URL
            string sas = blob.GetSharedAccessSignature(new SharedAccessBlobPolicy(), "standard");
            return blob.Uri.AbsoluteUri + sas;


        }


        private void generateExpenses()
        {

            string[] hotels = { "Marriot", "Hyatt", "Westin", "Motel 6", "Days Inn" };
            string[] locations = { "Pacific Coast Highway", "Jersey Turnpike", "Route 66", "Miracle Mile", "Chisolm Trail" };
            var rnd = new Random();
            foreach (var shipment in this.GetMyShipments())
            {
                for (int lcv = 0; lcv < 5; lcv++)
                {
                    SaveExpense(new Expense
                    {
                        ExpenseID = Guid.NewGuid(),
                        DriverID = this.driverID,
                        ShipmentID = shipment.ShipmentID,
                        ExpenseType = ExpenseTypeEnum.Lodging,
                        Date = shipment.Scheduled.AddDays(-lcv),
                        Amount = (100f + 1000f * rnd.NextDouble()),
                        HasReceipt = false,
                        ReceiptURL = "",
                        Hotel = hotels[lcv],
                        Room = (lcv * 100 + 15).ToString(),
                        DirectBill = lcv % 3 == 0

                    }, null);
                    SaveExpense(new Expense
                    {
                        ExpenseID = Guid.NewGuid(),
                        DriverID = this.driverID,
                        ShipmentID = shipment.ShipmentID,
                        ExpenseType = ExpenseTypeEnum.Toll,
                        Date = shipment.Scheduled.AddDays(-lcv),
                        Amount = (.25f + 10f * rnd.NextDouble()),
                        HasReceipt = false,
                        ReceiptURL = "",
                        Location = locations[lcv]
                    }, null);
                }
            }

        }

        #endregion

    }
}